namespace PokerSettle.Core.ViewModels;

/// <summary>Shell routes. Tabs are absolute ("//home"); pages are pushed onto the current tab's stack.</summary>
public static class Routes
{
    public const string Home = "home";
    public const string Games = "games";
    public const string Board = "board";
    public const string Players = "players";
    public const string Settings = "settings";

    public const string NewGame = "newgame";
    public const string LiveGame = "livegame";
    public const string AddPlayers = "addplayers";
    public const string CashOut = "cashout";
    public const string Review = "review";
    public const string GameDetail = "gamedetail";
    public const string PlayerEdit = "playeredit";
    public const string PlayerDetail = "playerdetail";
    public const string Logs = "logs";
    public const string Recovery = "recovery";

    public const string GameId = "gameId";
    public const string PlayerId = "playerId";
    public const string FromGameId = "fromGameId";
    public const string Resolution = "resolution";
    public const string AssignedPlayerId = "assignedPlayerId";

    public static string Tab(string tab) => "//" + tab;
}
