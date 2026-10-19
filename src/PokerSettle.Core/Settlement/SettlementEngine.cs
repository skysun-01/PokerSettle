using PokerSettle.Core.Models;
using PokerSettle.Core.Money;

namespace PokerSettle.Core.Settlement;

/// <summary>One player's ledger for a game, in game units (paise, or chips in chip mode).</summary>
public sealed record PlayerLine(long PlayerId, int Position, long BuyInTotal, int BuyInCount, long? CashOut, long Adjustment = 0)
{
    /// <summary>cash-out − buy-ins + adjustment, in game units. Requires a cash-out.</summary>
    public long NetUnits => (CashOut ?? 0) - BuyInTotal + Adjustment;
}

public sealed record SettlementOptions(
    bool ChipMode,
    ChipRate? ChipRate,
    SettlementMode Mode,
    long? BankerId,
    RoundingMode Rounding)
{
    public static SettlementOptions From(GameSettings settings) =>
        new(settings.ChipMode, settings.ChipRate, settings.SettlementMode, settings.BankerId, settings.Rounding);
}

/// <param name="NetUnits">Result in game units.</param>
/// <param name="Net">Result in paise (chip results converted with largest remainder). Stored on the entry.</param>
/// <param name="SettledNet">Net rounded for payments (largest remainder); equals Net when rounding is off.</param>
public sealed record PlayerResult(
    long PlayerId,
    int Position,
    long BuyInTotal,
    int BuyInCount,
    long CashOut,
    long Adjustment,
    long NetUnits,
    long Net,
    long SettledNet);

/// <summary>A payment in paise.</summary>
public sealed record Payment(long FromId, long ToId, long Amount);

public sealed record SettlementResult(IReadOnlyList<PlayerResult> Results, IReadOnlyList<Payment> Payments);

public enum IssueKind
{
    TooFewPlayers,
    MissingCashOut,
    NegativeCashOut,
    Unbalanced,
    MissingBanker,
    InvalidChipRate,
    /// <summary>Warning only: a player has no buy-ins.</summary>
    NoBuyIns,
}

public sealed record SettlementIssue(IssueKind Kind, long? PlayerId = null, long Amount = 0)
{
    public bool IsBlocking => Kind != IssueKind.NoBuyIns;
}

public sealed record AdjustmentShare(long PlayerId, long Amount);

/// <summary>
/// Pure settlement math. Nets are cash-out − buy-ins + adjustments and always sum to exactly zero.
/// Every output is deterministic: inputs are ordered by player id and ties break on the lower id.
/// </summary>
public static class SettlementEngine
{
    public static long RoundingUnit(RoundingMode rounding) => rounding switch
    {
        RoundingMode.Nearest1 => 100,
        RoundingMode.Nearest10 => 1_000,
        _ => 1,
    };

    /// <summary>Σ cash-outs − Σ buy-ins + Σ adjustments, in game units (missing cash-outs count as 0).</summary>
    public static long Imbalance(IEnumerable<PlayerLine> lines) => lines.Sum(l => l.NetUnits);

    public static IReadOnlyList<SettlementIssue> Validate(IReadOnlyList<PlayerLine> lines, SettlementOptions options)
    {
        var issues = new List<SettlementIssue>();
        if (lines.Count < 2) issues.Add(new SettlementIssue(IssueKind.TooFewPlayers));

        var missing = false;
        foreach (var line in lines.OrderBy(l => l.Position).ThenBy(l => l.PlayerId))
        {
            if (line.CashOut is null)
            {
                missing = true;
                issues.Add(new SettlementIssue(IssueKind.MissingCashOut, line.PlayerId));
            }
            else if (line.CashOut < 0)
            {
                missing = true;
                issues.Add(new SettlementIssue(IssueKind.NegativeCashOut, line.PlayerId));
            }
            if (line.BuyInCount == 0) issues.Add(new SettlementIssue(IssueKind.NoBuyIns, line.PlayerId));
        }

        if (options.ChipMode && options.ChipRate is not { IsValid: true })
            issues.Add(new SettlementIssue(IssueKind.InvalidChipRate));

        if (options.Mode == SettlementMode.Banker &&
            (options.BankerId is null || lines.All(l => l.PlayerId != options.BankerId)))
            issues.Add(new SettlementIssue(IssueKind.MissingBanker));

        if (!missing)
        {
            var imbalance = Imbalance(lines);
            if (imbalance != 0) issues.Add(new SettlementIssue(IssueKind.Unbalanced, Amount: imbalance));
        }
        return issues;
    }

