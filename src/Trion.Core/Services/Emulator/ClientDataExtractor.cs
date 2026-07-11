using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Trion.Core.Logging;

namespace Trion.Core.Services.Emulator;

/// <summary>
/// Runs WoW client data extraction tools (mapextractor/ad, vmap4extractor,
/// vmap4assembler, mmaps_generator/MoveMapGen) directly via Process.Start and
/// validates the output.
///
/// Each step is silently skipped when its binary is not present in the repack,
/// so CMaNGOS (no vmaps) and older repacks (ad instead of mapextractor) work
/// without any configuration.
///
/// Correct execution order:
///   1. mapextractor / ad        cwd=clientPath  → maps/ dbc/ in clientPath
///   2. vmap4extractor           cwd=clientPath  → Buildings/ in clientPath
///   3. vmap4assembler           cwd=clientPath  → vmaps/ in clientPath
///   [relocate maps/ dbc/ vmaps/ from clientPath → outputPath]
///   4. mmaps_generator / MoveMapGen  cwd=outputPath  → mmaps/ in outputPath
/// </summary>
public sealed class ClientDataExtractor
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly ILogger _log;

    // Directories extracted to clientPath that must be moved to outputPath
    // before mmaps_generator (which needs them as its cwd).
    // All directories written to clientPath by steps 1-3 that must land in outputPath.
    // cameras/ and gt/ are extracted by map_extractor alongside maps/ and dbc/.
    // Buildings/ is NOT here — it's an intermediate consumed by vmap4_assembler and then deleted.
    private static readonly string[] DirsToRelocate = ["maps", "dbc", "cameras", "gt", "vmaps"];

    private static readonly ExtractionStep[] Steps =
    [
        // UseOutputDir=false → run from clientPath (reads MPQs / client data there)
        // UseOutputDir=true  → run from outputPath (reads maps/vmaps already moved there)
        new("Map extraction",   5,  30, ["mapextractor", "map_extractor", "ad"],            [],                    UseOutputDir: false, ValidateMapOutput),
        new("VMap extraction",  35, 50, ["vmap4extractor", "vmap4_extractor"],            [],                    UseOutputDir: false, ValidateVmapRawOutput),
        new("VMap assembly",    50, 65, ["vmap4assembler", "vmap4_assembler"],            ["Buildings", "vmaps"], UseOutputDir: false, ValidateVmapOutput),
        // mmaps_generator needs maps/ to already exist in outputPath — skip if map extraction was skipped
        new("MMap generation",  70, 98, ["mmaps_generator", "MoveMapGen"], [],                    UseOutputDir: true,  ValidateMmapOutput, RequiredDirs: ["maps"]),
    ];

    public ClientDataExtractor(TrionLogger trionLogger)
    {
        _log = trionLogger.CreateLogger(nameof(ClientDataExtractor));
    }

    // Sentinel written to outputPath after at least one extraction step completes.
    // GameClientService.IsDataExtracted checks for this file instead of probing map counts.
    public const string SentinelFileName = ".extraction_complete";

    public async Task ExtractAsync(
        string                              clientPath,
        string                              toolsPath,
        string                              outputPath,
        ChannelWriter<ExtractProgressEvent> progress,
        CancellationToken                   ct = default)
    {
        Directory.CreateDirectory(outputPath);
        bool relocated  = false;
        bool anyStepRan = false;

        foreach (var step in Steps)
        {
            // Before any step that needs outputPath as its working directory,
            // relocate the intermediate output that earlier steps wrote to clientPath.
            if (step.UseOutputDir && !relocated)
            {
                await RelocateIntermediateAsync(clientPath, outputPath, progress, ct);
                relocated = true;
            }

            var toolPath = ResolveToolPath(toolsPath, step.ToolNames);
            if (toolPath is null)
            {
                _log.LogInformation("Skipping '{Step}' — no matching tool found in {Dir}", step.Label, toolsPath);
                await progress.WriteAsync(new ExtractProgressEvent(
                    step.Label, step.EndPct, "Skipped (tool not found in repack)."), ct);
                continue;
            }

            // Skip steps whose required input directories are missing (e.g. mmaps needs maps/).
            if (step.RequiredDirs is { Length: > 0 })
            {
                var checkDir = step.UseOutputDir ? outputPath : clientPath;
                var missing  = Array.Find(step.RequiredDirs,
                    d => !Directory.Exists(Path.Combine(checkDir, d)));
                if (missing is not null)
                {
                    _log.LogInformation(
                        "Skipping '{Step}' — required input '{Dir}/' not found in {Path}",
                        step.Label, missing, checkDir);
                    await progress.WriteAsync(new ExtractProgressEvent(
                        step.Label, step.EndPct,
                        $"Skipped — '{missing}/' not present (earlier extraction step was skipped)."), ct);
                    continue;
                }
            }

            var workDir       = step.UseOutputDir ? outputPath : clientPath;
            var validationDir = step.UseOutputDir ? outputPath : clientPath;
            await RunWithRetryAsync(toolPath, step, workDir, validationDir, progress, ct);
            anyStepRan = true;
        }

        _log.LogInformation("ClientDataExtractor: all phases complete.");
        await progress.WriteAsync(new ExtractProgressEvent("Complete", 100, "All data extracted."), ct);

        // Write sentinel so IsDataExtracted knows extraction finished cleanly.
        // Uses CancellationToken.None so it isn't skipped if ct fires at the last moment.
        if (anyStepRan)
            await File.WriteAllTextAsync(
                Path.Combine(outputPath, SentinelFileName),
                DateTime.UtcNow.ToString("O"),
                CancellationToken.None);

        progress.TryComplete();
    }

    // -- Tool resolution --------------------------------------------------------

    // Search the repack root and every immediate subdirectory so tools placed in
    // bin/, tools/, or similar subdirectories are found without hard-coding paths.
    private string? ResolveToolPath(string toolsPath, string[] toolNames)
    {
        if (!Directory.Exists(toolsPath)) return null;

        IEnumerable<string> dirs = [toolsPath, ..Directory.GetDirectories(toolsPath)];
        foreach (var dir in dirs)
        {
            foreach (var name in toolNames)
            {
                var path = Path.Combine(dir, name);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !Path.HasExtension(path))
                    path += ".exe";
                if (File.Exists(path))
                    return path;
            }
        }
        return null;
    }

    // -- Intermediate relocation (clientPath → outputPath) ---------------------

    private async Task RelocateIntermediateAsync(
        string clientPath, string outputPath,
        ChannelWriter<ExtractProgressEvent> progress, CancellationToken ct)
    {
        _log.LogInformation("Relocating extracted data from '{Client}' → '{Output}'",
            clientPath, outputPath);

        await progress.WriteAsync(new ExtractProgressEvent(
            "Relocating data", 66, "Moving extracted data to shared output directory..."), ct);

        foreach (var dir in DirsToRelocate)
        {
            var src = Path.Combine(clientPath, dir);
            var dst = Path.Combine(outputPath, dir);
            if (!Directory.Exists(src))
            {
                _log.LogDebug("Skipping relocation of '{Dir}' — not present in client dir", dir);
                continue;
            }

            _log.LogInformation("Relocating '{Dir}'...", dir);

            if (Directory.Exists(dst))
                Directory.Delete(dst, recursive: true);

            try
            {
                Directory.Move(src, dst);
            }
            catch (IOException)
            {
                // Cross-volume: fall back to recursive copy + delete
                _log.LogInformation("Cross-volume move for '{Dir}' — falling back to copy+delete", dir);
                CopyDirectory(src, dst);
                Directory.Delete(src, recursive: true);
            }

            _log.LogInformation("Relocated '{Dir}' → '{Dst}'", dir, dst);
        }

        // Clean up the Buildings/ staging directory — no longer needed after assembly
        var buildings = Path.Combine(clientPath, "Buildings");
        if (Directory.Exists(buildings))
        {
            try { Directory.Delete(buildings, recursive: true); }
            catch (Exception ex) { _log.LogDebug(ex, "Could not remove Buildings/ staging dir"); }
        }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel  = Path.GetRelativePath(src, file);
            var dest = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    // -- Core runner ------------------------------------------------------------

    private async Task RunWithRetryAsync(
        string toolPath,
        ExtractionStep step,
        string workDir,
        string validationDir,
        ChannelWriter<ExtractProgressEvent> progress,
        CancellationToken ct)
    {
        _log.LogInformation("Step '{Step}': {Exe}  cwd={Dir}", step.Label, toolPath, workDir);

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            await progress.WriteAsync(new ExtractProgressEvent(
                step.Label, step.StartPct,
                attempt > 1 ? $"Attempt {attempt}/{MaxRetries}..." : null), ct);

            Process? proc;
            try
            {
                var psi = new ProcessStartInfo(toolPath)
                {
                    WorkingDirectory = workDir,
                    UseShellExecute  = false,
                    CreateNoWindow   = true,
                };
                foreach (var arg in step.Arguments)
                    psi.ArgumentList.Add(arg);

                proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                _log.LogWarning("{Step} attempt {N} failed to start: {Err}", step.Label, attempt, ex.Message);
                if (attempt == MaxRetries)
                {
                    await progress.WriteAsync(new ExtractProgressEvent(
                        step.Label, -1, $"Failed after {MaxRetries} attempts: {ex.Message}",
                        IsError: true), ct);
                    progress.TryComplete(new InvalidOperationException(ex.Message));
                }
                continue;
            }

            if (proc is null)
            {
                _log.LogWarning("{Step} attempt {N}: Process.Start returned null", step.Label, attempt);
                if (attempt == MaxRetries) return;
                continue;
            }

            _log.LogInformation("{Step} launched (PID {Pid})", step.Label, proc.Id);

            int pollPct = step.StartPct;
            var started = DateTime.UtcNow;
            try
            {
                while (!proc.HasExited)
                {
                    pollPct = Math.Min(pollPct + 2, step.EndPct - 2);
                    var elapsed    = DateTime.UtcNow - started;
                    var elapsedStr = elapsed.TotalMinutes >= 1
                        ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:D2}s"
                        : $"{(int)elapsed.TotalSeconds}s";
                    await progress.WriteAsync(
                        new ExtractProgressEvent(step.Label, pollPct, $"Running... ({elapsedStr})"), ct);
                    await Task.Delay(PollInterval, ct);
                }
            }
            finally
            {
                // Kill if cancellation interrupted the wait.
                if (!proc.HasExited)
                    try { proc.Kill(entireProcessTree: true); } catch { }
                proc.Dispose();
            }

            ct.ThrowIfCancellationRequested();

            if (step.Validator(validationDir))
            {
                _log.LogInformation("{Step} complete.", step.Label);
                await progress.WriteAsync(
                    new ExtractProgressEvent(step.Label, step.EndPct, $"{step.Label} complete."), ct);
                return;
            }

            _log.LogWarning("{Step} output validation failed on attempt {N}.", step.Label, attempt);
            if (attempt == MaxRetries)
            {
                await progress.WriteAsync(new ExtractProgressEvent(
                    step.Label, -1, $"Output validation failed after {MaxRetries} attempts.",
                    IsError: true), ct);
                progress.TryComplete(new InvalidOperationException($"{step.Label}: output validation failed."));
                return;
            }
        }
    }

    // -- Validators -------------------------------------------------------------

    private static bool ValidateMapOutput(string path) =>
        Directory.Exists(Path.Combine(path, "maps"))
        && Directory.GetFiles(Path.Combine(path, "maps"), "*.map").Length > 10;

    // vmap4extractor writes raw data to Buildings/ — just check the directory exists
    private static bool ValidateVmapRawOutput(string path) =>
        Directory.Exists(Path.Combine(path, "Buildings"));

    private static bool ValidateVmapOutput(string path) =>
        Directory.Exists(Path.Combine(path, "vmaps"))
        && Directory.GetFiles(Path.Combine(path, "vmaps"), "*.vmtree").Length > 0;

    private static bool ValidateMmapOutput(string path) =>
        Directory.Exists(Path.Combine(path, "mmaps"))
        && Directory.GetFiles(Path.Combine(path, "mmaps"), "*.mmtile").Length > 100;

    // -- Step descriptor --------------------------------------------------------

    private sealed record ExtractionStep(
        string             Label,
        int                StartPct,
        int                EndPct,
        string[]           ToolNames,
        string[]           Arguments,
        bool               UseOutputDir,
        Func<string, bool> Validator,
        string[]           RequiredDirs = null!);
}
