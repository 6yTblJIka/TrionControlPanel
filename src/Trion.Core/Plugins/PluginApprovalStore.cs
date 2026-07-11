using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Trion.Core.Plugins;

/// <summary>
/// Persists approved plugin SHA-256 hashes in settings.db.
///
/// Each row is HMAC-SHA256 signed with <paramref name="hmacKey"/> (the same
/// Agent:HmacSharedKey used for IPC). Editing the database directly cannot
/// produce a valid signature without knowing the key, so a raw DB INSERT does
/// not grant plugin approval.
/// </summary>
public sealed class PluginApprovalStore : IPluginApprovalStore
{
    private readonly string _dbPath;
    private readonly byte[] _keyBytes;

    public PluginApprovalStore(string dbPath, string hmacKey)
    {
        _dbPath   = dbPath;
        _keyBytes = Encoding.UTF8.GetBytes(hmacKey);
        EnsureTable();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    private void EnsureTable()
    {
        using var conn = Open();
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS approved_plugins (
                hash        TEXT PRIMARY KEY,
                signature   TEXT NOT NULL DEFAULT '',
                notes       TEXT NOT NULL DEFAULT '',
                approved_at TEXT NOT NULL DEFAULT ''
            )
            """);
    }

    // HMAC-SHA256(key, hash) -- stored as lowercase hex.
    private string Sign(string hash) =>
        Convert.ToHexString(
            HMACSHA256.HashData(_keyBytes, Encoding.UTF8.GetBytes(hash)))
        .ToLowerInvariant();

    public bool IsApproved(string hash)
    {
        using var conn = Open();
        var stored = conn.QueryFirstOrDefault<string>(
            "SELECT signature FROM approved_plugins WHERE hash = @hash",
            new { hash });

        if (stored is null) return false;

        // Constant-time compare to prevent timing attacks.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(stored),
            Encoding.UTF8.GetBytes(Sign(hash)));
    }

    public void Approve(string hash, string notes = "")
    {
        using var conn = Open();
        conn.Execute("""
            INSERT INTO approved_plugins (hash, signature, notes, approved_at)
            VALUES (@hash, @sig, @notes, @at)
            ON CONFLICT(hash) DO UPDATE
                SET signature = @sig, notes = @notes, approved_at = @at
            """, new { hash, sig = Sign(hash), notes, at = DateTime.UtcNow.ToString("o") });
    }

    public void Revoke(string hash)
    {
        using var conn = Open();
        conn.Execute(
            "DELETE FROM approved_plugins WHERE hash = @hash",
            new { hash });
    }

    public IReadOnlyList<ApprovedPlugin> GetAll()
    {
        using var conn = Open();
        return [.. conn.Query<ApprovedPlugin>(
            "SELECT hash AS Hash, notes AS Notes, approved_at AS ApprovedAt FROM approved_plugins")];
    }
}