    /// <summary>
    /// Adjustments (game units) that cancel the current imbalance. Split equally: near-equal shares in
    /// seat order, leftover units to the first seats. Assign: the whole amount to one player.
    /// </summary>
    public static IReadOnlyList<AdjustmentShare> ResolveDiscrepancy(
        IReadOnlyList<PlayerLine> lines, DiscrepancyResolution resolution, long? assignedPlayerId)
    {
        var imbalance = Imbalance(lines);
        if (imbalance == 0 || resolution == DiscrepancyResolution.None) return [];

        if (resolution == DiscrepancyResolution.AssignToPlayer)
        {
            if (assignedPlayerId is not { } id || lines.All(l => l.PlayerId != id))
                throw new ArgumentException("Choose a player in this game.", nameof(assignedPlayerId));
            return [new AdjustmentShare(id, -imbalance)];
        }

        var seats = lines.OrderBy(l => l.Position).ThenBy(l => l.PlayerId).ToList();
        var shares = Apportionment.SplitEvenly(-imbalance, seats.Count);
        var result = new List<AdjustmentShare>(seats.Count);
        for (var i = 0; i < seats.Count; i++)
        {
            if (shares[i] != 0) result.Add(new AdjustmentShare(seats[i].PlayerId, shares[i]));
        }
        return result;
    }

