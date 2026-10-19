using Microsoft.Extensions.Logging;
using PokerSettle.Core.Logging;

namespace PokerSettle.App;

/// <summary>Access to the DI container for code MAUI creates itself (controls in templates, crash hooks).</summary>
public static class ServiceHelper
{
    public static IServiceProvider? Services => IPlatformApplication.Current?.Services;

    public static T Get<T>() where T : notnull =>
        (Services ?? throw new InvalidOperationException("Services are not ready yet.")).GetRequiredService<T>();

    public static T? TryGet<T>() where T : class => Services?.GetService<T>();
}

/// <summary>
/// Last-chance logging for exceptions nobody caught: writes the entry and flushes synchronously,
/// because the process is about to end. The crash marker stays, so the next launch can ask about it.
/// </summary>
public static class CrashReporter
{
    public static void Report(Exception? exception, string source)
    {
        if (exception is null) return;
        try
        {
            var services = ServiceHelper.Services;
            if (services is null) return;
            services.GetRequiredService<ILoggerFactory>().CreateLogger("PokerSettle.Crash").UnhandledException(exception, source);
            services.GetRequiredService<JsonLogWriter>().Flush();
        }
        catch
        {
            // Never throw from a crash handler.
        }
    }
}
