using PokerSettle.App.Controls;
using PokerSettle.Core.ViewModels.Games;

namespace PokerSettle.App.Pages;

public partial class CashOutPage : BasePage
{
    public CashOutPage(CashOutViewModel viewModel) : base(viewModel)
    {
        InitializeComponent();
    }

    /// <summary>Next on one amount moves to the next player's amount; on the last one, continue.</summary>
    private void OnAmountCompleted(object? sender, EventArgs e)
    {
        if (sender is not Entry entry) return;
        if (!FocusChain.FocusFollowing(RowsHost, entry) && BindingContext is CashOutViewModel vm)
            vm.ContinueCommand.Execute(null);
    }
}
