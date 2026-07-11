using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trion.Desktop.Models;
using Trion.Desktop.Services;

namespace Trion.Desktop.ViewModels;

/// <summary>One entry in the category picker (repack / Trion itself / something else).</summary>
public sealed record BugReportCategoryOption(string Key, string Label)
{
    // The ComboBox's collapsed selection box renders via this ToString() rather than
    // DisplayMemberPath (a WPF quirk with custom ComboBox templates) -- without this
    // override it would show the record's default "BugReportCategoryOption { Key = ..., Label = ... }".
    public override string ToString() => Label;
}

/// <summary>One installed repack, offered in the "Which repack?" dropdown.</summary>
public sealed record BugReportRepackOption(string Slug, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public partial class BugReportViewModel : ObservableObject
{
    public const string CategoryRepack = "repack";
    public const string CategoryTrion  = "trion";
    public const string CategoryOther  = "other";

    private readonly IBugReportService    _bugReports;
    private readonly ILocalizationService _loc;

    public ILocalizationService Loc => _loc;

    public static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(4) ?? "--";

    public ObservableCollection<BugReportCategoryOption> Categories       { get; } = [];
    public ObservableCollection<BugReportRepackOption>   InstalledRepacks { get; } = [];

    /// <summary>True when at least one repack is installed, so the dropdown can be shown
    /// instead of the free-text fallback field.</summary>
    public bool HasInstalledRepacks => InstalledRepacks.Count > 0;

    [ObservableProperty] private BugReportCategoryOption? _selectedCategory;
    [ObservableProperty] private BugReportRepackOption?   _selectedRepack;
    [ObservableProperty] private string  _repackFreeText = string.Empty;
    [ObservableProperty] private string  _description    = string.Empty;
    [ObservableProperty] private string  _content        = string.Empty;
    [ObservableProperty] private string  _errorMessage   = string.Empty;
    [ObservableProperty] private bool    _isBusy;
    [ObservableProperty] private bool    _isSubmitted;

    /// <summary>True when the "Which repack?" field should be shown.</summary>
    public bool IsRepackCategorySelected => SelectedCategory?.Key == CategoryRepack;

    public event Action? RequestClose;

    public BugReportViewModel(IBugReportService bugReports, ILocalizationService loc, ISettingsService settings)
    {
        _bugReports = bugReports;
        _loc        = loc;

        Categories.Add(new BugReportCategoryOption(CategoryRepack, _loc["BugReportCategoryRepack"]));
        Categories.Add(new BugReportCategoryOption(CategoryTrion,  _loc["BugReportCategoryTrion"]));
        Categories.Add(new BugReportCategoryOption(CategoryOther,  _loc["BugReportCategoryOther"]));
        SelectedCategory = Categories[0];

        // Offer a dropdown of installed repacks (falls back to free text when none are installed).
        // Repacks are loaded dynamically from the catalog-driven state -- RepackInstallPaths is
        // keyed by slug but an entry can be legitimately absent/empty ("use default location"),
        // so an empty path must NOT be treated as "not installed"; it must fall back to the same
        // default install directory the rest of the app uses (see ServerControlViewModel).
        var cfg = settings.Current;

        foreach (var (slug, installed) in cfg.RepackInstalled)
        {
            if (!installed) continue;

            var installPath = cfg.RepackInstallPaths.GetValueOrDefault(slug, "");
            if (string.IsNullOrEmpty(installPath))
                installPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "servers", slug);

            var manifest    = RepackManifest.Load(installPath);
            var displayName = !string.IsNullOrWhiteSpace(manifest?.RealmName) ? manifest.RealmName : slug;
            InstalledRepacks.Add(new BugReportRepackOption(slug, displayName));
        }

        if (InstalledRepacks.Count > 0)
            SelectedRepack = InstalledRepacks[0];
    }

    partial void OnSelectedCategoryChanged(BugReportCategoryOption? value)
        => OnPropertyChanged(nameof(IsRepackCategorySelected));

    partial void OnDescriptionChanged(string value)  => SubmitCommand.NotifyCanExecuteChanged();
    partial void OnContentChanged(string value)      => SubmitCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value)         => SubmitCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;

        string? repackSlug = null;
        if (IsRepackCategorySelected)
            repackSlug = HasInstalledRepacks ? SelectedRepack?.Slug : RepackFreeText;

        var result = await _bugReports.SubmitAsync(
            SelectedCategory?.Key ?? CategoryOther,
            repackSlug,
            Description,
            Content);

        IsBusy = false;

        if (result.Success)
        {
            IsSubmitted = true;
        }
        else
        {
            ErrorMessage = result.Error ?? _loc["BugReportError"];
        }
    }

    private bool CanSubmit() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(Description) &&
        !string.IsNullOrWhiteSpace(Content);

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();
}
