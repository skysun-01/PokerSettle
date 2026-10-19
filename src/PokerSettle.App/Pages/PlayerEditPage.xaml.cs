using PokerSettle.App.Controls;
using PokerSettle.Core.ViewModels.Players;

namespace PokerSettle.App.Pages;

public partial class PlayerEditPage : BasePage
{
    public PlayerEditPage(PlayerEditViewModel viewModel) : base(viewModel)
    {
        InitializeComponent();
        FocusChain.Link(NameEntry, NicknameEntry, PhoneEntry, UpiEntry);
        // Enter on the last field saves.
        UpiEntry.ReturnType = ReturnType.Done;
        UpiEntry.ReturnCommand = viewModel.SaveCommand;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (string.IsNullOrEmpty(NameEntry.Text)) NameEntry.Focus();
    }
}
