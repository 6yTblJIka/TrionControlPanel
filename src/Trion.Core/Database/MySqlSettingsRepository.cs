using System.Text.Json;
using Dapper;
using MySqlConnector;
using Trion.Core.Abstractions.Settings;

namespace Trion.Core.Database;

/// <summary>
/// MySQL-backed implementation of <see cref="ISettingsRepository"/>.
/// Schema matches the SQLite variant — drop-in replacement.
/// </summary>
public sealed class MySqlSettingsRepository : ISettingsRepository
{
    private readonly string _connectionString;

    public MySqlSettingsRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        await using var db = new MySqlConnection(_connectionString);
        var json = await db.QuerySingleOrDefaultAsync<string?>(
            "SELECT `value` FROM settings WHERE `key` = @Key",
            new { Key = key });

        if (json is null) return default;
        return JsonSerializer.Deserialize<T>(json);
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value);
        await using var db = new MySqlConnection(_connectionString);
        await db.ExecuteAsync("""
            INSERT INTO settings (`key`, `value`, updated_at)
            VALUES (@Key, @Value, @UpdatedAt)
            ON DUPLICATE KEY UPDATE `value` = VALUES(`value`), updated_at = VALUES(updated_at)
            """,
            new
            {
                Key       = key,
                Value     = json,
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
            });
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await using var db = new MySqlConnection(_connectionString);
        await db.ExecuteAsync(
            "DELETE FROM settings WHERE `key` = @Key",
            new { Key = key });
    }
}
