using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trion.Desktop.Services;

namespace Trion.Desktop.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAccountService      _account;
    private readonly ILocalizationService _loc;

    // -- Login form -------------------------------------------------------------
    [ObservableProperty] private string _username      = string.Empty;
    [ObservableProperty] private string _password      = string.Empty;
    [ObservableProperty] private bool   _rememberMe    = false;

    // -- 2FA form ---------------------------------------------------------------
    [ObservableProperty] private string _twoFactorCode        = string.Empty;
    [ObservableProperty] private bool   _isTwoFactorMode;
    [ObservableProperty] private string _twoFactorIconGlyph   = ""; // envelope (email default)
    [ObservableProperty] private string _twoFactorTitle       = "Check your email";
    [ObservableProperty] private string _twoFactorDescription = "A 6-digit code was sent to the address linked to your account.";

    // -- Shared -----------------------------------------------------------------
    [ObservableProperty] private string _errorMessage  = string.Empty;
    [ObservableProperty] private bool   _isBusy;

    /// <summary>Holds the challenge token between login step and 2FA verification step.</summary>
    private string _challengeToken = string.Empty;

    /// <summary>Holds RememberMe from the login form so it survives the 2FA step.</summary>
    private bool _rememberMePending;

    public ILocalizationService Loc => _loc;

    public static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(4) ?? "--";

    /// <summary>Set to true when the full login flow completes; the window closes.</summary>
    public bool LoginSucceeded { get; private set; }

    /// <summary>Set to true when the user chooses to continue without an account.</summary>
    public bool SkippedLogin { get; private set; }

    public event Action? RequestClose;

    public LoginViewModel(IAccountService account, ILocalizationService loc)
    {
        _account = account;
        _loc     = loc;
    }

    // -- Step 1: credentials ---------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;

        var result = await _account.LoginAsync(Username.Trim(), Password, RememberMe);

        IsBusy = false;

        if (result.RequiresTwoFactor)
        {
            _challengeToken    = result.ChallengeToken ?? "";
            _rememberMePending = RememberMe;
            Password           = string.Empty;   // clear from memory

            if (result.TwoFactorType == "totp")
            {
                TwoFactorIconGlyph   = ""; // key icon (Font Awesome)
                TwoFactorTitle       = "Open your authenticator app";
                TwoFactorDescription = "Enter the 6-digit code shown in your authenticator app.";
            }
            else
            {
                TwoFactorIconGlyph   = ""; // envelope icon (Font Awesome)
                TwoFactorTitle       = "Check your email";
                TwoFactorDescription = "A 6-digit code was sent to the address linked to your account.";
            }

            IsTwoFactorMode = true;
            ErrorMessage    = string.Empty;
            return;
        }

        if (result.Success)
        {
            LoginSucceeded = true;
            RequestClose?.Invoke();
        }
        else
        {
            ErrorMessage = result.Error ?? "Login failed.";
        }
    }

    private bool CanLogin() => !IsBusy && !string.IsNullOrWhiteSpace(Username) && Password.Length > 0;

    // -- Step 2: 2FA code entry -------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyTwoFactorAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;

        var result = await _account.VerifyTwoFactorAsync(
            _challengeToken, TwoFactorCode.Trim(), _rememberMePending);

        IsBusy = false;

        if (result.Success)
        {
            LoginSucceeded = true;
            RequestClose?.Invoke();
        }
        else
        {
            ErrorMessage = result.Error ?? "Verification failed.";
        }
    }

    private bool CanVerify() => !IsBusy && TwoFactorCode.Trim().Length == 6;

    [RelayCommand]
    private void BackToLogin()
    {
        IsTwoFactorMode = false;
        TwoFactorCode   = string.Empty;
        ErrorMessage    = string.Empty;
        _challengeToken = string.Empty;
    }

    // -- Guest / offline -------------------------------------------------------

    [RelayCommand]
    private void ContinueOffline()
    {
        SkippedLogin = true;
        _account.ContinueAsGuest();
        RequestClose?.Invoke();
    }

    // -- Property-change wiring -------------------------------------------------

    partial void OnUsernameChanged(string value)      => LoginCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value)      => LoginCommand.NotifyCanExecuteChanged();
    partial void OnTwoFactorCodeChanged(string value) => VerifyTwoFactorCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value)
    {
        LoginCommand.NotifyCanExecuteChanged();
        VerifyTwoFactorCommand.NotifyCanExecuteChanged();
    }
}
