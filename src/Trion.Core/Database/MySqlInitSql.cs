namespace Trion.Core.Database;

/// <summary>
/// SQL executed once immediately after MySQL is initialized for the first time.
/// Creates the root password and the 'phoenix' application user. Per-expansion
/// databases are created on demand by <c>EmulatorInstallerService.CreateDatabasesAsync</c>
/// when a repack is actually installed.
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
        -- -- Root account ------------------------------------------------------
        ALTER USER 'root'@'localhost' IDENTIFIED WITH 'caching_sha2_password' BY 'FlyingPhoenix';

        -- -- Application user ---------------------------------------------------
        DROP USER IF EXISTS 'phoenix'@'localhost';
        CREATE USER 'phoenix'@'localhost'
            IDENTIFIED WITH 'caching_sha2_password' BY 'phoenix'
            WITH MAX_QUERIES_PER_HOUR 0
                 MAX_CONNECTIONS_PER_HOUR 0
                 MAX_UPDATES_PER_HOUR 0;
        GRANT ALL PRIVILEGES ON *.* TO 'phoenix'@'localhost' WITH GRANT OPTION;

        -- -- Flush --------------------------------------------------------------
        FLUSH PRIVILEGES;
        """;

    /// <summary>
    /// Password assigned to <c>root@localhost</c> when Trion configures a system MySQL.
    /// Distinct from <see cref="RootPassword"/> (portable MySQL) so users can tell the two apart.
    /// </summary>
    public const string SystemRootPassword = "FlyingPhoenix01!";

    /// <summary>
    /// Minimal setup script run against an existing system MySQL installation.
    /// Sets the root password to a known value, creates the 'phoenix' application
    /// user, and grants it full privileges.  No databases are pre-created — they
    /// are created on demand when a repack is installed.
    /// </summary>
    public const string SystemMySqlSetup = """
        ALTER USER 'root'@'localhost' IDENTIFIED WITH 'caching_sha2_password' BY 'FlyingPhoenix01!';

        DROP USER IF EXISTS 'phoenix'@'localhost';
        CREATE USER 'phoenix'@'localhost'
            IDENTIFIED WITH 'caching_sha2_password' BY 'phoenix'
            WITH MAX_QUERIES_PER_HOUR 0
                 MAX_CONNECTIONS_PER_HOUR 0
                 MAX_UPDATES_PER_HOUR 0;
        GRANT ALL PRIVILEGES ON *.* TO 'phoenix'@'localhost' WITH GRANT OPTION;

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
