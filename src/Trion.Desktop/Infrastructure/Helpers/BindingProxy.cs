using System.Windows;

namespace Trion.Desktop.Infrastructure.Helpers;

/// <summary>
/// Bridges DataContext into Freezable elements (e.g. DropShadowEffect) that cannot
/// walk the visual tree. Define as a resource on the nearest FrameworkElement ancestor
/// and bind to its Data property via Source={StaticResource ...}.
/// </summary>
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
