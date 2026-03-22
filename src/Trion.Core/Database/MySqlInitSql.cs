namespace Trion.Core.Database;

/// <summary>
/// SQL executed once immediately after MySQL is initialized for the first time.
/// Creates the root password, the 'phoenix' application user, and all
/// expansion-specific database schemas.
/// </summary>
public static class MySqlInitSql
{
    /// <summary>
    /// Password assigned to <c>root@localhost</c> during first-run setup.
    /// Used by DatabaseService.PingAsync to verify MySQL connectivity
    /// with known-good credentials regardless of the application user configuration.
    /// </summary>
    public const string RootPassword = "FlyingPhoenix";

    /// <summary>
    /// Full first-run initialization script for MySQL 8.
    /// Run via <c>mysql.exe --user=root --host=127.0.0.1 --port=3306 --protocol=TCP</c>
    /// with this text piped to stdin (Windows).
    /// On Linux use <see cref="FirstRunSetupLinux"/> instead (MariaDB-compatible syntax).
    /// </summary>
    public const string FirstRunSetup = """
        -- ── Root account ──────────────────────────────────────────────────────
        ALTER USER 'root'@'localhost' IDENTIFIED WITH 'caching_sha2_password' BY 'FlyingPhoenix';

        -- ── Application user ───────────────────────────────────────────────────
        DROP USER IF EXISTS 'phoenix'@'localhost';
        CREATE USER 'phoenix'@'localhost'
            IDENTIFIED WITH 'caching_sha2_password' BY 'phoenix'
            WITH MAX_QUERIES_PER_HOUR 0
                 MAX_CONNECTIONS_PER_HOUR 0
                 MAX_UPDATES_PER_HOUR 0;
        GRANT ALL PRIVILEGES ON *.* TO 'phoenix'@'localhost' WITH GRANT OPTION;

        -- ── Classic databases ──────────────────────────────────────────────────
        CREATE DATABASE IF NOT EXISTS `classic_world`      DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;
        CREATE DATABASE IF NOT EXISTS `classic_characters` DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;
        CREATE DATABASE IF NOT EXISTS `classic_auth`       DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;
        CREATE DATABASE IF NOT EXISTS `classic_logs`       DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_general_ci;

        GRANT ALL PRIVILEGES ON `classic_world`      .* TO 'phoenix'@'localhost' WITH GRANT OPTION;
        GRANT ALL PRIVILEGES ON `classic_characters` .* TO 'phoenix'@'localhost' WITH GRANT OPTION;
        GRANT ALL PRIVILEGES ON `classic_auth`       .* TO 'phoenix'@'localhost' WITH GRANT OPTION;
        GRANT ALL PRIVILEGES ON `classic_logs`       .* TO 'phoenix'@'localhost' WITH GRANT OPTION;

        -- ── TBC databases ──────────────────────────────────────────────────────
        CREATE DATABASE IF NOT EXISTS `tbc_world`      DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;
        CREATE DATABASE IF NOT EXISTS `tbc_characters` DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;
        CREATE DATABASE IF NOT EXISTS `tbc_auth`       DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;
        CREATE DATABASE IF NOT EXISTS `tbc_logs`       DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_general_ci;

        GRANT ALL PRIVILEGES ON `tbc_world`      .* TO 'phoenix'@'localhost' WITH GRANT OPTION;
        GRANT ALL PRIVILEGES ON `tbc_characters` .* TO 'phoenix'@'localhost' WITH GRANT OPTION;
        GRANT ALL PRIVILEGES ON `tbc_auth`       .* TO 'phoenix'@'localhost' WITH GRANT OPTION;
        GRANT ALL PRIVILEGES ON `tbc_logs`       .* TO 'phoenix'@'localhost' WITH GRANT OPTION;

        -- ── WotLK databases ────────────────────────────────────────────────────
        CREATE DATABASE IF NOT EXISTS `wotlk_world`      DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;
        CREATE DATABASE IF NOT EXISTS `wotlk_characters` DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;
        CREATE DATABASE IF NOT EXISTS `wotlk_auth`       DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;
        CREATE DATABASE IF NOT EXISTS `wotlk_playerbots` DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_general_ci;

        GRANT ALL PRIVILEGES ON `wotlk_world`      .* TO 'phoenix'@'localhost' WITH GRANT OPTION;
        GRANT ALL PRIVILEGES ON `wotlk_characters` .* TO 'phoenix'@'localhost' WITH GRANT OPTION;
        GRANT ALL PRIVILEGES ON `wotlk_auth`       .* TO 'phoenix'@'localhost' WITH GRANT OPTION;
        GRANT ALL PRIVILEGES ON `wotlk_playerbots` .* TO 'phoenix'@'localhost' WITH GRANT OPTION;

        -- ── Cataclysm databases ────────────────────────────────────────────────
        CREATE DATABASE IF NOT EXISTS `cata_world`      DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;
        CREATE DATABASE IF NOT EXISTS `cata_characters` DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;
        CREATE DATABASE IF NOT EXISTS `cata_auth`       DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;

        GRANT ALL PRIVILEGES ON `cata_world`      .* TO 'phoenix'@'localhost' WITH GRANT OPTION;
        GRANT ALL PRIVILEGES ON `cata_characters` .* TO 'phoenix'@'localhost' WITH GRANT OPTION;
        GRANT ALL PRIVILEGES ON `cata_auth`       .* TO 'phoenix'@'localhost' WITH GRANT OPTION;

        -- ── MoP databases ──────────────────────────────────────────────────────
        CREATE DATABASE IF NOT EXISTS `mop_world`      DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;
        CREATE DATABASE IF NOT EXISTS `mop_characters` DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;
        CREATE DATABASE IF NOT EXISTS `mop_auth`       DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;

        GRANT ALL PRIVILEGES ON `mop_world`      .* TO 'phoenix'@'localhost' WITH GRANT OPTION;
        GRANT ALL PRIVILEGES ON `mop_characters` .* TO 'phoenix'@'localhost' WITH GRANT OPTION;
        GRANT ALL PRIVILEGES ON `mop_auth`       .* TO 'phoenix'@'localhost' WITH GRANT OPTION;

        -- ── Custom core databases ─────────────────────────────────────────────
        CREATE DATABASE IF NOT EXISTS `custom_world`      DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;
        CREATE DATABASE IF NOT EXISTS `custom_characters` DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;
        CREATE DATABASE IF NOT EXISTS `custom_auth`       DEFAULT CHARACTER SET UTF8MB4 COLLATE utf8mb4_unicode_ci;

        GRANT ALL PRIVILEGES ON `custom_world`      .* TO 'phoenix'@'localhost' WITH GRANT OPTION;
        GRANT ALL PRIVILEGES ON `custom_characters` .* TO 'phoenix'@'localhost' WITH GRANT OPTION;
        GRANT ALL PRIVILEGES ON `custom_auth`       .* TO 'phoenix'@'localhost' WITH GRANT OPTION;

        -- ── Flush ──────────────────────────────────────────────────────────────
        FLUSH PRIVILEGES;
        """;

    /// <summary>
    /// First-run initialization script compatible with both MySQL 8 and MariaDB.
    /// Uses <c>IDENTIFIED BY</c> without specifying an auth plugin, which works on
    /// both MySQL 8 (defaults to caching_sha2_password) and MariaDB.
    /// Used by <c>LinuxMySqlService</c>.
    /// </summary>
    public static readonly string FirstRunSetupLinux =
        FirstRunSetup.Replace(
            "IDENTIFIED WITH 'caching_sha2_password' BY",
            "IDENTIFIED BY");
}
