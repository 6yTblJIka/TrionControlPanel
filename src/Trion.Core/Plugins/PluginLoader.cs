using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Trion.Core.Plugins;

/// <summary>
/// Scans the <c>plugins/</c> folder, loads each DLL, and instantiates every
/// type that implements <see cref="ITrionPlugin"/>.
///
/// When <paramref name="strictMode"/> is <c>true</c> a plugin DLL is only loaded
/// if its SHA-256 hash has been approved via <see cref="IPluginApprovalStore"/>.
/// Unapproved DLLs are skipped -- their manifests are still recorded so the
/// Plugins management page can show an Approve button.
/// </summary>
public sealed class PluginLoader
{
    private readonly string                  _folder;
    private readonly ILogger?                _logger;
    private readonly IPluginApprovalStore?   _approvalStore;
    private readonly bool                    _strictMode;
    private readonly List<ITrionPlugin>      _plugins    = [];
    private readonly List<Assembly>          _assemblies = [];
    private readonly List<PluginManifest>    _manifests  = [];

    public IReadOnlyList<ITrionPlugin>   Plugins    => _plugins;
    public IReadOnlyList<Assembly>       Assemblies => _assemblies;
    public IReadOnlyList<PluginManifest> Manifests  => _manifests;

    public PluginLoader(
        string                 folder,
        IPluginApprovalStore?  approvalStore = null,
        bool                   strictMode    = false,
        ILogger?               logger        = null)
    {
        _folder        = folder;
        _approvalStore = approvalStore;
        _strictMode    = strictMode;
        _logger        = logger;
    }

    /// <summary>
    /// Scans the plugins folder and loads all valid plugin assemblies.
    /// Safe to call multiple times -- existing entries are not duplicated.
    /// </summary>
    public void LoadAll()
    {
        if (!Directory.Exists(_folder))
        {
            Directory.CreateDirectory(_folder);
            _logger?.LogInformation("Created plugins folder: {Folder}", _folder);
            return;
        }

        foreach (var dll in Directory.GetFiles(_folder, "*.dll", SearchOption.TopDirectoryOnly))
            LoadAssembly(dll);

        _logger?.LogInformation(
            "Plugin loading complete -- {Count} plugin(s) loaded.", _plugins.Count);
    }

    private void LoadAssembly(string dllPath)
    {
        var hash       = ComputeHash(dllPath);
        var isApproved = _approvalStore?.IsApproved(hash) ?? true; // no store = no restriction

        // RSA-signed plugins (installed from the store) bypass the hash allowlist.
        var rsaVerified = PluginVerifier.VerifyFile(dllPath);

        if (_strictMode && !isApproved && !rsaVerified)
        {
            _logger?.LogWarning(
                "Plugin blocked (not approved, no valid .sig): {Path} [{Hash}]", dllPath, hash);

            _manifests.Add(new PluginManifest
            {
                Name         = Path.GetFileNameWithoutExtension(dllPath),
                Version      = "--",
                Description  = "Plugin is not approved. Use the Plugins page to approve it, or install it from the Plugin Store.",
                Author       = "--",
                AssemblyPath = dllPath,
                Assembly     = typeof(PluginLoader).Assembly,
                Hash         = hash,
                IsApproved   = false,
                LoadError    = $"Blocked by strict mode -- SHA-256: {hash}",
            });
            return;
        }

        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(dllPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Could not load assembly: {Path}", dllPath);
            _manifests.Add(new PluginManifest
            {
                Name         = Path.GetFileNameWithoutExtension(dllPath),
                Version      = "--",
                Description  = "Failed to load assembly.",
                Author       = "--",
                AssemblyPath = dllPath,
                Assembly     = typeof(PluginLoader).Assembly,
                Hash         = hash,
                IsApproved   = isApproved,
                LoadError    = ex.Message,
            });
            return;
        }

        IEnumerable<Type> pluginTypes;
        try
        {
            pluginTypes = assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false }
                         && typeof(ITrionPlugin).IsAssignableFrom(t));
        }
        catch (ReflectionTypeLoadException ex)
        {
            _logger?.LogError(ex, "Type-load error in assembly: {Path}", dllPath);
            return;
        }

        foreach (var type in pluginTypes)
        {
            ITrionPlugin plugin;
            try
            {
                plugin = (ITrionPlugin)Activator.CreateInstance(type)!;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Could not instantiate plugin type {Type}", type.FullName);
                continue;
            }

            _plugins.Add(plugin);

            if (!_assemblies.Contains(assembly))
                _assemblies.Add(assembly);

            _manifests.Add(new PluginManifest
            {
                Name         = plugin.Name,
                Version      = plugin.Version,
                Description  = plugin.Description,
                Author       = plugin.Author,
                AssemblyPath = dllPath,
                Assembly     = assembly,
                Hash         = hash,
                IsApproved   = isApproved,
            });

            _logger?.LogInformation(
                "Plugin loaded: {Name} v{Version} by {Author} [{Hash}]",
                plugin.Name, plugin.Version, plugin.Author, hash[..12] + "...");
        }
    }

    private static string ComputeHash(string dllPath)
    {
        var bytes = File.ReadAllBytes(dllPath);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
