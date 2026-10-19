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

        // Pushed pages: a fresh page and ViewModel per navigation.
        services.AddTransient<LogsPage>();
        services.AddTransient<PlayerEditPage>();
        services.AddTransient<NewGamePage>();
        services.AddTransient<LiveGamePage>();
        services.AddTransient<AddPlayersPage>();
        services.AddTransient<CashOutPage>();
        services.AddTransient<ReviewPage>();
        services.AddTransient<GameDetailPage>();
        return services;
    }

    public static void RegisterRoutes()
    {
        Routing.RegisterRoute(Routes.Logs, typeof(LogsPage));
        Routing.RegisterRoute(Routes.PlayerEdit, typeof(PlayerEditPage));
        Routing.RegisterRoute(Routes.NewGame, typeof(NewGamePage));
        Routing.RegisterRoute(Routes.LiveGame, typeof(LiveGamePage));
        Routing.RegisterRoute(Routes.AddPlayers, typeof(AddPlayersPage));
        Routing.RegisterRoute(Routes.CashOut, typeof(CashOutPage));
        Routing.RegisterRoute(Routes.Review, typeof(ReviewPage));
        Routing.RegisterRoute(Routes.GameDetail, typeof(GameDetailPage));
    }
}
