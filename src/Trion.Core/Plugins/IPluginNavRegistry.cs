namespace Trion.Core.Plugins;

/// <summary>
/// Singleton service plugins use to add items to the sidebar navigation.
/// Items are registered during <see cref="ITrionPlugin.ConfigureServices"/> and
/// rendered by NavMenu.razor.
/// </summary>
public interface IPluginNavRegistry
{
    void Register(PluginNavItem item);
    IReadOnlyList<PluginNavItem> Items { get; }
}
