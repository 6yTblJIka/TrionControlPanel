namespace Trion.Core.Plugins;

public sealed class PluginNavRegistry : IPluginNavRegistry
{
    private readonly List<PluginNavItem> _items = [];

    public void Register(PluginNavItem item) => _items.Add(item);

    public IReadOnlyList<PluginNavItem> Items =>
        [.. _items.OrderBy(i => i.Order).ThenBy(i => i.Label)];
}
