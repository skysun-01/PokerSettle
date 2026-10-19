using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokerSettle.Core.Models;
using PokerSettle.Core.Money;
using PokerSettle.Core.Players;
using PokerSettle.Core.Settlement;

namespace PokerSettle.Core.ViewModels.Games;

/// <summary>One player's result line (review and game detail).</summary>
public sealed class ResultRow
{
    public required long PlayerId { get; init; }
    public required string Name { get; init; }
    public required string Initials { get; init; }
    public required string Color { get; init; }
    public required string Details { get; init; }
    public required string NetText { get; init; }
    public required int NetSign { get; init; }
    public string? Note { get; init; }
    public bool HasNote => Note is not null;
    public string Description => $"{Name}: {NetText}. {Details}" + (Note is null ? "" : $". {Note}");
}

/// <summary>A payment in the review (read-only).</summary>
public sealed class PaymentRow
{
    public required string FromName { get; init; }
    public required string ToName { get; init; }
    public required string AmountText { get; init; }
    public string Text => $"{FromName} pays {ToName} {AmountText}";
}

/// <summary>A stored payment that can be marked paid.</summary>
public sealed partial class TransferItem : ObservableObject
{
    public required long Id { get; init; }
    public required string FromName { get; init; }
    public required string ToName { get; init; }
    public required string AmountText { get; init; }
    public string? UpiId { get; init; }
    public required IRelayCommand<TransferItem> TogglePaidCommand { get; init; }
    public required IRelayCommand<TransferItem> CopyUpiCommand { get; init; }
    public required IRelayCommand<TransferItem> PayUpiCommand { get; init; }
    internal long Amount { get; init; }

    public string Text => $"{FromName} pays {ToName} {AmountText}";
    public bool HasUpi => !string.IsNullOrWhiteSpace(UpiId);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaidButtonText), nameof(PaidDescription))]
    public partial bool IsPaid { get; set; }

    [ObservableProperty]
    public partial string? PaidText { get; set; }

    [ObservableProperty]
    public partial bool CanPayViaUpi { get; set; }

    public string PaidButtonText => IsPaid ? "Paid ✓" : "Mark paid";
    public string PaidDescription => IsPaid ? $"{Text}, paid. Tap to mark as not paid." : $"Mark {Text} as paid";
}

internal static class GameRowFactory
{
    public static List<PlayerLine> Lines(GameSnapshot game) =>
        game.Entries.Select(e => new PlayerLine(e.PlayerId, e.Entry.Position, game.BuyInTotal(e.PlayerId),
            game.BuyInCount(e.PlayerId), e.Entry.CashOut, game.AdjustmentTotal(e.PlayerId))).ToList();

    public static List<ResultRow> Results(GameSnapshot game, IReadOnlyList<PlayerResult> results, GameMoney money, bool showSettled)
    {
        var rows = new List<ResultRow>();
        foreach (var r in results.OrderByDescending(r => r.Net).ThenBy(r => game.FindPlayer(r.PlayerId)?.Name, StringComparer.OrdinalIgnoreCase))
        {
            var player = game.FindPlayer(r.PlayerId)!;
            var count = r.BuyInCount == 1 ? "1 buy-in" : $"{r.BuyInCount} buy-ins";
            var details = $"{count} {money.Units(r.BuyInTotal)} · out {money.Units(r.CashOut)}";
            if (r.Adjustment != 0) details += $" · adj. {money.SignedUnits(r.Adjustment)}";
            string? note = null;
            if (showSettled && r.SettledNet != r.Net)
                note = $"Settles as {money.Formatter.FormatSigned(r.SettledNet)} after rounding";
            rows.Add(new ResultRow
            {
                PlayerId = r.PlayerId,
                Name = player.Name,
                Initials = PlayerNames.Initials(player.Name),
                Color = player.Color,
                Details = details,
                NetText = money.Formatter.FormatSigned(r.Net),
                NetSign = Math.Sign(r.Net),
                Note = note,
            });
        }
        return rows;
    }

    /// <summary>Stored results for a finalized game (nets from the entries).</summary>
    public static List<ResultRow> StoredResults(GameSnapshot game, GameMoney money)
    {
        var results = game.Entries.Select(e => new PlayerResult(e.PlayerId, e.Entry.Position, game.BuyInTotal(e.PlayerId),
            game.BuyInCount(e.PlayerId), e.Entry.CashOut ?? 0, game.AdjustmentTotal(e.PlayerId),
            (e.Entry.CashOut ?? 0) - game.BuyInTotal(e.PlayerId) + game.AdjustmentTotal(e.PlayerId),
            e.Entry.Net ?? 0, e.Entry.Net ?? 0)).ToList();
        return Results(game, results, money, showSettled: false);
    }

    public static string AdjustmentSummary(GameSnapshot game, IReadOnlyList<AdjustmentShare> shares, AdjustmentKind kind, GameMoney money)
    {
        var total = shares.Sum(s => s.Amount);
        if (kind == AdjustmentKind.AssignedDifference && shares.Count == 1)
            return $"Count difference of {money.Units(Math.Abs(total))} assigned to {game.FindPlayer(shares[0].PlayerId)?.Name}: {money.SignedUnits(shares[0].Amount)}.";
        var min = shares.Min(s => s.Amount);
        var max = shares.Max(s => s.Amount);
        var each = min == max ? money.SignedUnits(min) : $"{money.SignedUnits(min)} to {money.SignedUnits(max)}";
        return $"Count difference of {money.Units(Math.Abs(total))} split equally: {each} each.";
    }
}
