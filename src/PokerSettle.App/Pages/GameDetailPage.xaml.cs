using PokerSettle.Core.ViewModels.Games;

namespace PokerSettle.App.Pages;

public partial class GameDetailPage : BasePage
{
    public GameDetailPage(GameDetailViewModel viewModel) : base(viewModel)
    {
        InitializeComponent();
    }
}
