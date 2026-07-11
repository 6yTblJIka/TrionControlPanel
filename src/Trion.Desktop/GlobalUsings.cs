// -- WPF vs WinForms type disambiguation ---------------------------------------
// UseWindowsForms=true (needed for the system-tray NotifyIcon) pulls in
// System.Windows.Forms and System.Drawing globally, which conflicts with
// several WPF / System.Windows.Media types.
// These global aliases restore the correct WPF types project-wide so every
// other file compiles unchanged.

global using Application          = System.Windows.Application;
global using Binding              = System.Windows.Data.Binding;
global using Brush                = System.Windows.Media.Brush;
global using Brushes              = System.Windows.Media.Brushes;
global using Button               = System.Windows.Controls.Button;
global using Color                = System.Windows.Media.Color;
global using ColorConverter       = System.Windows.Media.ColorConverter;
global using Key                  = System.Windows.Input.Key;
global using KeyEventArgs         = System.Windows.Input.KeyEventArgs;
global using MessageBox           = System.Windows.MessageBox;
global using MessageBoxButton     = System.Windows.MessageBoxButton;
global using MessageBoxImage      = System.Windows.MessageBoxImage;
global using MessageBoxResult     = System.Windows.MessageBoxResult;
global using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
global using MouseButtonState     = System.Windows.Input.MouseButtonState;
global using Pen                  = System.Windows.Media.Pen;
global using Point                = System.Windows.Point;
global using UserControl          = System.Windows.Controls.UserControl;
