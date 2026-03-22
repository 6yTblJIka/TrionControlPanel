using MudBlazor;

namespace Trion.Web.Services;

// ── Per-color accent (dark and light accent hex + contrast foreground) ────────

public sealed record TrionColorAccent(
    string Primary,       // Accent.Primary
    string Foreground);   // Text on an accent-colored button

// ── One color entry — only accent changes, base backgrounds are shared ────────

public sealed record TrionColorEntry(
    string Id,
    string DisplayName,
    string AccentHex,     // Vivid swatch for the picker circle (dark accent)
    TrionColorAccent Dark,
    TrionColorAccent Light);

// ── Theme registry ────────────────────────────────────────────────────────────

public static class TrionThemes
{
    public static readonly IReadOnlyList<TrionColorEntry> Colors =
    [
        new("TrionBlue", "Trion Blue", "#00D4FF",
            Dark:  new("#00D4FF", "#001E2E"),
            Light: new("#0097B5", "#FFFFFF")),

        new("Ocean", "Ocean", "#F43F5E",
            Dark:  new("#F43F5E", "#FFFFFF"),
            Light: new("#BE123C", "#FFFFFF")),

        new("Green", "Green", "#4CAF50",
            Dark:  new("#4CAF50", "#FFFFFF"),
            Light: new("#2E7D32", "#FFFFFF")),

        new("Orange", "Orange", "#FF9800",
            Dark:  new("#FF9800", "#16120E"),
            Light: new("#E65100", "#FFFFFF")),

        new("Lila", "Lila", "#AB47BC",
            Dark:  new("#AB47BC", "#FFFFFF"),
            Light: new("#7B1FA2", "#FFFFFF")),
    ];

    public static IReadOnlyList<TrionColorEntry> All  => Colors;
    public static TrionColorEntry Get(string id)      => Colors.FirstOrDefault(c => c.Id == id) ?? Colors[0];

    /// <summary>
    /// Builds a <see cref="MudTheme"/> with shared dark/light base palettes
    /// and only the accent (Primary) overridden from the chosen color entry.
    /// </summary>
    public static MudTheme BuildMudTheme(string colorId)
    {
        var e = Get(colorId);
        return new MudTheme
        {
            PaletteDark      = MakeDark(e.Dark.Primary,  e.Dark.Foreground),
            PaletteLight     = MakeLight(e.Light.Primary, e.Light.Foreground),
            LayoutProperties = new LayoutProperties { DrawerWidthLeft = "220px" },
        };
    }

    // ── Shared dark base — from DarkTheme.xaml ────────────────────────────────
    // Background.Primary #13131F | .Secondary #1A1A28 | Sidebar #0F0F1A
    // Border.Default #282840
    // Foreground.Primary #E4E4F0 | .Secondary #8888A8 | .Muted #505070

    private static PaletteDark MakeDark(string accent, string accentFg) => new()
    {
        Primary                  = accent,
        PrimaryContrastText      = accentFg,
        Secondary                = accent,
        Background               = "#13131F",
        Surface                  = "#1A1A28",
        BackgroundGray           = "#0F0F1A",
        DrawerBackground         = "#0F0F1A",
        AppbarBackground         = "#0F0F1A",
        DrawerText               = "#8888A8",
        DrawerIcon               = "#8888A8",
        AppbarText               = "#E4E4F0",
        TextPrimary              = "#E4E4F0",
        TextSecondary            = "#8888A8",
        TextDisabled             = "#505070",
        ActionDefault            = "#8888A8",
        ActionDisabled           = "#505070",
        ActionDisabledBackground = "#50507033",
        Divider                  = "#282840",
        DividerLight             = "#282840",
        LinesDefault             = "#282840",
        TableLines               = "#282840",
        GrayDefault              = "#505070",
        GrayLight                = "#1A1A28",
        GrayLighter              = "#13131F",
        Dark                     = "#0F0F1A",
        Success                  = "#66BB6A",
        Warning                  = "#FFB347",
        Error                    = "#FF4757",
        Info                     = "#4A86FF",
    };

    // ── Shared light base — neutral light (all color light XAMLs share this) ──
    // Background.Primary #F5F5F5 | .Secondary #FFFFFF | Sidebar #EBEBEB
    // Border.Default #E0E0E0
    // Foreground.Primary #1A1A1A | .Secondary #616161 | .Disabled #C0C0C0

    private static PaletteLight MakeLight(string accent, string accentFg) => new()
    {
        Primary                  = accent,
        PrimaryContrastText      = accentFg,
        Secondary                = accent,
        Background               = "#F5F5F5",
        Surface                  = "#FFFFFF",
        BackgroundGray           = "#EBEBEB",
        DrawerBackground         = "#EBEBEB",
        AppbarBackground         = "#EBEBEB",
        DrawerText               = "#616161",
        DrawerIcon               = "#616161",
        AppbarText               = "#1A1A1A",
        TextPrimary              = "#1A1A1A",
        TextSecondary            = "#616161",
        TextDisabled             = "#C0C0C0",
        ActionDefault            = "#616161",
        ActionDisabled           = "#C0C0C0",
        ActionDisabledBackground = "#C0C0C033",
        Divider                  = "#E0E0E0",
        DividerLight             = "#E0E0E0",
        LinesDefault             = "#E0E0E0",
        TableLines               = "#E0E0E0",
        GrayDefault              = "#C0C0C0",
        GrayLight                = "#FFFFFF",
        GrayLighter              = "#F5F5F5",
        Dark                     = "#EBEBEB",
        Success                  = "#2E7D32",
        Warning                  = "#E65100",
        Error                    = "#C62828",
        Info                     = "#0288D1",
    };
}