    public static IReadOnlyList<PlayerLine> ApplyAdjustments(IReadOnlyList<PlayerLine> lines, IEnumerable<AdjustmentShare> shares)
    {
        var extra = shares.GroupBy(s => s.PlayerId).ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));
        return lines
            .Select(l => extra.TryGetValue(l.PlayerId, out var add) ? l with { Adjustment = l.Adjustment + add } : l)
            .ToList();
    }

    /// <summary>
    /// Money nets (paise) for balanced lines, in the given order. Chip results are converted with
    /// largest remainder so the money nets still sum to exactly zero.
    /// </summary>
    public static long[] MoneyNets(IReadOnlyList<PlayerLine> lines, bool chipMode, ChipRate? rate)
    {
        var units = lines.Select(l => l.NetUnits).ToArray();
        if (units.Sum() != 0) throw new InvalidOperationException("Results must sum to zero.");
        if (!chipMode) return units;
        if (rate is not { IsValid: true } r) throw new InvalidOperationException("Chip mode needs a valid chip rate.");

        // Keep the caller's order but break remainder ties by player id.
        var order = Enumerable.Range(0, lines.Count).OrderBy(i => lines[i].PlayerId).ToArray();
        var numerators = order.Select(i => (Int128)units[i] * r.Money).ToArray();
        var converted = Apportionment.LargestRemainder(numerators, r.Chips, 1);
        var nets = new long[lines.Count];
        for (var k = 0; k < order.Length; k++) nets[order[k]] = converted[k];
        return nets;
    }

    /// <summary>Computes results and payments. Throws if <see cref="Validate"/> reports a blocking issue.</summary>
    public static SettlementResult Settle(IReadOnlyList<PlayerLine> lines, SettlementOptions options)
    {
        var blocking = Validate(lines, options).FirstOrDefault(i => i.IsBlocking);
        if (blocking is not null) throw new InvalidOperationException($"Can't settle: {blocking.Kind}.");

        var ordered = lines.OrderBy(l => l.PlayerId).ToList();
        var nets = MoneyNets(ordered, options.ChipMode, options.ChipRate);
        var settled = Apportionment.RoundToUnit(nets, RoundingUnit(options.Rounding));
        var ids = ordered.Select(l => l.PlayerId).ToArray();

        var payments = options.Mode == SettlementMode.Banker
            ? BankerPayments(ids, settled, options.BankerId!.Value)
            : FewestPayments(ids, settled);

        var results = new List<PlayerResult>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var l = ordered[i];
            results.Add(new PlayerResult(l.PlayerId, l.Position, l.BuyInTotal, l.BuyInCount, l.CashOut!.Value,
                l.Adjustment, l.NetUnits, nets[i], settled[i]));
        }
        return new SettlementResult(results, payments);
    }

    /// <summary>
    /// Fewest payments: settle exact opposite pairs first, otherwise the biggest loser pays the
    /// biggest winner. Every payment clears at least one player, so there are at most n−1 payments.
    /// </summary>
    public static IReadOnlyList<Payment> FewestPayments(IReadOnlyList<long> playerIds, IReadOnlyList<long> nets)
    {
        if (playerIds.Count != nets.Count) throw new ArgumentException("Ids and nets differ in length.");
        if (nets.Sum() != 0) throw new ArgumentException("Nets must sum to zero.", nameof(nets));

        var debtors = new List<Balance>();
        var creditors = new List<Balance>();
        for (var i = 0; i < nets.Count; i++)
        {
            if (nets[i] < 0) debtors.Add(new Balance(playerIds[i], -nets[i]));
            else if (nets[i] > 0) creditors.Add(new Balance(playerIds[i], nets[i]));
        }

        var payments = new List<Payment>();
        while (debtors.Count > 0 && creditors.Count > 0)
        {
            var (d, c) = FindExactPair(debtors, creditors);
            if (d >= 0)
            {
                payments.Add(new Payment(debtors[d].Id, creditors[c].Id, debtors[d].Amount));
                debtors.RemoveAt(d);
                creditors.RemoveAt(c);
                continue;
            }

            d = IndexOfLargest(debtors);
            c = IndexOfLargest(creditors);
            var amount = Math.Min(debtors[d].Amount, creditors[c].Amount);
            payments.Add(new Payment(debtors[d].Id, creditors[c].Id, amount));
            debtors[d] = debtors[d] with { Amount = debtors[d].Amount - amount };
            creditors[c] = creditors[c] with { Amount = creditors[c].Amount - amount };
            if (debtors[d].Amount == 0) debtors.RemoveAt(d);
            if (creditors[c].Amount == 0) creditors.RemoveAt(c);
        }
        return payments;
    }

    /// <summary>Banker: every loser pays the banker, then the banker pays every winner.</summary>
    public static IReadOnlyList<Payment> BankerPayments(IReadOnlyList<long> playerIds, IReadOnlyList<long> nets, long bankerId)
    {
        if (playerIds.Count != nets.Count) throw new ArgumentException("Ids and nets differ in length.");
        if (nets.Sum() != 0) throw new ArgumentException("Nets must sum to zero.", nameof(nets));
        if (!playerIds.Contains(bankerId)) throw new ArgumentException("The banker must be in the game.", nameof(bankerId));

        var others = Enumerable.Range(0, nets.Count).Where(i => playerIds[i] != bankerId).ToList();
        var payments = new List<Payment>();
        foreach (var i in others.Where(i => nets[i] < 0).OrderBy(i => nets[i]).ThenBy(i => playerIds[i]))
            payments.Add(new Payment(playerIds[i], bankerId, -nets[i]));
        foreach (var i in others.Where(i => nets[i] > 0).OrderByDescending(i => nets[i]).ThenBy(i => playerIds[i]))
            payments.Add(new Payment(bankerId, playerIds[i], nets[i]));
        return payments;
    }

    private static (int Debtor, int Creditor) FindExactPair(List<Balance> debtors, List<Balance> creditors)
    {
        int bestD = -1, bestC = -1;
        for (var d = 0; d < debtors.Count; d++)
        {
            for (var c = 0; c < creditors.Count; c++)
            {
                if (debtors[d].Amount != creditors[c].Amount) continue;
                if (bestD < 0 || IsBetter(debtors[d], creditors[c], debtors[bestD], creditors[bestC]))
                {
                    bestD = d;
                    bestC = c;
                }
            }
        }
        return (bestD, bestC);

        // Largest amount first, then lowest debtor id, then lowest creditor id.
        static bool IsBetter(Balance d, Balance c, Balance bd, Balance bc)
        {
            if (d.Amount != bd.Amount) return d.Amount > bd.Amount;
            if (d.Id != bd.Id) return d.Id < bd.Id;
            return c.Id < bc.Id;
        }
    }

    private static int IndexOfLargest(List<Balance> balances)
    {
        var best = 0;
        for (var i = 1; i < balances.Count; i++)
        {
            var b = balances[i];
            var current = balances[best];
            if (b.Amount > current.Amount || (b.Amount == current.Amount && b.Id < current.Id)) best = i;
        }
        return best;
    }

    private readonly record struct Balance(long Id, long Amount);
}
