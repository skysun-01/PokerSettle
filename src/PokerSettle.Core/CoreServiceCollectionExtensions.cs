using Microsoft.Extensions.DependencyInjection;
using PokerSettle.Core.Data;
using PokerSettle.Core.Diagnostics;
using PokerSettle.Core.Services;
using PokerSettle.Core.ViewModels;
using PokerSettle.Core.ViewModels.Settings;

namespace PokerSettle.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers Core services and ViewModels. The host must also register: DatabaseOptions, CrashMarker,
    /// LogSwitch, JsonLogWriter, IMessenger and the platform services (navigation, dialogs, …).
    /// Tab ViewModels are singletons (their pages live for the whole session); pushed pages get fresh ones.
    /// </summary>
    public static IServiceCollection AddPokerSettleCore(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<DataStore>();
        services.AddSingleton<AppSettings>();
        services.AddSingleton<SnackbarService>();
        services.AddSingleton<AppState>();
        services.AddSingleton<CoreServices>();
        services.AddSingleton<KeepAwakeCoordinator>();
        services.AddSingleton<DebugReportBuilder>();

        // Tabs
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<GamesViewModel>();
        services.AddSingleton<BoardViewModel>();
        services.AddSingleton<PlayersViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Pushed pages
        services.AddTransient<LogsViewModel>();
        return services;
    }
}
