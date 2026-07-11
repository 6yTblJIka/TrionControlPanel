using System.Windows;
using System.Windows.Media;
using Trion.Desktop.Services;

namespace Trion.Desktop.Views;

public partial class TrionMessageBox : Window
{
    private static ILocalizationService? _loc;

    private MessageBoxResult _result = MessageBoxResult.None;

    private TrionMessageBox() => InitializeComponent();

    public static void SetLocalization(ILocalizationService loc) => _loc = loc;

    private static string L(string key, string fallback) => _loc?[key] is { } v && v != key ? v : fallback;

    // -- Public static API ----------------------------------------------------

    public static MessageBoxResult Show(
        string message,
        string title,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage  icon   = MessageBoxImage.Information)
    {
        var dlg = new TrionMessageBox();
        dlg.Setup(message, title, buttons, icon);
        dlg.ShowDialog();
        return dlg._result;
    }

    // -- Setup ----------------------------------------------------------------

    private void Setup(string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
    {
        MessageText.Text = message;
        TitleText.Text   = title;

        switch (icon)
        {
            case MessageBoxImage.Error:
                IconBorder.Background = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44));
                IconText.Text         = "✕";
                break;
            case MessageBoxImage.Warning:
                IconBorder.Background = new SolidColorBrush(Color.FromRgb(0xf5, 0x9e, 0x0b));
                IconText.Text         = "!";
                break;
            default: // Information
                IconBorder.SetResourceReference(BackgroundProperty, "Accent.Primary");
                IconText.Text = "i";
                break;
        }

        switch (buttons)
        {
            case MessageBoxButton.YesNo:
                AddButton(L("MsgBoxBtnNo",  "No"),  MessageBoxResult.No,  primary: false);
                AddButton(L("MsgBoxBtnYes", "Yes"), MessageBoxResult.Yes, primary: true);
                break;
            default: // OK
                AddButton(L("MsgBoxBtnOk", "OK"), MessageBoxResult.OK, primary: true);
                break;
        }
    }

    private void AddButton(string label, MessageBoxResult result, bool primary)
    {
        var btn = new Button
        {
            Content = label,
            Width   = 88,
            Height  = 34,
            Margin  = new Thickness(8, 0, 0, 0),
        };
        btn.SetResourceReference(StyleProperty,
            primary ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
        btn.Click += (_, _) => { _result = result; Close(); };
        ButtonPanel.Children.Add(btn);
    }

    // -- Interaction ----------------------------------------------------------

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { _result = MessageBoxResult.Cancel; Close(); }
        if (e.Key == Key.Enter)  { _result = MessageBoxResult.OK;     Close(); }
    }
}
