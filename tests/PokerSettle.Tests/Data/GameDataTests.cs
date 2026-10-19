using PokerSettle.Core.Data;
using PokerSettle.Core.Models;
using PokerSettle.Core.Settlement;
using PokerSettle.Core.Stats;

namespace PokerSettle.Tests.Data;

public static class GameFlows
{
    public static readonly FinalizeRequest Plain = new(DiscrepancyResolution.None, null, SettlementMode.FewestPayments, null, RoundingMode.None);

    public static async Task<long[]> AddPlayersAsync(DataStore store, params string[] names)
    {
        var ids = new List<long>();
        foreach (var name in names) ids.Add((await store.AddPlayerAsync(new PlayerDraft(name, null, null, null, null, null))).Id);
        return ids.ToArray();
    }

    /// <summary>Two-player game: the winner takes <paramref name="amount"/> from the loser.</summary>
    public static async Task<long> PlayFinalizedAsync(DataStore store, FakeClock clock, DateOnly date, long winner, long loser, long amount = 20_000)
    {
        var id = await store.CreateGameAsync(new NewGameRequest("Game " + date.ToString("MMdd"), date, null, null,
            new GameSettings { DefaultBuyIn = 50_000 }, [winner, loser]));
        await store.SetCashOutAsync(id, winner, 50_000 + amount);
        await store.SetCashOutAsync(id, loser, 50_000 - amount);
        await store.FinalizeGameAsync(id, Plain);
        clock.Advance(TimeSpan.FromHours(1));
        return id;
    }
}

public class GameDataTests
{
    [Fact]
    public async Task Creating_a_game_seats_players_with_one_default_buy_in_each()
    {
        using var dir = new TempDir();
        await using var store = await Stores.OpenAsync(dir);
        var ids = await GameFlows.AddPlayersAsync(store, "Amit", "Rahul", "Priya");

        var gameId = await store.CreateGameAsync(new NewGameRequest("Sat, 24 Oct", new DateOnly(2026, 10, 24), "Terrace", null,
            new GameSettings { DefaultBuyIn = 50_000 }, [ids[2], ids[0], ids[1]]));
        var game = await store.GetGameAsync(gameId);

        Assert.NotNull(game);
        Assert.Equal(GameStatus.InProgress, game.Game.Status);
        Assert.Equal(new DateOnly(2026, 10, 24), game.Game.Date);
        Assert.Equal(["Priya", "Amit", "Rahul"], game.Entries.Select(e => e.Player.Name));
        Assert.Equal(3, game.BuyIns.Count);
        Assert.All(game.BuyIns, b => Assert.Equal(50_000, b.Amount));
        Assert.Equal(150_000, game.Pot);
        Assert.Equal(gameId, await store.GetInProgressGameIdAsync());
    }

    [Fact]
    public async Task Only_one_game_can_be_in_progress()
    {
        using var dir = new TempDir();
        await using var store = await Stores.OpenAsync(dir);
        var ids = await GameFlows.AddPlayersAsync(store, "Amit", "Rahul");
        var request = new NewGameRequest("One", new DateOnly(2026, 10, 24), null, null, new GameSettings { DefaultBuyIn = 100 }, ids);
        var first = await store.CreateGameAsync(request);

        var ex = await Assert.ThrowsAsync<GameInProgressException>(() => store.CreateGameAsync(request with { Title = "Two" }));
        Assert.Equal(first, ex.GameId);
    }

    [Fact]
    public async Task Buy_ins_can_be_removed_and_restored_with_the_same_id()
    {
        using var dir = new TempDir();
        await using var store = await Stores.OpenAsync(dir);
        var ids = await GameFlows.AddPlayersAsync(store, "Amit", "Rahul");
        var gameId = await store.CreateGameAsync(new NewGameRequest("G", new DateOnly(2026, 10, 24), null, null,
            new GameSettings { DefaultBuyIn = 50_000, StartWithBuyIn = false }, ids));

        var buyIn = await store.AddBuyInAsync(gameId, ids[0], 75_000);
        var removed = await store.RemoveBuyInAsync(buyIn.Id);
        Assert.Empty((await store.GetGameAsync(gameId))!.BuyIns);

        await store.RestoreBuyInAsync(removed!);
        var restored = Assert.Single((await store.GetGameAsync(gameId))!.BuyIns);
        Assert.Equal(buyIn, restored);
    }

