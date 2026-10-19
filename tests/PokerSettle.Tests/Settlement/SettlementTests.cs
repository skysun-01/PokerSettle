using PokerSettle.Core.Models;
using PokerSettle.Core.Money;
using PokerSettle.Core.Settlement;

namespace PokerSettle.Tests.Settlement;

public class SettlementTests
{
    private static readonly SettlementOptions Fewest = new(false, null, SettlementMode.FewestPayments, null, RoundingMode.None);

    private static PlayerLine Line(long id, long buyIn, long? cashOut, int buyIns = 1, int position = 0) =>
        new(id, position, buyIn, buyIns, cashOut);

    [Fact]
    public void Single_loser_pays_single_winner()
    {
        var result = SettlementEngine.Settle([Line(1, 50_000, 0), Line(2, 50_000, 100_000)], Fewest);

        var payment = Assert.Single(result.Payments);
        Assert.Equal(new Payment(1, 2, 50_000), payment);
        Assert.Equal(-50_000, result.Results.Single(r => r.PlayerId == 1).Net);
        Assert.Equal(50_000, result.Results.Single(r => r.PlayerId == 2).Net);
    }

    [Fact]
    public void Net_is_cash_out_minus_buy_ins_plus_adjustment()
    {
        var lines = new[] { new PlayerLine(1, 0, 100_000, 2, 30_000, Adjustment: -100), new PlayerLine(2, 1, 50_000, 1, 120_100) };
        var result = SettlementEngine.Settle(lines, Fewest);
        Assert.Equal(-70_100, result.Results.Single(r => r.PlayerId == 1).Net);
        Assert.Equal(70_100, result.Results.Single(r => r.PlayerId == 2).Net);
        Assert.Equal(0, result.Results.Sum(r => r.Net));
    }

    [Fact]
    public void Exact_pairs_are_settled_first_to_save_payments()
    {
        // Greedy biggest-to-biggest alone would need 4 payments here.
        var nets = new long[] { -600, -400, 400, 350, 250 };
        var ids = new long[] { 1, 2, 3, 4, 5 };

        var payments = SettlementEngine.FewestPayments(ids, nets);

        Assert.Equal(3, payments.Count);
        Assert.Contains(new Payment(2, 3, 400), payments);
        Assert.Contains(new Payment(1, 4, 350), payments);
        Assert.Contains(new Payment(1, 5, 250), payments);
    }

    [Fact]
    public void Biggest_loser_pays_biggest_winner()
    {
        var payments = SettlementEngine.FewestPayments([1, 2, 3, 4], [-700, -300, 600, 400]);
        Assert.Equal(new Payment(1, 3, 600), payments[0]);
        Assert.Equal(3, payments.Count);
        AssertSettles([1, 2, 3, 4], [-700, -300, 600, 400], payments);
    }

    [Fact]
    public void Ties_break_on_the_lower_player_id_regardless_of_input_order()
    {
        var a = SettlementEngine.FewestPayments([10, 20, 30, 40], [-500, -500, 300, 700]);
        var b = SettlementEngine.FewestPayments([40, 30, 20, 10], [700, 300, -500, -500]);
        Assert.Equal(a, b);
        Assert.Equal(10, a[0].FromId);
    }

    [Fact]
    public void Everyone_even_needs_no_payments() =>
        Assert.Empty(SettlementEngine.FewestPayments([1, 2, 3], [0, 0, 0]));

    [Fact]
    public void Banker_collects_from_losers_and_pays_winners()
    {
        var options = Fewest with { Mode = SettlementMode.Banker, BankerId = 3 };
        var lines = new[] { Line(1, 50_000, 0), Line(2, 50_000, 70_000), Line(3, 50_000, 80_000) };

        var result = SettlementEngine.Settle(lines, options);

        Assert.Equal([new Payment(1, 3, 50_000), new Payment(3, 2, 20_000)], result.Payments);
    }

    [Fact]
    public void Banker_who_lost_pays_every_winner()
    {
        var payments = SettlementEngine.BankerPayments([1, 2, 3], [-500, 200, 300], bankerId: 1);
        Assert.Equal([new Payment(1, 3, 300), new Payment(1, 2, 200)], payments);
    }

    [Fact]
    public void Banker_mode_requires_a_banker_in_the_game()
    {
        var issues = SettlementEngine.Validate([Line(1, 100, 0), Line(2, 100, 200)],
            Fewest with { Mode = SettlementMode.Banker, BankerId = 99 });
        Assert.Contains(issues, i => i.Kind == IssueKind.MissingBanker);
    }

    [Fact]
    public void Rounding_to_ten_uses_largest_remainder_and_stays_zero_sum()
    {
        var options = Fewest with { Rounding = RoundingMode.Nearest10 };
        var result = SettlementEngine.Settle([Line(1, 0, 123_400, 0), Line(2, 123_400, 0)], options);

        Assert.Equal(123_400, result.Results.Single(r => r.PlayerId == 1).Net);
        Assert.Equal(123_000, result.Results.Single(r => r.PlayerId == 1).SettledNet);
        Assert.Equal(-123_000, result.Results.Single(r => r.PlayerId == 2).SettledNet);
        Assert.Equal(new Payment(2, 1, 123_000), Assert.Single(result.Payments));
    }

