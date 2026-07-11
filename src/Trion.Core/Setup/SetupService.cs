using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using MySqlConnector;
using Trion.Core.Abstractions.Services;
using Trion.Core.Abstractions.Settings;
using Trion.Core.Constants;
using Trion.Core.Database;

namespace Trion.Core.Setup;

/// <summary>
/// Business logic for the first-run setup wizard.
/// Validates connections, creates accounts, and records completion.
/// </summary>
public sealed class SetupService
{
    private readonly ISettingsRepository _settings;

    public SetupService(ISettingsRepository settings)
    {
        _settings = settings;
    }

    // -- Setup state ------------------------------------------------------

    /// <summary>
    /// Returns true when setup has been completed (or a legacy bootstrap account exists).
    /// </summary>
    public async Task<bool> IsCompletedAsync(CancellationToken ct = default)
    {
        var flag = await _settings.GetAsync<bool?>(SetupKeys.Completed, ct);
        if (flag == true) return true;

        // Backward-compat: bootstrap account existed before wizard was added
        var legacy = await _settings.GetAsync<string>("bootstrap_username", ct);
        if (legacy is not null)
        {
            await _settings.SetAsync(SetupKeys.Completed, true, ct);
            return true;
        }

        return false;
    }

    // -- Connection validation --------------------------------------------

