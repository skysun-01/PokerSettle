using PokerSettle.Core.ViewModels.Games;

namespace PokerSettle.App.Pages;

public partial class LiveGamePage : BasePage
{
    public LiveGamePage(LiveGameViewModel viewModel) : base(viewModel)
    {
        InitializeComponent();
    }

    /// <summary>Wide windows show the player cards in two or three columns.</summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        var span = width switch
        {
            >= 1300 => 3,
            >= 820 => 2,
            _ => 1,
        };
        if (CardsLayout.Span != span) CardsLayout.Span = span;
    }
}
