using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using PokerSettle.Core.Data;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Money;
using PokerSettle.Core.Services;

namespace PokerSettle.Core.ViewModels;

/// <summary>Pages forward Shell query parameters to ViewModels that implement this.</summary>
public interface IQueryReceiver
{
    void ApplyQuery(IDictionary<string, object> query);
}

/// <summary>
/// Base for all ViewModels. Every command body goes through <see cref="RunSafeAsync"/>, which logs
/// the exception and shows "Something went wrong. Your data is safe." instead of crashing.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    protected ViewModelBase(CoreServices services)
    {
        Services = services;
        Log = services.LoggerFactory.CreateLogger(GetType().FullName ?? GetType().Name);
    }

    protected CoreServices Services { get; }
    protected DataStore Data => Services.Data;
    protected AppSettings Settings => Services.Settings;
    protected MoneyFormatter Money => Services.Settings.Formatter;
    protected INavigationService Navigation => Services.Navigation;
    protected IDialogService Dialogs => Services.Dialogs;
    protected SnackbarService Snackbar => Services.Snackbar;
    protected IMessenger Messenger => Services.Messenger;
    protected IClock Clock => Services.Clock;
    protected ILogger Log { get; }

    /// <summary>True while the page is on screen.</summary>
    protected bool IsActive { get; private set; }

    /// <summary>Set when data this page shows changed while it was hidden; reload on next appearance.</summary>
    protected bool IsStale { get; set; } = true;

    public async Task OnAppearingAsync()
    {
        IsActive = true;
        await RunSafeAsync(OnAppearingCoreAsync, "Appearing");
    }

    public void OnDisappearing()
    {
        IsActive = false;
        try
        {
            OnDisappearingCore();
        }
        catch (Exception ex)
        {
            Log.CommandFailed(ex, GetType().Name + ".Disappearing");
        }
    }

    protected virtual Task OnAppearingCoreAsync() => Task.CompletedTask;

    protected virtual void OnDisappearingCore()
    {
    }

    /// <summary>Runs a command body; exceptions are logged and shown as a friendly message.</summary>
    protected async Task RunSafeAsync(Func<Task> work, [CallerMemberName] string command = "")
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.CommandFailed(ex, $"{GetType().Name}.{command}");
            Snackbar.ShowError();
        }
    }

    protected void RunSafe(Action work, [CallerMemberName] string command = "")
    {
        try
        {
            work();
        }
        catch (Exception ex)
        {
            Log.CommandFailed(ex, $"{GetType().Name}.{command}");
            Snackbar.ShowError();
        }
    }

    protected static long? QueryLong(IDictionary<string, object> query, string key) =>
        query.TryGetValue(key, out var value) ? value switch
        {
            long l => l,
            int i => i,
            string s when long.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        } : null;
}
