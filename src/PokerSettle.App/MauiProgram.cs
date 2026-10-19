using CommunityToolkit.Maui;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using PokerSettle.App.Pages;
using PokerSettle.App.Services;
using PokerSettle.Core;
using PokerSettle.Core.Data;
using PokerSettle.Core.Diagnostics;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Services;

namespace PokerSettle.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // The debug-logging flag lives in Preferences so it's known before the database opens.
        var preferences = new MauiPreferencesStore();
        var logSwitch = new LogSwitch(preferences.GetBool("debug_logging", false));
        var logWriter = new JsonLogWriter(new LogFileStore(Path.Combine(FileSystem.AppDataDirectory, "logs")));

        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);
        builder.Logging.AddProvider(new JsonFileLoggerProvider(logSwitch, logWriter));
#if DEBUG
        builder.Logging.AddDebug();
#endif

        var services = builder.Services;
        services.AddSingleton<IPreferencesStore>(preferences);
        services.AddSingleton(logSwitch);
        services.AddSingleton(logWriter);
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        services.AddSingleton(new CrashMarker(FileSystem.AppDataDirectory));
        services.AddSingleton(new DatabaseOptions
        {
            DirectoryPath = FileSystem.AppDataDirectory,
#if DEBUG
            StartWithDemo = preferences.GetBool("use_demo_database", false),
#endif
        });

        services.AddSingleton<INavigationService, MauiNavigationService>();
        services.AddSingleton<IDialogService, MauiDialogService>();
        services.AddSingleton<IClipboardService, MauiClipboardService>();
        services.AddSingleton<IShareService, MauiShareService>();
        services.AddSingleton<IFilePickerService, MauiFilePickerService>();
        services.AddSingleton<IFileSaverService, MauiFileSaverService>();
        services.AddSingleton<IHapticsService, MauiHapticsService>();
        services.AddSingleton<IScreenService, MauiScreenService>();
        services.AddSingleton<IThemeService, MauiThemeService>();
        services.AddSingleton<ILauncherService, MauiLauncherService>();
        services.AddSingleton<IAppEnvironment, MauiAppEnvironment>();

        services.AddPokerSettleCore();
        services.AddSingleton<AppShell>();
        services.AddPages();

        return builder.Build();
    }
}