    [Fact]
    public async Task Finalize_stores_nets_and_payments_in_one_go()
    {
        using var dir = new TempDir();
        await using var store = await Stores.OpenAsync(dir);
        var ids = await GameFlows.AddPlayersAsync(store, "Amit", "Rahul", "Priya");
        var gameId = await store.CreateGameAsync(new NewGameRequest("G", new DateOnly(2026, 10, 24), null, null,
            new GameSettings { DefaultBuyIn = 50_000 }, ids));
        await store.AddBuyInAsync(gameId, ids[0], 50_000);
        await store.SetCashOutAsync(gameId, ids[0], 0);
        await store.SetCashOutAsync(gameId, ids[1], 170_000);
        await store.SetCashOutAsync(gameId, ids[2], 30_000);

        var result = await store.FinalizeGameAsync(gameId, GameFlows.Plain);
        var game = (await store.GetGameAsync(gameId))!;

        Assert.Equal(GameStatus.Finalized, game.Game.Status);
        Assert.NotNull(game.Game.FinalizedAt);
        Assert.Equal([-100_000, 120_000, -20_000], game.Entries.Select(e => e.Entry.Net));
        Assert.Equal(result.Payments.Count, game.Transfers.Count);
        Assert.Contains(game.Transfers, t => t.FromId == ids[0] && t.ToId == ids[1] && t.Amount == 100_000);
        Assert.Contains(game.Transfers, t => t.FromId == ids[2] && t.ToId == ids[1] && t.Amount == 20_000);
        await Assert.ThrowsAsync<GameNotEditableException>(() => store.AddBuyInAsync(gameId, ids[0], 100));
    }

    [Fact]
    public async Task Unbalanced_game_is_never_finalized_silently()
    {
        using var dir = new TempDir();
        await using var store = await Stores.OpenAsync(dir);
        var ids = await GameFlows.AddPlayersAsync(store, "Amit", "Rahul", "Priya");
        var gameId = await store.CreateGameAsync(new NewGameRequest("G", new DateOnly(2026, 10, 24), null, null,
            new GameSettings { DefaultBuyIn = 50_000 }, ids));
        await store.SetCashOutAsync(gameId, ids[0], 50_100);
        await store.SetCashOutAsync(gameId, ids[1], 50_000);
        await store.SetCashOutAsync(gameId, ids[2], 50_000);

        var ex = await Assert.ThrowsAsync<SettlementValidationException>(() => store.FinalizeGameAsync(gameId, GameFlows.Plain));
        Assert.Equal(100, Assert.Single(ex.Issues).Amount);

        await store.FinalizeGameAsync(gameId, GameFlows.Plain with { Resolution = DiscrepancyResolution.SplitEqually });
        var game = (await store.GetGameAsync(gameId))!;
        Assert.Equal(3, game.Adjustments.Count);
        Assert.All(game.Adjustments, a => Assert.Equal(AdjustmentKind.SplitDifference, a.Kind));
        Assert.Equal(-100, game.Adjustments.Sum(a => a.Amount));
        Assert.Equal(0, game.Entries.Sum(e => e.Entry.Net));
    }

