namespace Trion.Agent;

public sealed class AgentOptions
{
    /// <summary>Named pipe name on Windows.</summary>
    public string PipeName { get; set; } = "TrionAgentPipe";

    /// <summary>Unix socket path on Linux.</summary>
    public string SocketPath { get; set; } = "/run/trion/agent.sock";

    /// <summary>HMAC shared key -- set via environment variable Agent__HmacSharedKey.</summary>
    public string HmacSharedKey { get; set; } = string.Empty;

    /// <summary>
    /// Absolute directory paths that emulator executables are permitted to live under.
    /// When empty the path check is skipped (permissive / development mode).
    /// Production deployments should set this to the configured emulator install root(s)
    /// so that only executables inside those directories can be launched via the Agent.
    /// Example: [ "C:\\TrionServers", "D:\\WoWEmulators" ]
    /// </summary>
    public string[] AllowedInstallRoots { get; set; } = [];
}
