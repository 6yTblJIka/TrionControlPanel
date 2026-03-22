using Dapper;
using MySqlConnector;

namespace Trion.Core.Database;

/// <summary>
/// Forward-only schema migrations for a MySQL settings database.
/// Mirrors <see cref="Settings.SettingsMigrations"/> but targets MySQL syntax.
/// </summary>
public static class MySqlSettingsMigrations
{
    private static readonly (int Version, string Sql)[] Migrations =
    [
        (1, """
            CREATE TABLE IF NOT EXISTS settings (
                `key`       VARCHAR(255) NOT NULL PRIMARY KEY,
                `value`     LONGTEXT     NOT NULL,
                updated_at  VARCHAR(64)  NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """),

        (2, """
            CREATE TABLE IF NOT EXISTS refresh_tokens (
                id          VARCHAR(64)  NOT NULL PRIMARY KEY,
                username    VARCHAR(255) NOT NULL,
                user_hash   VARCHAR(64)  NOT NULL,
                token_hash  VARCHAR(64)  NOT NULL,
                issued_at   VARCHAR(64)  NOT NULL,
                expires_at  VARCHAR(64)  NOT NULL,
                revoked     TINYINT(1)   NOT NULL DEFAULT 0,
                revoked_at  VARCHAR(64)  NULL,
                issuing_ip  VARCHAR(64)  NOT NULL,
                INDEX idx_token_hash (token_hash)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """),
    ];

    public static async Task RunAsync(MySqlConnection db)
    {
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS schema_version (
                version INT NOT NULL
            );
            INSERT INTO schema_version (version)
            SELECT 0 FROM DUAL
            WHERE NOT EXISTS (SELECT 1 FROM schema_version);
            """);

        foreach (var (version, sql) in Migrations)
        {
            var current = await db.QuerySingleAsync<int>("SELECT version FROM schema_version");
            if (current >= version) continue;

            await using var tx = await db.BeginTransactionAsync();
            try
            {
                await db.ExecuteAsync(sql, transaction: tx);
                await db.ExecuteAsync(
                    "UPDATE schema_version SET version = @Version",
                    new { Version = version },
                    tx);
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }
}
