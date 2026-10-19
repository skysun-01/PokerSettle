using PokerSettle.Core.ViewModels;

namespace PokerSettle.App.Pages;

/// <summary>DI registrations and Shell routes for every page.</summary>
public static class PageRegistry
{
    public static IServiceCollection AddPages(this IServiceCollection services)
    {
        // Tab pages: Shell creates each once, on demand.
        services.AddTransient<HomePage>();
        services.AddTransient<GamesPage>();
        services.AddTransient<BoardPage>();
        services.AddTransient<PlayersPage>();
        services.AddTransient<SettingsPage>();

        // Pushed pages
        services.AddTransient<LogsPage>();
        return services;
    }

    public static void RegisterRoutes()
    {
        Routing.RegisterRoute(Routes.Logs, typeof(LogsPage));
    }
}
