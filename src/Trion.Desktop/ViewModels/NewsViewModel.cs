using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Trion.Desktop.Models;
using Trion.Desktop.Services;

namespace Trion.Desktop.ViewModels;

/// <summary>Full-page "Trion News" view, reached via the title-bar news popup's "View All" button.</summary>
public partial class NewsViewModel : ObservableObject
{
    private readonly INewsService         _news;
    private readonly ILocalizationService _loc;

    public ILocalizationService Loc => _loc;

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isEmpty;
    public ObservableCollection<NewsItem> NewsItems { get; } = [];

    public NewsViewModel(INewsService news, ILocalizationService loc)
    {
        _news = news;
        _loc  = loc;

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;

        // No dedicated "all history" endpoint -- request a generous page of the most
        // recent items instead.
        var items = await _news.GetLatestAsync(50);

        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            NewsItems.Clear();
            foreach (var item in items)
                NewsItems.Add(item);
            IsLoading = false;
            IsEmpty   = NewsItems.Count == 0;
        });
    }
}
