using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokerSettle.Core.Backup;
using PokerSettle.Core.Services;

namespace PokerSettle.Core.ViewModels.Settings;

/// <summary>
/// Shown once after startup when the database couldn't be used: it was moved aside (never deleted) and a
/// fresh one started. Offers to import a backup.
/// </summary>
public sealed partial class RecoveryViewModel(CoreServices services, BackupCoordinator backup) : ViewModelBase(services)
{
    [ObservableProperty] public partial string Explanation { get; set; } = "";
    [ObservableProperty] public partial string? MovedTo { get; set; }
    [ObservableProperty] public partial bool HasMovedFile { get; set; }
    [ObservableProperty] public partial string? Reason { get; set; }

    protected override Task OnAppearingCoreAsync()
    {
        var recovery = Data.Recovery;
        if (recovery is null)
        {
            Explanation = "Your data is fine.";
            return Task.CompletedTask;
        }
        Explanation = recovery.InMemory
            ? "Your data file couldn't be opened or moved, so this session is running without saving. Restart the app; if this keeps happening, export what you need and reinstall."
            : "Your data file couldn't be opened, so a fresh, empty one was started. The old file was kept, not deleted. If you have a backup, import it now.";
        Reason = recovery.Reason;
        MovedTo = recovery.MovedTo;
        HasMovedFile = recovery.MovedTo is not null;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ImportAsync() => RunSafeAsync(async () =>
    {
        if (await backup.ImportAsync()) await Navigation.GoToAsync(Routes.Tab(Routes.Home));
    });

    [RelayCommand]
    private Task ContinueAsync() => RunSafeAsync(async () =>
    {
        Data.ClearRecovery();
        await Navigation.GoToAsync(Routes.Tab(Routes.Home));
    });
}
