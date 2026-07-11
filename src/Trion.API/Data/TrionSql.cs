namespace Trion.API.Data;

internal static class TrionSql
{
    // -- Users -----------------------------------------------------------------

    internal const string GetUserByEmail = """
        SELECT ID, Username, Email, PasswordHash, Role, IsActive, IsBanned,
               ApiKey, ApiTier, LastLogin, CreatedAt
        FROM   Users
        WHERE  Email = @Email
        LIMIT  1
        """;

    internal const string GetUserById = """
        SELECT ID, Username, Email, PasswordHash, Role, IsActive, IsBanned,
               ApiKey, ApiTier, LastLogin, CreatedAt
        FROM   Users
        WHERE  ID = @Id
        LIMIT  1
        """;

    internal const string UpdateLastLogin = """
        UPDATE Users SET LastLogin = UTC_TIMESTAMP() WHERE ID = @Id
        """;

    // -- Supporter keys --------------------------------------------------------

    internal const string GetKeyExists = """
        SELECT EXISTS(SELECT 1 FROM SupporterKey WHERE ApiKey = @ApiKey)
        """;

    internal const string InsertSupporterKey = """
        INSERT INTO SupporterKey (ApiKey, UID) VALUES (@ApiKey, @UID)
        """;

    // -- Supporters ------------------------------------------------------------

    internal const string GetSupporters = """
        SELECT u.Username, u.ApiTier,
               DATE_FORMAT(u.CreatedAt, '%Y-%m-%d') AS JoinedAt
        FROM   Users u
        INNER  JOIN SupporterKey sk ON sk.UID = u.ID
        WHERE  u.IsActive = 1 AND u.IsBanned = 0
        ORDER  BY FIELD(u.ApiTier, 'legend','champion','guardian','support'),
                  u.Username ASC
        """;

    // -- News ------------------------------------------------------------------

    internal const string GetNews = """
        SELECT ID, Title, Summary, Category, PublishedAt, Url
        FROM   News
        WHERE  IsPublished = 1
        ORDER  BY PublishedAt DESC
        LIMIT  @Limit
        """;

    // -- Downloads -------------------------------------------------------------

    internal const string GetDownloads = """
        SELECT Name, Version, Description, Url, UpdatedAt
        FROM   Downloads
        WHERE  IsAvailable = 1
        ORDER  BY SortOrder ASC
        """;

    // -- Repack builds ---------------------------------------------------------
    // Table schema (run once):
    //   CREATE TABLE IF NOT EXISTS RepackBuilds (
    //       Id               INT AUTO_INCREMENT PRIMARY KEY,
    //       UserId           INT NOT NULL,
    //       ServiceJobId     VARCHAR(64)   NULL,
    //       ServiceId        INT           NULL,
    //       RealmName        VARCHAR(64)   NOT NULL,
    //       EmulatorId       VARCHAR(64)   NOT NULL DEFAULT 'azerothcore',
    //       BuildFingerprint VARCHAR(32)   NULL,
    //       Status           VARCHAR(32)   NOT NULL DEFAULT 'pending',
    //       DownloadUrl      VARCHAR(1024) NULL,
    //       ErrorMessage     TEXT NULL,
    //       CreatedAt        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    //       UpdatedAt        DATETIME NULL ON UPDATE CURRENT_TIMESTAMP,
    //       INDEX idx_user        (UserId),
    //       INDEX idx_svcjob      (ServiceJobId),
    //       INDEX idx_fingerprint (BuildFingerprint),
    //       CONSTRAINT fk_rb_svc FOREIGN KEY (ServiceId) REFERENCES repack_services(Id) ON DELETE SET NULL
    //   ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
    //
    // Companion tables (repack_services, repack_service_invites) are defined
    // in the repack_services schema block further below.

    internal const string InsertRepackBuild = """
        INSERT INTO RepackBuilds (UserId, RealmName, EmulatorId, Status, CreatedAt)
        VALUES (@UserId, @RealmName, @EmulatorId, 'pending', UTC_TIMESTAMP())
        """;

    internal const string GetRepackBuildById = """
        SELECT Id, UserId, ServiceJobId, RealmName, EmulatorId,
               Status, DownloadUrl, ErrorMessage, CreatedAt, UpdatedAt
        FROM   RepackBuilds
        WHERE  Id = @Id
        LIMIT  1
        """;

    internal const string GetRepackBuildsForUser = """
        SELECT Id, UserId, ServiceJobId, RealmName, EmulatorId,
               Status, DownloadUrl, ErrorMessage, CreatedAt, UpdatedAt
        FROM   RepackBuilds
        WHERE  UserId = @UserId
        ORDER  BY CreatedAt DESC
        LIMIT  20
        """;

    internal const string UpdateRepackBuildQueued = """
        UPDATE RepackBuilds
        SET    ServiceJobId = @ServiceJobId,
               Status       = 'queued',
               UpdatedAt    = UTC_TIMESTAMP()
        WHERE  Id = @Id
        """;

    internal const string UpdateRepackBuildFromCallback = """
        UPDATE RepackBuilds
        SET    Status       = @Status,
               DownloadUrl  = @DownloadUrl,
               ErrorMessage = @ErrorMessage,
               UpdatedAt    = UTC_TIMESTAMP()
        WHERE  Id = @Id
        """;

