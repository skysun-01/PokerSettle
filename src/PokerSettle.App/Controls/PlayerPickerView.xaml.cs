namespace PokerSettle.App.Controls;

/// <summary>Player picker used by New game and Add players; bound to a PlayerPickerViewModel.</summary>
public partial class PlayerPickerView : ContentView
{
    public PlayerPickerView()
    {
        InitializeComponent();
    }

    public Entry Search => SearchEntry;
}
