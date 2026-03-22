namespace Trion.Core.Abstractions.Auth;

public interface IEmulatorAuthService
{
    Task<AuthResult> AuthenticateAsync(
        string username,
        string password,
        string ip = "",
        CancellationToken ct = default);
}
