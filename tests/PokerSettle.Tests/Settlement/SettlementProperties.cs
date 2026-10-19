using FsCheck;
using FsCheck.Xunit;
using PokerSettle.Core.Models;
using PokerSettle.Core.Money;
using PokerSettle.Core.Settlement;

namespace PokerSettle.Tests.Settlement;

/// <summary>
/// FsCheck properties over random balanced results: payments clear every balance, every amount is
/// positive, there are at most n−1 payments, and the output is deterministic (even for shuffled input).
/// </summary>
public class SettlementProperties
{
    /// <summary>Turns arbitrary ints into 2..16 balanced nets (paise) with distinct, shuffled player ids.</summary>
    private static (long[] Ids, long[] Nets) Balanced(int[]? raw, int seed)
    {
        var values = (raw ?? []).Take(15).Select(v => (long)(v % 2_000_000) * 50).ToList();
        if (values.Count == 0) values.Add(0);
        values.Add(-values.Sum());
        var rng = new Random(seed);
        var ids = Enumerable.Range(1, values.Count).Select(i => (long)(i * 7919 % 100_003)).OrderBy(_ => rng.Next()).ToArray();
        return (ids, values.ToArray());
    }

    private static bool Settles(long[] ids, long[] nets, IReadOnlyList<Payment> payments)
    {
        var balance = ids.Zip(nets).ToDictionary(x => x.First, x => x.Second);
        foreach (var p in payments)
        {
            balance[p.FromId] += p.Amount;
            balance[p.ToId] -= p.Amount;
        }
        return balance.Values.All(v => v == 0);
    }

    [Property(MaxTest = 500)]
    public bool Fewest_payments_bring_every_balance_to_zero(int[] raw, int seed)
    {
        var (ids, nets) = Balanced(raw, seed);
        return Settles(ids, nets, SettlementEngine.FewestPayments(ids, nets));
    }

    [Property(MaxTest = 500)]
    public bool Every_payment_is_positive_between_two_different_players(int[] raw, int seed)
    {
        var (ids, nets) = Balanced(raw, seed);
        return SettlementEngine.FewestPayments(ids, nets).All(p => p.Amount > 0 && p.FromId != p.ToId);
    }

    [Property(MaxTest = 500)]
    public bool At_most_n_minus_one_payments(int[] raw, int seed)
    {
        var (ids, nets) = Balanced(raw, seed);
        var nonZero = nets.Count(n => n != 0);
        var payments = SettlementEngine.FewestPayments(ids, nets);
        return payments.Count <= ids.Length - 1 && payments.Count <= Math.Max(0, nonZero - 1);
    }

    [Property(MaxTest = 300)]
    public bool Output_is_deterministic_even_for_shuffled_input(int[] raw, int seed)
    {
        var (ids, nets) = Balanced(raw, seed);
        var first = SettlementEngine.FewestPayments(ids, nets);
        var again = SettlementEngine.FewestPayments(ids, nets);
        var order = Enumerable.Range(0, ids.Length).Reverse().ToArray();
        var shuffled = SettlementEngine.FewestPayments(order.Select(i => ids[i]).ToArray(), order.Select(i => nets[i]).ToArray());
        return first.SequenceEqual(again) && first.SequenceEqual(shuffled);
    }

    [Property(MaxTest = 300)]
    public bool Banker_mode_settles_with_at_most_n_minus_one_payments_through_the_banker(int[] raw, int seed, NonNegativeInt pick)
    {
        var (ids, nets) = Balanced(raw, seed);
        var banker = ids[pick.Get % ids.Length];
        var payments = SettlementEngine.BankerPayments(ids, nets, banker);
        return Settles(ids, nets, payments)
            && payments.Count <= ids.Length - 1
            && payments.All(p => p.Amount > 0 && (p.FromId == banker || p.ToId == banker));
    }

    [Property(MaxTest = 300)]
    public bool Rounding_keeps_the_sum_at_zero_and_each_value_within_one_unit(int[] raw, int seed, bool tens)
    {
        var (_, nets) = Balanced(raw, seed);
        var unit = tens ? 1_000 : 100;
        var rounded = Apportionment.RoundToUnit(nets, unit);
        return rounded.Sum() == 0
            && rounded.All(r => r % unit == 0)
            && rounded.Zip(nets).All(x => Math.Abs(x.First - x.Second) < unit);
    }

    [Property(MaxTest = 200)]
    public bool End_to_end_settlement_from_random_buy_ins_and_cash_outs(int[] raw, int seed, bool chips, bool banker)
    {
        // Build a real game: buy-ins from the random values, cash-outs a random split of the pot.
        var rng = new Random(seed);
        var count = Math.Clamp((raw?.Length ?? 0) + 2, 2, 12);
        var lines = new List<PlayerLine>();
        long pot = 0;
        for (var i = 0; i < count; i++)
        {
            var buyIns = 1 + rng.Next(4);
            var buyIn = buyIns * (chips ? 1_000L : 50_000L);
            pot += buyIn;
            lines.Add(new PlayerLine(100 + i, i, buyIn, buyIns, 0));
        }
        long assigned = 0;
        for (var i = 0; i < count; i++)
        {
            var cash = i == count - 1 ? pot - assigned : (long)(rng.NextDouble() * (pot - assigned));
            assigned += cash;
            lines[i] = lines[i] with { CashOut = cash };
        }
        var options = new SettlementOptions(chips, chips ? new ChipRate(3, 1_000) : null,
            banker ? SettlementMode.Banker : SettlementMode.FewestPayments, banker ? 100 + rng.Next(count) : null,
            (RoundingMode)rng.Next(3));

        var result = SettlementEngine.Settle(lines, options);
        var ids = result.Results.Select(r => r.PlayerId).ToArray();
        var settled = result.Results.Select(r => r.SettledNet).ToArray();
        return result.Results.Sum(r => r.Net) == 0
            && settled.Sum() == 0
            && Settles(ids, settled, result.Payments)
            && result.Payments.Count <= count - 1
            && result.Payments.All(p => p.Amount > 0);
    }
}
