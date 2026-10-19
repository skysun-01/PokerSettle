using Android.App;
using Android.Runtime;

namespace PokerSettle.App;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        // Managed exceptions that reach the Java side: log them (and flush) before Android ends the process.
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => CrashReporter.Report(e.Exception, "Android");
        base.OnCreate();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
