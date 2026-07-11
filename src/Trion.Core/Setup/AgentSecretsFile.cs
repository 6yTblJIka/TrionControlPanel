using System.Text.Json;

namespace Trion.Core.Setup;

/// <summary>
/// Manages the agent-secrets.json file that carries the HMAC shared key between
/// Trion.Web and Trion.Agent.  The file is written once by the setup wizard and
/// read by both processes at startup via AddJsonFile.
///
/// Location:
///   Windows : %ProgramData%\Trion\agent-secrets.json
///   Linux   : /etc/trion/agent-secrets.json
/// </summary>
public static class AgentSecretsFile
{
    public static string FilePath { get; } = OperatingSystem.IsWindows()
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Trion", "agent-secrets.json")
        : "/etc/trion/agent-secrets.json";

    /// <summary>
    /// Generates a cryptographically random 256-bit key and writes it to the
    /// secrets file.  Returns the generated key string.
    /// </summary>
    public static string GenerateAndWrite()
    {
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        Write(key);
        return key;
    }

    /// <summary>Writes an existing key to the secrets file (overwrites if present).</summary>
    public static void Write(string hmacKey)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(
            new { Agent = new { HmacSharedKey = hmacKey } },
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(FilePath, json);
    }

    /// <summary>True when the secrets file exists on disk.</summary>
    public static bool Exists => File.Exists(FilePath);
}
