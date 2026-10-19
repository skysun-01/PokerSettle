using PokerSettle.Core.ViewModels.Players;

namespace PokerSettle.App.Pages;

public partial class PlayerDetailPage : BasePage
{
    public PlayerDetailPage(PlayerDetailViewModel viewModel) : base(viewModel)
    {
        InitializeComponent();
    }
}
