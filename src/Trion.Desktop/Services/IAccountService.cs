using Trion.Desktop.Models;

namespace Trion.Desktop.Services;

/// <summary>
/// Result returned from <see cref="IAccountService.LoginAsync"/>.
/// <para>
/// When <see cref="RequiresTwoFactor"/> is true the caller must prompt for a code and
/// call <see cref="IAccountService.VerifyTwoFactorAsync"/>.
/// <see cref="TwoFactorType"/> is <c>"totp"</c> (authenticator app) or <c>"email"</c>.
/// </para>
/// </summary>
public record LoginResult(
    bool    Success,
    string? Error             = null,
    bool    RequiresTwoFactor = false,
    string? ChallengeToken    = null,
    string? TwoFactorType     = null);

public interface IAccountService
{
    string?         Username   { get; }
    string?         Email      { get; }
    bool            IsLoggedIn { get; }
    bool            IsGuest    { get; }
    AccountProfile? Profile    { get; }

    event Action? SessionChanged;

    /// <summary>
    /// Validate credentials against the server.
    /// On success: <c>Success = true</c>.
    /// When 2FA is required: <c>RequiresTwoFactor = true</c> and <c>ChallengeToken</c> is set -- call <see cref="VerifyTwoFactorAsync"/> next.
    /// </summary>
    Task<LoginResult> LoginAsync(string username, string password, bool rememberMe = false, CancellationToken ct = default);

    /// <summary>
    /// Submit the 6-digit code (email OTP or TOTP) to complete the login challenge.
    /// </summary>
    Task<LoginResult> VerifyTwoFactorAsync(string challengeToken, string code, bool rememberMe = false, CancellationToken ct = default);

    void Logout();
    void ContinueAsGuest();
}
