using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PokerSettle.Core.Services;

/// <summary>
/// State for the in-app snackbar (a plain control on every page, not an OS notification), so Undo
/// behaves the same on Android and Windows. Showing a new message replaces the previous one; its
/// Undo is then no longer offered.
/// </summary>
public sealed partial class SnackbarService : ObservableObject
{
    public const string SafeErrorMessage = "Something went wrong. Your data is safe.";
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan UndoDuration = TimeSpan.FromSeconds(7);

    private CancellationTokenSource? _timer;
    private Func<Task>? _action;

    [ObservableProperty]
    public partial string? Message { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAction))]
    public partial string? ActionText { get; private set; }

    [ObservableProperty]
    public partial bool IsVisible { get; private set; }

    [ObservableProperty]
    public partial bool IsError { get; private set; }

    public bool HasAction => ActionText is not null;

    public void Show(string message, string? actionText = null, Func<Task>? action = null, TimeSpan? duration = null) =>
        Present(message, actionText, action, duration ?? (action is null ? DefaultDuration : UndoDuration), isError: false);

    public void ShowUndo(string message, Func<Task> undo) => Show(message, "Undo", undo);

    public void ShowError(string message = SafeErrorMessage) =>
        Present(message, null, null, TimeSpan.FromSeconds(6), isError: true);

    [RelayCommand]
    private async Task RunActionAsync()
    {
        var action = _action;
        Hide();
        if (action is null) return;
        try
        {
            await action();
        }
        catch (Exception)
        {
            ShowError();
        }
    }

    [RelayCommand]
    public void Dismiss() => Hide();

    private void Present(string message, string? actionText, Func<Task>? action, TimeSpan duration, bool isError)
    {
        _timer?.Cancel();
        _action = action;
        Message = message;
        ActionText = action is null ? null : actionText;
        IsError = isError;
        IsVisible = true;

        var timer = new CancellationTokenSource();
        _timer = timer;
        _ = HideLaterAsync(duration, timer);
    }

    private async Task HideLaterAsync(TimeSpan duration, CancellationTokenSource timer)
    {
        try
        {
            await Task.Delay(duration, timer.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        if (ReferenceEquals(_timer, timer)) Hide();
    }

    private void Hide()
    {
        _timer?.Cancel();
        _timer = null;
        _action = null;
        IsVisible = false;
    }
}
