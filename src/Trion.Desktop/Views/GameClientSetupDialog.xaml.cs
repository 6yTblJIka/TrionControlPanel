using System.IO;
using System.Windows;
using Microsoft.Win32;
using Trion.Desktop.Services;

namespace Trion.Desktop.Views;

public partial class GameClientSetupDialog : Window
{
    private readonly string             _expansion;
    private readonly IGameClientService _gameClient;

    public GameClientSetupDialog(string expansion, IGameClientService gameClient)
    {
        InitializeComponent();

        _expansion  = expansion;
        _gameClient = gameClient;

        // Fill in expansion badge and accent colour
        var name    = gameClient.GetExpansionDisplayName(expansion);
        var version = gameClient.GetExpansionVersion(expansion);
        ExpansionLabel.Text = string.IsNullOrEmpty(version) ? name : $"{name}  ({version})";
        IconBorder.SetResourceReference(BackgroundProperty, "Accent.Primary");

        // Pre-fill if the user already selected a path in a previous session
        var saved = gameClient.GetClientPath(expansion);
        if (!string.IsNullOrEmpty(saved))
            ClientPathBox.Text = saved;
    }

    // -- Event handlers ----------------------------------------------------------

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select your WoW client folder",
        };

        if (dlg.ShowDialog(this) == true)
            ClientPathBox.Text = dlg.FolderName;
    }

    private void OnPathTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var path = ClientPathBox.Text.Trim();
        ContinueButton.IsEnabled = Directory.Exists(path);
    }

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        var path = ClientPathBox.Text.Trim();
        if (!Directory.Exists(path))
        {
            TrionMessageBox.Show("The selected folder does not exist.", "Invalid Path",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _gameClient.Configure(_expansion, path);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { DialogResult = false; }
    }
}
