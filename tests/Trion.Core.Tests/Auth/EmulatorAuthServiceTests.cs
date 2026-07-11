using Microsoft.Data.Sqlite;
using Trion.Core.Tests;
using Microsoft.Extensions.Options;
using Trion.Core.Auth;
using Trion.Core.Logging;
using Trion.Core.Settings;

namespace Trion.Core.Tests.Auth;

public sealed class EmulatorAuthServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection   _conn;
    private readonly SettingsRepository _settings;
    private readonly AuditLogger        _audit;
    private readonly string             _auditDir;

    public EmulatorAuthServiceTests()
    {
        var dbName = Guid.NewGuid().ToString("N");
        _conn     = new SqliteConnection($"Data Source={dbName};Mode=Memory;Cache=Shared");
        _conn.Open();
        _settings = new SettingsRepository($"{dbName};Mode=Memory;Cache=Shared");
        _auditDir = Path.Combine(Path.GetTempPath(), "trion_audit_" + dbName);
        _audit    = new AuditLogger(_auditDir);
    }

    public async Task InitializeAsync()
    {
        await SettingsMigrations.RunAsync(_conn);
        await BootstrapAccount.EnsureCreatedAsync(_settings, initialPassword: "testpass");
    }

    public async Task DisposeAsync()
    {
        await _audit.DisposeAsync();
        await _conn.DisposeAsync();
        if (Directory.Exists(_auditDir))
            Directory.Delete(_auditDir, recursive: true);
    }

    // -- helpers --------------------------------------------------------------

    private EmulatorAuthService CreateSut(EmulatorAuthOptions? opts = null)
    {
        opts ??= new EmulatorAuthOptions();
        return new EmulatorAuthService(
            _settings,
            _audit,
            new FakeOptionsMonitor<EmulatorAuthOptions>(opts),
            TestLogger.Instance);
    }

    // -- Owner / bootstrap account ---------------------------------------------

    [Fact]
    public async Task Owner_Login_Succeeds_WithCorrectCredentials()
    {
        var sut    = CreateSut();
        var result = await sut.AuthenticateAsync("admin", "testpass");

        Assert.True(result.Success);
        Assert.Equal("admin", result.Username);
        Assert.Equal(3, result.GmLevel);
    }

    [Fact]
    public async Task Owner_Login_Fails_WithWrongPassword()
    {
        var sut    = CreateSut();
        var result = await sut.AuthenticateAsync("admin", "WRONG");

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task Unknown_Username_IsRejected()
    {
        var sut    = CreateSut();
        var result = await sut.AuthenticateAsync("nobody", "testpass");

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    // -- Lockout mechanics -----------------------------------------------------

    [Fact]
    public async Task FailedLogins_Lock_Account_AfterMaxAttempts()
    {
        var opts = new EmulatorAuthOptions { MaxFailedAttempts = 3, LockoutDuration = TimeSpan.FromMinutes(15) };
        var sut  = CreateSut(opts);

        for (int i = 0; i < 3; i++)
            await sut.AuthenticateAsync("admin", "bad");

        var result = await sut.AuthenticateAsync("admin", "bad");

        Assert.False(result.Success);
        Assert.Contains("locked", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Locked_Account_IsBlocked_BeforeLockoutExpires()
    {
        var opts = new EmulatorAuthOptions { MaxFailedAttempts = 2, LockoutDuration = TimeSpan.FromHours(1) };
        var sut  = CreateSut(opts);

        await sut.AuthenticateAsync("admin", "bad");
        await sut.AuthenticateAsync("admin", "bad");

        var result = await sut.AuthenticateAsync("admin", "bad");

        Assert.False(result.Success);
        Assert.Contains("locked", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Correct_Login_Clears_FailCount()
    {
        var opts = new EmulatorAuthOptions { MaxFailedAttempts = 3, LockoutDuration = TimeSpan.FromMinutes(15) };
        var sut  = CreateSut(opts);

        // Two failures
        await sut.AuthenticateAsync("admin", "bad");
        await sut.AuthenticateAsync("admin", "bad");

        // Successful login resets the counter
        var ok = await sut.AuthenticateAsync("admin", "testpass");
        Assert.True(ok.Success);

        // Should NOT be locked after the two earlier failures
        var after = await sut.AuthenticateAsync("admin", "bad");
        Assert.DoesNotContain("locked", after.FailureReason ?? "", StringComparison.OrdinalIgnoreCase);
    }
}

// -- Shared fakes -------------------------------------------------------------

file sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
