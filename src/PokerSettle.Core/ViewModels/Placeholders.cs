using PokerSettle.Core.Services;

namespace PokerSettle.Core.ViewModels;

// Phase 1 shell: tab ViewModels that later phases fill in.

public sealed class HomeViewModel(CoreServices services) : ViewModelBase(services)
{
    public string Title => "Poker Settle";
}

public sealed class GamesViewModel(CoreServices services) : ViewModelBase(services)
{
    public string Title => "Games";
}

public sealed class BoardViewModel(CoreServices services) : ViewModelBase(services)
{
    public string Title => "Board";
}

public sealed class PlayersViewModel(CoreServices services) : ViewModelBase(services)
{
    public string Title => "Players";
}
