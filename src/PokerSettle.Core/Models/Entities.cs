namespace PokerSettle.Core.Models;

// Money is always long minor units (paise). "Game units" are paise, or whole chips when a game is in chip mode.

public sealed record Player
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public string? Nickname { get; init; }
    public string? Phone { get; init; }
    public string? UpiId { get; init; }
    public string? Notes { get; init; }
    /// <summary>Avatar color as #RRGGBB.</summary>
    public required string Color { get; init; }
    public bool IsArchived { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Nickname) ? Name : $"{Name} ({Nickname})";
}

/// <summary>Chips per money: <c>Chips</c> chips are worth <c>Money</c> paise (e.g. 1,000 chips = 50,000 paise).</summary>
public readonly record struct ChipRate(long Chips, long Money)
{
    public bool IsValid => Chips > 0 && Money > 0;
}

public sealed record GameSettings
{
    /// <summary>Default buy-in in game units.</summary>
    public long DefaultBuyIn { get; init; }
    public bool ChipMode { get; init; }
    public ChipRate? ChipRate { get; init; }
    public SettlementMode SettlementMode { get; init; }
    public long? BankerId { get; init; }
    public RoundingMode Rounding { get; init; }
    /// <summary>Everyone (including late players) starts with one default buy-in.</summary>
    public bool StartWithBuyIn { get; init; } = true;
}

public sealed record Game
{
    public long Id { get; init; }
    public required string Title { get; init; }
    public DateOnly Date { get; init; }
    public string? Location { get; init; }
    public string? Notes { get; init; }
    public GameStatus Status { get; init; }
    public required GameSettings Settings { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? FinalizedAt { get; init; }

    public bool IsFinalized => Status == GameStatus.Finalized;
}

public sealed record Entry
{
    public long GameId { get; init; }
    public long PlayerId { get; init; }
    /// <summary>Seat order: the order players were added to the game.</summary>
    public int Position { get; init; }
    /// <summary>Final amount in game units; null until entered.</summary>
    public long? CashOut { get; init; }
    /// <summary>Result in paise; written only at finalization.</summary>
    public long? Net { get; init; }
}

public sealed record BuyIn(long Id, long GameId, long PlayerId, long Amount, DateTimeOffset At);

/// <summary>Signed amount in game units added to one player's result.</summary>
public sealed record Adjustment(long Id, long GameId, AdjustmentKind Kind, long? PlayerId, long Amount);

/// <summary>A settlement payment in paise.</summary>
public sealed record Transfer(long Id, long GameId, long FromId, long ToId, long Amount, bool IsPaid, DateTimeOffset? PaidAt);

/// <summary>An entry together with its player, as loaded for one game.</summary>
public sealed record GameEntry(Entry Entry, Player Player)
{
    public long PlayerId => Entry.PlayerId;
}

/// <summary>Everything stored for one game. Also used to restore a deleted game (Undo).</summary>
public sealed record GameSnapshot(
    Game Game,
    IReadOnlyList<GameEntry> Entries,
    IReadOnlyList<BuyIn> BuyIns,
    IReadOnlyList<Adjustment> Adjustments,
    IReadOnlyList<Transfer> Transfers)
{
    public long BuyInTotal(long playerId)
    {
        long total = 0;
        foreach (var b in BuyIns)
        {
            if (b.PlayerId == playerId) total += b.Amount;
        }
        return total;
    }

    public int BuyInCount(long playerId) => BuyIns.Count(b => b.PlayerId == playerId);

    public long AdjustmentTotal(long playerId)
    {
        long total = 0;
        foreach (var a in Adjustments)
        {
            if (a.PlayerId == playerId) total += a.Amount;
        }
        return total;
    }

    public long Pot => BuyIns.Sum(b => b.Amount);

    public Player? FindPlayer(long playerId) => Entries.FirstOrDefault(e => e.PlayerId == playerId)?.Player;
}
