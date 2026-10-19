using PokerSettle.App.Controls;
using PokerSettle.Core.ViewModels.Games;

namespace PokerSettle.App.Pages;

public partial class NewGamePage : BasePage
{
    public NewGamePage(NewGameViewModel viewModel) : base(viewModel)
    {
        InitializeComponent();
        FocusChain.Link(TitleEntry, BuyInEntry, LocationEntry, Picker.Search);
    }
}