    [Fact]
    public void Rounding_to_one_keeps_totals_exact()
    {
        var rounded = Apportionment.RoundToUnit([33_333, 33_333, -66_666], 100);
        Assert.Equal([33_300, 33_300, -66_600], rounded);
        Assert.Equal(0, rounded.Sum());
    }

    [Fact]
    public void Largest_remainder_ties_go_to_the_lowest_index()
    {
        // 1/3 paisa each way: one of the three must absorb the extra paisa.
        var result = Apportionment.LargestRemainder([100, 100, -200], 3, 1);
        Assert.Equal([34, 33, -67], result);
    }

    [Fact]
    public void Chip_mode_balances_in_chips_then_converts_to_money()
    {
        var options = new SettlementOptions(true, new ChipRate(1_000, 50_000), SettlementMode.FewestPayments, null, RoundingMode.None);
        var result = SettlementEngine.Settle([Line(1, 1_000, 1_500), Line(2, 1_000, 500)], options);

        Assert.Equal(500, result.Results.Single(r => r.PlayerId == 1).NetUnits);
        Assert.Equal(25_000, result.Results.Single(r => r.PlayerId == 1).Net);
        Assert.Equal(new Payment(2, 1, 25_000), Assert.Single(result.Payments));
    }

    [Fact]
    public void Chip_mode_with_an_awkward_rate_still_sums_to_zero()
    {
        // 3 chips = ₹1: results of +1, +1, −2 chips are 33.3, 33.3, −66.7 paise.
        var options = new SettlementOptions(true, new ChipRate(3, 100), SettlementMode.FewestPayments, null, RoundingMode.None);
        var lines = new[] { Line(1, 10, 11), Line(2, 10, 11), Line(3, 10, 8) };

        var result = SettlementEngine.Settle(lines, options);

        Assert.Equal([34, 33, -67], result.Results.OrderBy(r => r.PlayerId).Select(r => r.Net));
        Assert.Equal(0, result.Results.Sum(r => r.Net));
    }

    [Fact]
    public void Unbalanced_game_is_reported_with_the_exact_difference()
    {
        var issues = SettlementEngine.Validate([Line(1, 50_000, 40_000), Line(2, 50_000, 50_000)], Fewest);
        var issue = Assert.Single(issues, i => i.Kind == IssueKind.Unbalanced);
        Assert.Equal(-10_000, issue.Amount);
        Assert.Throws<InvalidOperationException>(() => SettlementEngine.Settle([Line(1, 50_000, 40_000), Line(2, 50_000, 50_000)], Fewest));
    }

    [Fact]
    public void Split_difference_equally_assigns_leftover_paise_by_seat()
    {
        // Cash-outs are 100 paise more than buy-ins across 3 players.
        var lines = new[] { Line(7, 50_000, 50_100, position: 0), Line(5, 50_000, 50_000, position: 1), Line(9, 50_000, 50_000, position: 2) };

        var shares = SettlementEngine.ResolveDiscrepancy(lines, DiscrepancyResolution.SplitEqually, null);

        Assert.Equal([new AdjustmentShare(7, -33), new AdjustmentShare(5, -33), new AdjustmentShare(9, -34)], shares);
        var settled = SettlementEngine.Settle(SettlementEngine.ApplyAdjustments(lines, shares), Fewest);
        Assert.Equal(0, settled.Results.Sum(r => r.Net));
    }

    [Fact]
    public void Assign_difference_to_one_player()
    {
        var lines = new[] { Line(1, 50_000, 30_000), Line(2, 50_000, 60_000) };
        var shares = SettlementEngine.ResolveDiscrepancy(lines, DiscrepancyResolution.AssignToPlayer, 2);
        Assert.Equal([new AdjustmentShare(2, 10_000)], shares);
        Assert.Throws<ArgumentException>(() => SettlementEngine.ResolveDiscrepancy(lines, DiscrepancyResolution.AssignToPlayer, 42));
    }

    [Fact]
    public void Validation_blocks_too_few_players_and_missing_amounts_and_warns_on_no_buy_ins()
    {
        var issues = SettlementEngine.Validate([Line(1, 0, null, buyIns: 0)], Fewest);
        Assert.Contains(issues, i => i.Kind == IssueKind.TooFewPlayers && i.IsBlocking);
        Assert.Contains(issues, i => i.Kind == IssueKind.MissingCashOut && i.PlayerId == 1);
        Assert.Contains(issues, i => i.Kind == IssueKind.NoBuyIns && !i.IsBlocking);
    }

    [Fact]
    public void Split_evenly_handles_negative_totals_deterministically()
    {
        Assert.Equal([-33, -33, -34], Apportionment.SplitEvenly(-100, 3));
        Assert.Equal([34, 33, 33], Apportionment.SplitEvenly(100, 3));
        Assert.Equal([0, 0, 0, 0], Apportionment.SplitEvenly(0, 4));
    }

    internal static void AssertSettles(IReadOnlyList<long> ids, IReadOnlyList<long> nets, IReadOnlyList<Payment> payments)
    {
        var balance = ids.Zip(nets).ToDictionary(x => x.First, x => x.Second);
        foreach (var p in payments)
        {
            balance[p.FromId] += p.Amount;
            balance[p.ToId] -= p.Amount;
        }
        Assert.All(balance.Values, v => Assert.Equal(0, v));
    }
}