    internal const string GetRepackBuildByServiceJobId = """
        SELECT Id, UserId, ServiceJobId, RealmName, EmulatorId,
               Status, DownloadUrl, ErrorMessage, CreatedAt, UpdatedAt
        FROM   RepackBuilds
        WHERE  ServiceJobId = @ServiceJobId
        LIMIT  1
        """;

    internal const string UpdateRepackBuildQueuedWithService = """
        UPDATE RepackBuilds
        SET    ServiceJobId      = @ServiceJobId,
               ServiceId        = @ServiceId,
               BuildFingerprint = @BuildFingerprint,
               Status           = 'queued',
               UpdatedAt        = UTC_TIMESTAMP()
        WHERE  Id = @Id
        """;

    internal const string GetRepackBuildCached = """
        SELECT Id, UserId, ServiceJobId, RealmName, EmulatorId,
               Status, DownloadUrl, ErrorMessage, CreatedAt, UpdatedAt
        FROM   RepackBuilds
        WHERE  BuildFingerprint = @Fingerprint
          AND  Status           = 'done'
          AND  DownloadUrl      IS NOT NULL
        ORDER  BY CreatedAt DESC
        LIMIT  1
        """;

    // -- Repack service registry -----------------------------------------------
    // Table schema (run once):
    //   CREATE TABLE IF NOT EXISTS repack_services (
    //       Id                INT AUTO_INCREMENT PRIMARY KEY,
    //       Name              VARCHAR(100) NOT NULL,
    //       Url               VARCHAR(512) NOT NULL,
    //       HmacSecret        VARCHAR(128) NOT NULL,
    //       IsActive          TINYINT(1)   NOT NULL DEFAULT 1,
    //       MaxConcurrentJobs INT          NOT NULL DEFAULT 1,
    //       CurrentJobs       INT          NOT NULL DEFAULT 0,
    //       LastSeenAt        DATETIME     NULL,
    //       RegisteredAt      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP
    //   ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
    //
    //   CREATE TABLE IF NOT EXISTS repack_service_invites (
    //       Id               INT AUTO_INCREMENT PRIMARY KEY,
    //       InviteKey        VARCHAR(128) NOT NULL UNIQUE,
    //       Note             VARCHAR(255) NULL,
    //       CreatedBy        INT          NOT NULL,
    //       CreatedAt        DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    //       ExpiresAt        DATETIME     NULL,
    //       ClaimedAt        DATETIME     NULL,
    //       ClaimedServiceId INT          NULL
    //   ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

    internal const string GetAllRepackServices = """
        SELECT Id, Name, Url, HmacSecret, IsActive, MaxConcurrentJobs, CurrentJobs, LastSeenAt, RegisteredAt
        FROM   repack_services
        ORDER  BY RegisteredAt ASC
        """;

    internal const string SelectLeastLoadedRepackService = """
        SELECT Id, Name, Url, HmacSecret, IsActive, MaxConcurrentJobs, CurrentJobs, LastSeenAt, RegisteredAt
        FROM   repack_services
        WHERE  IsActive = 1 AND CurrentJobs < MaxConcurrentJobs
        ORDER  BY CurrentJobs ASC, RegisteredAt ASC
        LIMIT  1
        """;

    internal const string IncrementRepackServiceJobs = """
        UPDATE repack_services SET CurrentJobs = CurrentJobs + 1 WHERE Id = @Id
        """;

    internal const string DecrementRepackServiceJobs = """
        UPDATE repack_services
        SET CurrentJobs = GREATEST(0, CurrentJobs - 1), LastSeenAt = UTC_TIMESTAMP()
        WHERE Id = @Id
        """;

    internal const string InsertRepackService = """
        INSERT INTO repack_services (Name, Url, HmacSecret, IsActive, MaxConcurrentJobs, CurrentJobs, RegisteredAt)
        VALUES (@Name, @Url, @HmacSecret, 1, @MaxConcurrentJobs, 0, UTC_TIMESTAMP())
        """;

    internal const string ToggleRepackService = """
        UPDATE repack_services SET IsActive = NOT IsActive WHERE Id = @Id
        """;

    // -- Repack service invites ------------------------------------------------

    internal const string InsertRepackServiceInvite = """
        INSERT INTO repack_service_invites (InviteKey, Note, CreatedBy, CreatedAt)
        VALUES (@InviteKey, @Note, @CreatedBy, UTC_TIMESTAMP())
        """;

    internal const string GetRepackServiceInviteByKey = """
        SELECT Id, InviteKey, Note, CreatedBy, CreatedAt, ExpiresAt, ClaimedAt, ClaimedServiceId
        FROM   repack_service_invites
        WHERE  InviteKey = @InviteKey
        LIMIT  1
        """;

    internal const string ClaimRepackServiceInvite = """
        UPDATE repack_service_invites
        SET ClaimedAt = UTC_TIMESTAMP(), ClaimedServiceId = @ServiceId
        WHERE Id = @Id AND ClaimedAt IS NULL
        """;

    internal const string GetAllRepackServiceInvites = """
        SELECT i.Id, i.InviteKey, i.Note, i.CreatedBy, i.CreatedAt, i.ExpiresAt, i.ClaimedAt, i.ClaimedServiceId
        FROM   repack_service_invites i
        ORDER  BY i.CreatedAt DESC
        """;

    internal const string RevokeRepackServiceInvite = """
        DELETE FROM repack_service_invites WHERE Id = @Id AND ClaimedAt IS NULL
        """;
}
