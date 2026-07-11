using System.Windows;
using Trion.Desktop.ViewModels;

namespace Trion.Desktop.Views;

public partial class BugReportWindow : Window
{
    public BugReportWindow(BugReportViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        vm.RequestClose += () => Dispatcher.InvokeAsync(Close);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