    [Fact]
    public async Task Reopen_clears_results_and_refinalizing_keeps_paid_marks_for_identical_payments()
    {
        using var dir = new TempDir();
        var clock = new FakeClock();
        await using var store = await Stores.OpenAsync(dir, clock);
        var ids = await GameFlows.AddPlayersAsync(store, "Amit", "Rahul", "Priya");
        var gameId = await store.CreateGameAsync(new NewGameRequest("G", new DateOnly(2026, 10, 24), null, null,
            new GameSettings { DefaultBuyIn = 50_000 }, ids));
        await store.SetCashOutAsync(gameId, ids[0], 0);
        await store.SetCashOutAsync(gameId, ids[1], 100_000);
        await store.SetCashOutAsync(gameId, ids[2], 50_000);
        await store.FinalizeGameAsync(gameId, GameFlows.Plain);
        var transfer = Assert.Single((await store.GetGameAsync(gameId))!.Transfers);
        await store.SetTransferPaidAsync(transfer.Id, true);

        await store.ReopenGameAsync(gameId);
        var reopened = (await store.GetGameAsync(gameId))!;
        Assert.Equal(GameStatus.InProgress, reopened.Game.Status);
        Assert.All(reopened.Entries, e => Assert.Null(e.Entry.Net));
        Assert.Empty(await store.GetBoardAsync(DateRange.All, true));
        Assert.Empty(await store.GetPendingPaymentsAsync());

        // Priya's late buy-in changes her payment but not Amit's.
        await store.AddBuyInAsync(gameId, ids[2], 50_000);
        await store.SetCashOutAsync(gameId, ids[1], 150_000);
        await store.FinalizeGameAsync(gameId, GameFlows.Plain);
        var again = (await store.GetGameAsync(gameId))!;
        Assert.True(again.Transfers.Single(t => t.FromId == ids[0]).IsPaid);
        Assert.False(again.Transfers.Single(t => t.FromId == ids[2]).IsPaid);
    }

    [Fact]
    public async Task Deleted_game_can_be_restored_exactly()
    {
        using var dir = new TempDir();
        var clock = new FakeClock();
        await using var store = await Stores.OpenAsync(dir, clock);
        var ids = await GameFlows.AddPlayersAsync(store, "Amit", "Rahul");
        var gameId = await GameFlows.PlayFinalizedAsync(store, clock, new DateOnly(2026, 10, 24), ids[0], ids[1]);
        var before = (await store.GetGameAsync(gameId))!;

        var record = await store.DeleteGameAsync(gameId);
        Assert.Null(await store.GetGameAsync(gameId));
        Assert.Empty(await store.GetBoardAsync(DateRange.All, true));

        await store.RestoreGameAsync(record!);
        var after = (await store.GetGameAsync(gameId))!;
        Assert.Equal(before.Game, after.Game);
        Assert.Equal(before.Entries, after.Entries);
        Assert.Equal(before.BuyIns, after.BuyIns);
        Assert.Equal(before.Transfers, after.Transfers);
        Assert.Equal(2, (await store.GetBoardAsync(DateRange.All, true)).Count);
    }

    [Fact]
    public async Task Removing_a_player_from_a_live_game_can_be_undone()
    {
        using var dir = new TempDir();
        await using var store = await Stores.OpenAsync(dir);
        var ids = await GameFlows.AddPlayersAsync(store, "Amit", "Rahul", "Late");
        var gameId = await store.CreateGameAsync(new NewGameRequest("G", new DateOnly(2026, 10, 24), null, null,
            new GameSettings { DefaultBuyIn = 50_000 }, ids[..2]));
        var lateBuyIn = await store.AddPlayerToGameAsync(gameId, ids[2]);
        Assert.NotNull(lateBuyIn);
        Assert.Equal(2, (await store.GetGameAsync(gameId))!.Entries.Single(e => e.PlayerId == ids[2]).Entry.Position);

        var removed = await store.RemovePlayerFromGameAsync(gameId, ids[2]);
        Assert.Equal(2, (await store.GetGameAsync(gameId))!.Entries.Count);

        await store.RestorePlayerInGameAsync(removed!);
        var game = (await store.GetGameAsync(gameId))!;
        Assert.Equal(3, game.Entries.Count);
        Assert.Equal(lateBuyIn, game.BuyIns.Single(b => b.PlayerId == ids[2]));
    }

