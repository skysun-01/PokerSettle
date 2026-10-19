namespace PokerSettle.App.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
        // Exceptions that reach the WinUI dispatcher: log and flush before the process ends.
        UnhandledException += (_, e) => CrashReporter.Report(e.Exception, "WinUI");
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
