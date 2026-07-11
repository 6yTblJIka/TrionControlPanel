namespace Trion.API.Models;

// -- Inbound from the Trion client ---------------------------------------------

public sealed record RepackRequest(
    string        RealmName,
    string        DbHost          = "127.0.0.1",
    int           DbPort          = 3306,
    string        DbUser          = "acore",
    string        DbPass          = "acore",
    string        DbName          = "acore_world",
    string        AuthDbName      = "acore_auth",
    string        CharDbName      = "acore_characters",
    string        BindIp          = "0.0.0.0",
    string        ExternalIp      = "127.0.0.1",
    int           RealmId         = 1,
    int           WorldPort       = 8085,
    int           AuthPort        = 3724,
    string        AcTag           = "master",
    string        EmulatorId      = "azerothcore",
    List<string>? SelectedModules = null,
    /// <summary>
    /// Slug of the RepackDefinition on the RepackService node (e.g. "wotlk", "supporter").
    /// When set, the service resolves the full product config (repo, modules, conf overrides) by slug.
    /// </summary>
    string?       RepackSlug      = null
);

// -- Stored in the TrionAPI DB -------------------------------------------------

public sealed class RepackBuild
{
    public int       Id            { get; init; }
    public int       UserId        { get; init; }
    public string?   ServiceJobId  { get; set;  }
    public string    RealmName     { get; init; } = "";
    public string    EmulatorId    { get; init; } = "azerothcore";
    public string    Status        { get; set;  } = "pending";
    public string?   DownloadUrl   { get; set;  }
    public string?   ErrorMessage  { get; set;  }
    public DateTime  CreatedAt     { get; init; }
    public DateTime? UpdatedAt     { get; set;  }
}

// -- Outbound to the Trion client ----------------------------------------------

public sealed record RepackBuildResponse(
    int      Id,
    string?  ServiceJobId,
    string   Status,
    string   RealmName,
    string   EmulatorId,
    string?  DownloadUrl,
    string?  ErrorMessage,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

// -- Catalog item returned to Trion Desktop ------------------------------------

/// <summary>
/// A single repack product definition as exposed to Trion Desktop.
/// The desktop uses <see cref="BaseType"/>, <see cref="ServerExecutable"/>, etc.
/// to drive installation after a build completes.
/// </summary>
public sealed record RepackCatalogItem(
    string Slug,
    string Name,
    string EmulatorId,
    string Expansion,
    string AccessType,
    string Branch,
    string RepoUrl,
    /// <summary>"azerothcore" | "trinitycore" | "cmangos" | "vmangos"</summary>
    string BaseType,
    string ServerExecutable,
    string AuthExecutable,
    string WorldConf,
    string AuthConf
);

// -- Callback payload from RepackService --------------------------------------

public sealed record RepackCallbackPayload(
    string  JobId,
    string  PhoenixJobId,
    int     UserId,
    string  Status,
    string? DownloadUrl,
    string? Error
);

// -- Repack service registry ---------------------------------------------------

public sealed class RepackServiceInstance
{
    public int      Id                 { get; init; }
    public string   Name               { get; init; } = "";
    public string   Url                { get; init; } = "";
    public string   HmacSecret         { get; init; } = "";
    public bool     IsActive           { get; init; }
    public int      MaxConcurrentJobs  { get; init; }
    public int      CurrentJobs        { get; init; }
    public DateTime? LastSeenAt        { get; init; }
    public DateTime RegisteredAt       { get; init; }
}

public sealed record RegisterServiceRequest(
    string InviteKey,
    string Name,
    string Url,
    int    MaxConcurrentJobs = 1
);

public sealed record RepackServiceInviteRow(
    int      Id,
    string   InviteKey,
    string?  Note,
    int      CreatedBy,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? ClaimedAt,
    int?     ClaimedServiceId
);