    [Fact]
    public async Task Player_names_are_unique_ignoring_case_and_spaces()
    {
        using var dir = new TempDir();
        await using var store = await Stores.OpenAsync(dir);
        var amit = await store.AddPlayerAsync(new PlayerDraft("  Amit   Kumar ", null, null, null, null, null));
        Assert.Equal("Amit Kumar", amit.Name);

        var ex = await Assert.ThrowsAsync<DuplicatePlayerNameException>(() =>
            store.AddPlayerAsync(new PlayerDraft("amitkumar", null, null, null, null, null)));
        Assert.Equal(amit.Id, ex.ExistingPlayerId);

        var rahul = await store.AddPlayerAsync(new PlayerDraft("Rahul", null, null, null, null, null));
        await Assert.ThrowsAsync<DuplicatePlayerNameException>(() => store.UpdatePlayerAsync(rahul with { Name = "AMIT KUMAR" }));
        // A rename shows everywhere because games store ids.
        await store.UpdatePlayerAsync(rahul with { Name = "Rahul S" });
        Assert.Equal("Rahul S", (await store.GetPlayerAsync(rahul.Id))!.Name);
    }

    [Fact]
    public async Task Players_with_history_can_only_be_archived_and_archived_players_are_not_suggested()
    {
        using var dir = new TempDir();
        var clock = new FakeClock();
        await using var store = await Stores.OpenAsync(dir, clock);
        var ids = await GameFlows.AddPlayersAsync(store, "Amit", "Rahul", "Unused");
        await GameFlows.PlayFinalizedAsync(store, clock, new DateOnly(2026, 10, 1), ids[0], ids[1]);

        Assert.True(await store.HasHistoryAsync(ids[0]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeletePlayerAsync(ids[0]));
        await store.DeletePlayerAsync(ids[2]);
        Assert.Null(await store.GetPlayerAsync(ids[2]));

        await store.SetArchivedAsync(ids[0], true);
        Assert.DoesNotContain(await store.GetSuggestionCandidatesAsync(), c => c.PlayerId == ids[0]);
        Assert.DoesNotContain(await store.GetBoardAsync(DateRange.All, includeArchived: false), r => r.PlayerId == ids[0]);
        Assert.Contains(await store.GetBoardAsync(DateRange.All, includeArchived: true), r => r.PlayerId == ids[0]);
    }

    [Fact]
    public async Task Merging_players_moves_all_history_to_the_kept_player()
    {
        using var dir = new TempDir();
        var clock = new FakeClock();
        await using var store = await Stores.OpenAsync(dir, clock);
        var ids = await GameFlows.AddPlayersAsync(store, "Amit", "Amit K", "Rahul");
        await GameFlows.PlayFinalizedAsync(store, clock, new DateOnly(2026, 10, 1), ids[0], ids[2], 10_000);
        await GameFlows.PlayFinalizedAsync(store, clock, new DateOnly(2026, 10, 2), ids[1], ids[2], 5_000);

        await store.MergePlayersAsync(sourceId: ids[1], targetId: ids[0]);

        Assert.Null(await store.GetPlayerAsync(ids[1]));
        var amit = (await store.GetBoardAsync(DateRange.All, true)).Single(r => r.PlayerId == ids[0]);
        Assert.Equal(2, amit.Games);
        Assert.Equal(15_000, amit.Net);
    }

    [Fact]
    public async Task Share_text_lists_results_payments_and_upi()
    {
        using var dir = new TempDir();
        var clock = new FakeClock();
        await using var store = await Stores.OpenAsync(dir, clock);
        var amit = await store.AddPlayerAsync(new PlayerDraft("Amit", null, "+91 98765 43210", null, null, null));
        var rahul = await store.AddPlayerAsync(new PlayerDraft("Rahul", null, null, "rahul@okaxis", null, null));
        var gameId = await GameFlows.PlayFinalizedAsync(store, clock, new DateOnly(2026, 10, 24), rahul.Id, amit.Id, 50_000);

        var text = ShareText.Build((await store.GetGameAsync(gameId))!, Core.Money.MoneyFormatter.Default);

        Assert.Contains("Rahul: +₹500", text);
        Assert.Contains("Amit: −₹500", text);
        Assert.Contains("Amit pays Rahul ₹500 (UPI: rahul@okaxis)", text);
        Assert.Contains("Pot ₹1,000 · 2 players", text);
        Assert.DoesNotContain("98765", text);
    }
}
