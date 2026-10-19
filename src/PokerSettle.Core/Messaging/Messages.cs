namespace PokerSettle.Core.Messaging;

[Flags]
public enum DataArea
{
    None = 0,
    Players = 1,
    Games = 2,
    /// <summary>Finalized results changed (finalize, reopen, delete, restore): the board must recompute.</summary>
    Results = 4,
    Payments = 8,
    All = Players | Games | Results | Payments,
}

/// <summary>Sent (WeakReferenceMessenger) after any write, so screens refresh only what changed.</summary>
public sealed record DataChangedMessage(DataArea Area, long? GameId = null)
{
    public bool Affects(DataArea area) => (Area & area) != 0;
}

/// <summary>A Preferences-backed setting changed; key names are in AppSettings.</summary>
public sealed record SettingsChangedMessage(string Key);
