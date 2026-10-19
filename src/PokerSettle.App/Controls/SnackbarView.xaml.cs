using System.ComponentModel;
using PokerSettle.Core.Services;

namespace PokerSettle.App.Controls;

/// <summary>
/// The in-app snackbar (not an OS notification): bound to the shared SnackbarService, so every page shows
/// the same message and Undo works identically on Android and Windows. Screen readers announce messages.
/// </summary>
public partial class SnackbarView : ContentView
{
    private readonly SnackbarService? _service;

    public SnackbarView()
    {
        InitializeComponent();
        _service = ServiceHelper.TryGet<SnackbarService>();
        BindingContext = _service;
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (_service is null) return;
        if (Handler is null) _service.PropertyChanged -= OnServiceChanged;
        else _service.PropertyChanged += OnServiceChanged;
    }

    private void OnServiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SnackbarService.IsVisible) || _service is not { IsVisible: true } service) return;
        // Only the snackbar on the visible page announces, to avoid repeats.
        if (!IsVisibleOnScreen()) return;
        Card.Opacity = 0;
        Card.TranslationY = 12;
        _ = Card.FadeToAsync(1, 150);
        _ = Card.TranslateToAsync(0, 0, 150, Easing.CubicOut);
        if (service.Message is { } message) SemanticScreenReader.Announce(message);
    }

    private bool IsVisibleOnScreen()
    {
        Element? current = this;
        while (current is not null and not Page) current = current.Parent;
        return current is Page page && Shell.Current?.CurrentPage == page;
    }
}
