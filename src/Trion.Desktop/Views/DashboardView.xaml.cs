using System.Windows.Controls;
using System.Windows.Input;
using Trion.Desktop.ViewModels;

namespace Trion.Desktop.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void DonationGoal_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is DashboardViewModel vm && vm.OpenDonationGoalCommand.CanExecute(null))
            vm.OpenDonationGoalCommand.Execute(null);
    }
}
