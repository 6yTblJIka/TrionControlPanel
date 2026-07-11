using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using Trion.Desktop.Views;
using WinForms = System.Windows.Forms;

namespace Trion.Desktop.Services;

public sealed class TrayService : ITrayService
{
    private readonly ILocalizationService _loc;
    private WinForms.NotifyIcon? _icon;
    private Window?              _window;

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public TrayService(ILocalizationService loc) => _loc = loc;

    public void Initialize(Window mainWindow)
    {
        _window = mainWindow;

        Icon appIcon;
        try
        {
            var sri = Application.GetResourceStream(
                          new Uri("pack://application:,,,/Resources/Images/favicon.ico"));
            appIcon = sri is not null ? new Icon(sri.Stream) : SystemIcons.Application;
        }
        catch
        {
            appIcon = SystemIcons.Application;
        }

        _icon = new WinForms.NotifyIcon
        {
            Text    = "Trion Control Panel",
            Icon    = appIcon,
            Visible = true,
        };

        _icon.DoubleClick += (_, _) => Application.Current.Dispatcher.BeginInvoke(Restore);
        _icon.MouseUp     += OnMouseUp;
    }

    private void OnMouseUp(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button != WinForms.MouseButtons.Right) return;

        int x = WinForms.Cursor.Position.X;
        int y = WinForms.Cursor.Position.Y;

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var menu = new TrayMenuWindow(Restore, Exit, _loc, x, y);
            menu.Show();
        });
    }

    public void ShowBalloon(string title, string message)
    {
        _icon?.ShowBalloonTip(3000, title, message, WinForms.ToolTipIcon.Info);
    }

    private void Restore()
    {
        if (_window is null) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private static void Exit() => Application.Current.Shutdown();

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
