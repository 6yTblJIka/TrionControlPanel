namespace Trion.Core.Plugins;

/// <summary>
/// A sidebar navigation entry contributed by a plugin.
/// Use MudBlazor icon strings for <see cref="Icon"/>
/// (e.g. <c>Icons.Material.Filled.Extension</c>).
/// </summary>
public sealed record PluginNavItem(
    string Label,
    string Href,
    string Icon,
    int    Order = 100);
