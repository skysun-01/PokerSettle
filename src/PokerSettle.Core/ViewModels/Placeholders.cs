using PokerSettle.Core.Services;

namespace PokerSettle.Core.ViewModels;

// Home arrives in phase 4.

public sealed class HomeViewModel(CoreServices services) : ViewModelBase(services)
{
    public string Title => "Poker Settle";
}
