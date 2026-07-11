using System.ComponentModel;
using System.Windows;
using Trion.Desktop.ViewModels;

namespace Trion.Desktop.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        vm.RequestClose += () => Dispatcher.InvokeAsync(Close);

        // Keep the PasswordBox in sync (it doesn't support data-binding)
        PasswordInput.PasswordChanged += (_, _) => vm.Password = PasswordInput.Password;

        // When switching to 2FA mode, clear the PasswordBox and focus the code input
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LoginViewModel.IsTwoFactorMode)) return;
        if (!_vm.IsTwoFactorMode) return;

        // Clear the PasswordBox value (the VM already cleared vm.Password)
        PasswordInput.Password = string.Empty;

        // Focus the code input on the next render pass
        Dispatcher.InvokeAsync(() => CodeInput?.Focus(),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
