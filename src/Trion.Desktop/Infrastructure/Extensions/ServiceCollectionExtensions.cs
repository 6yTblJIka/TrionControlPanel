using Microsoft.Extensions.DependencyInjection;
using Trion.Core.Services.Emulator;
using Trion.Desktop.Services;
using Trion.Desktop.ViewModels;
using Trion.Desktop.Views;

namespace Trion.Desktop.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection RegisterViewModels(this IServiceCollection services)
    {
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddSingleton<ServerControlViewModel>();
        services.AddTransient<InstallerViewModel>();
        services.AddTransient<SinglePlayerViewModel>();
        services.AddTransient<DatabaseEditorViewModel>();
        services.AddTransient<DdnsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<LogsViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<SupportersViewModel>();
        services.AddTransient<BugReportViewModel>();
        services.AddTransient<NewsViewModel>();
        return services;
    }

    public static IServiceCollection RegisterGameClientServices(this IServiceCollection services)
    {
        services.AddSingleton<IGameClientService, GameClientService>();
        services.AddTransient<ClientDataExtractor>();
        return services;
    }

    public static IServiceCollection RegisterTrayService(this IServiceCollection services)
    {
        services.AddSingleton<ITrayService, TrayService>();
        return services;
    }

    public static IServiceCollection RegisterViews(this IServiceCollection services)
    {
        services.AddSingleton<MainWindow>();
        services.AddTransient<DashboardView>();
        services.AddTransient<ServerControlView>();
        services.AddTransient<InstallerView>();
        services.AddTransient<SinglePlayerView>();
        services.AddTransient<DatabaseEditorView>();
        services.AddTransient<DdnsView>();
        services.AddTransient<SettingsView>();
        services.AddTransient<LogsView>();
        services.AddTransient<LoginWindow>();
        services.AddTransient<SupportersView>();
        services.AddTransient<BugReportWindow>();
        services.AddTransient<NewsView>();
        return services;
    }
}
