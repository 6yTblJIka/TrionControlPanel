namespace Trion.Web.Services;

/// <summary>
/// Scoped service (one per Blazor circuit) that tracks the active Trion theme
/// color and light/dark variant, and notifies subscribers when either changes.
/// </summary>
public sealed class ThemeState
{
    public string CurrentColorId { get; private set; } = "TrionBlue";
    public bool   IsDark         { get; private set; } = true;

    public event Action? OnChanged;

    public void SetColor(string colorId)
    {
        if (CurrentColorId == colorId) return;
        CurrentColorId = colorId;
        OnChanged?.Invoke();
    }

    public void SetVariant(bool isDark)
    {
        if (IsDark == isDark) return;
        IsDark = isDark;
        OnChanged?.Invoke();
    }
}