    public async Task<(bool Ok, string? Error)> TestMySqlAsync(
        string connStr, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync(ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Ok, string? Error)> ValidatePhoenixAccountAsync(
        string username, string password, EmulatorType emulatorType,
        string connStr, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync(ct);

            var hash = ComputeWowHash(username, password);

            var storedHash = await conn.QuerySingleOrDefaultAsync<string?>(
                "SELECT sha_pass_hash FROM account WHERE username = @u",
                new { u = username.ToUpperInvariant() });

            if (storedHash is null)
                return (false, "Account not found in the emulator database.");

            if (!storedHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
                return (false, "Invalid password.");

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Validates credentials against the Flying-Phoenix API.
    /// No emulator database connection is required.
    /// </summary>
    public async Task<(bool Ok, string? Error)> ValidatePhoenixApiAccountAsync(
        string username, string password, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var url  = ApiEndpoints.AccountLoginUrl(ExternalLinks.ApiDefaultHost);
            var resp = await http.PostAsJsonAsync(url, new { username, password }, ct);

            if (!resp.IsSuccessStatusCode)
                return (false, $"Phoenix API returned {(int)resp.StatusCode}. Check your credentials.");

            var body = await resp.Content.ReadFromJsonAsync<PhoenixLoginResponse>(ct);
            if (body is null || string.IsNullOrEmpty(body.Token))
                return (false, "Invalid response from the Phoenix API.");

            if (body.IsBanned)
                return (false, "This account is banned.");

            return (true, null);
        }
        catch (HttpRequestException)
        {
            return (false, "Cannot reach the Trion/Phoenix server. Check your internet connection.");
        }
        catch (TaskCanceledException)
        {
            return (false, "Request timed out.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task CompleteWithPhoenixApiAccountAsync(
        string username,
        bool useMySql, string? mysqlConnStr,
        CancellationToken ct = default)
    {
        // Store only the username -- no password hash.
        // Every login will re-verify live against the Phoenix API.
        await _settings.SetAsync("bootstrap_username",  username,      ct);
        await _settings.SetAsync(SetupKeys.AccountType, "phoenix_api", ct);

        await FinalizeAsync(useMySql, mysqlConnStr, ct);
    }

    // -- Completion -------------------------------------------------------

    public async Task CompleteWithLocalAccountAsync(
        string username, string password,
        bool useMySql, string? mysqlConnStr,
        CancellationToken ct = default)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

        await _settings.SetAsync("bootstrap_username",    username, ct);
        await _settings.SetAsync("bootstrap_password_hash", hash,   ct);
        await _settings.SetAsync(SetupKeys.AccountType,   "local",  ct);

        await FinalizeAsync(useMySql, mysqlConnStr, ct);
    }

    public async Task CompleteWithPhoenixAccountAsync(
        string username, string password,
        EmulatorType emulatorType, string emulatorConnStr,
        bool useMySql, string? mysqlConnStr,
        CancellationToken ct = default)
    {
        // Store BCrypt of the Phoenix password -- user logs into panel with their game password.
        var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

        await _settings.SetAsync("bootstrap_username",    username,                  ct);
        await _settings.SetAsync("bootstrap_password_hash", hash,                   ct);
        await _settings.SetAsync(SetupKeys.AccountType,   "phoenix",                ct);
        await _settings.SetAsync(SetupKeys.PhoenixType,   emulatorType.ToString(),  ct);
        await _settings.SetAsync(SetupKeys.PhoenixConn,   emulatorConnStr,          ct);

        await FinalizeAsync(useMySql, mysqlConnStr, ct);
    }

    // -- Private helpers --------------------------------------------------

    private async Task FinalizeAsync(bool useMySql, string? mysqlConnStr, CancellationToken ct)
    {
        // Generate the HMAC shared key used between Trion.Web and Trion.Agent.
        // Write it to the platform secrets file; also persist it in the settings DB as a backup.
        var existingKey = await _settings.GetAsync<string>(SetupKeys.AgentHmacKey, ct);
        if (string.IsNullOrWhiteSpace(existingKey))
        {
            var hmacKey = AgentSecretsFile.GenerateAndWrite();
            await _settings.SetAsync(SetupKeys.AgentHmacKey, hmacKey, ct);
        }
        else if (!AgentSecretsFile.Exists)
        {
            // Secrets file was deleted but key is still in DB — restore the file.
            AgentSecretsFile.Write(existingKey);
        }

        if (useMySql && !string.IsNullOrWhiteSpace(mysqlConnStr))
        {
            // Run MySQL migrations and seed the completed flag there before writing config.
            await using var conn = new MySqlConnection(mysqlConnStr);
            await conn.OpenAsync(ct);
            await MySqlSettingsMigrations.RunAsync(conn);

            var mysqlRepo = new MySqlSettingsRepository(mysqlConnStr);
            await CopySettingsToMySqlAsync(mysqlRepo, ct);
            await mysqlRepo.SetAsync(SetupKeys.Completed, true, ct);

            await _settings.SetAsync(SetupKeys.DbType,    "mysql",     ct);
            await _settings.SetAsync(SetupKeys.DbConnStr, mysqlConnStr, ct);

            WriteMySqlConfigFile(mysqlConnStr);
        }
        else
        {
            await _settings.SetAsync(SetupKeys.DbType, "sqlite", ct);
        }

        await _settings.SetAsync(SetupKeys.Completed, true, ct);
    }

    /// <summary>Copies bootstrap + setup keys from SQLite to MySQL after switching.</summary>
    private async Task CopySettingsToMySqlAsync(ISettingsRepository target, CancellationToken ct)
    {
        var keysToMigrate = new[]
        {
            "bootstrap_username", "bootstrap_password_hash",
            SetupKeys.AccountType, SetupKeys.PhoenixType, SetupKeys.PhoenixConn,
            SetupKeys.DbType, SetupKeys.DbConnStr,
        };

        foreach (var key in keysToMigrate)
        {
            var value = await _settings.GetAsync<string>(key, ct);
            if (value is not null)
                await target.SetAsync(key, value, ct);
        }
    }

    private static void WriteMySqlConfigFile(string connStr)
    {
        var path    = Path.Combine(AppContext.BaseDirectory, "trion-db.json");
        var content = JsonSerializer.Serialize(new
        {
            DatabaseType     = "mysql",
            ConnectionString = connStr,
        }, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(path, content);
    }

    private static string ComputeWowHash(string username, string password)
    {
        var input = $"{username.ToUpperInvariant()}:{password.ToUpperInvariant()}";
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    // -- DTOs -----------------------------------------------------------------

    private sealed record PhoenixLoginResponse(
        [property: JsonPropertyName("token")]      string  Token,
        [property: JsonPropertyName("is_banned")]  bool    IsBanned
    );
}
