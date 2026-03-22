using Trion.Core.Abstractions.Settings;

namespace Trion.Core.Auth;

public static class BootstrapAccount
{
    private const string UsernameKey     = "bootstrap_username";
    private const string PasswordHashKey = "bootstrap_password_hash";

    public const string DefaultUsername = "admin";

    /// <summary>
    /// Ensures a bootstrap (Trion-native Owner) account exists in settings.
    /// Called once at application startup before the web host begins accepting requests.
    /// </summary>
    public static async Task EnsureCreatedAsync(
        ISettingsRepository settings,
        string? initialPassword = null,
        CancellationToken ct = default)
    {
        var existing = await settings.GetAsync<string>(UsernameKey, ct);
        if (existing is not null)
            return;

        var password = initialPassword ?? GenerateTemporaryPassword();
        var hash     = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

        await settings.SetAsync(UsernameKey, DefaultUsername, ct);
        await settings.SetAsync(PasswordHashKey, hash, ct);

        // Print temporary password to console only — never write to disk in plaintext
        Console.WriteLine("=== TRION FIRST RUN ===");
        Console.WriteLine($"Bootstrap account created. Username: {DefaultUsername}");
        Console.WriteLine($"Temporary password: {password}");
        Console.WriteLine("Save this now — it will not be shown again.");
        Console.WriteLine("=======================");
        await Task.CompletedTask;
    }

    public static async Task<bool> ValidateAsync(
        ISettingsRepository settings,
        string username,
        string password,
        CancellationToken ct = default)
    {
        var storedUsername = await settings.GetAsync<string>(UsernameKey, ct);
        if (storedUsername is null || !storedUsername.Equals(username, StringComparison.Ordinal))
            return false;

        var storedHash = await settings.GetAsync<string>(PasswordHashKey, ct);
        if (storedHash is null)
            return false;

        return BCrypt.Net.BCrypt.Verify(password, storedHash);
    }

    private static string GenerateTemporaryPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$";
        return System.Security.Cryptography.RandomNumberGenerator.GetString(chars, 16);
    }
}
