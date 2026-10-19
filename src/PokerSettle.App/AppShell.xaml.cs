using PokerSettle.App.Pages;

namespace PokerSettle.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        PageRegistry.RegisterRoutes();
    }
}
