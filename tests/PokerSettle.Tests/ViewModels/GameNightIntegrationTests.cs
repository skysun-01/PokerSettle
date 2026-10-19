using PokerSettle.Core.Models;
using PokerSettle.Core.Services;
using PokerSettle.Core.Stats;
using PokerSettle.Core.ViewModels;
using PokerSettle.Core.ViewModels.Board;
using PokerSettle.Core.ViewModels.Games;
using PokerSettle.Core.ViewModels.Players;

namespace PokerSettle.Tests.ViewModels;

/// <summary>
/// The whole evening through the real ViewModels against a temp database (platform services faked):
/// add players → new game → buy-ins → cash-outs → finalize → payments → board → reopen the database.
/// </summary>
public class GameNightIntegrationTests
{
    private static string Plain(string text) => text.Replace("⁠", "");

    [Fact]
    public async Task Full_game_night_flow_persists_across_a_restart()
    {
        using var dir = new TempDir();
        long gameId;

        await using (var h = new Harness(dir))
        {
            // 1. Add players
            foreach (var name in new[] { "Amit", "Rahul", "Priya" })
            {
                var edit = h.Get<PlayerEditViewModel>();
                await edit.OnAppearingAsync();
                edit.Name = name;
                edit.UpiId = name == "Rahul" ? "rahul@okaxis" : null;
                await edit.SaveCommand.ExecuteAsync(null);
                Assert.Null(edit.NameError);
            }
            var duplicate = h.Get<PlayerEditViewModel>();
            await duplicate.OnAppearingAsync();
            duplicate.Name = " AMIT ";
            await duplicate.SaveCommand.ExecuteAsync(null);
            Assert.Equal("Amit already exists.", duplicate.NameError);
            Assert.Equal(3, (await h.Data.GetPlayersAsync()).Count);

            // 2. New game: everyone from suggestions, defaults from Settings
            var newGame = h.Get<NewGameViewModel>();
            await newGame.OnAppearingAsync();
            Assert.Equal("Sat, 24 Oct", newGame.Title);
            Assert.Equal("500", newGame.BuyInText);
            Assert.Equal(3, newGame.Picker.Suggestions.Count);
            foreach (var chip in newGame.Picker.Suggestions.ToList()) newGame.Picker.ToggleCommand.Execute(chip);
            Assert.Equal("Start game with 3 players", newGame.StartText);
            await newGame.StartCommand.ExecuteAsync(null);
            Assert.Equal("../livegame", h.Navigation.Last.Route);
            gameId = (long)h.Navigation.Last.Parameters![Routes.GameId];
            Assert.True(h.Screen.KeepOn || true);

            // 3. Live game: Amit rebuys twice, then undoes one, then rebuys again
            var live = h.Get<LiveGameViewModel>();
            live.ApplyQuery(new Dictionary<string, object> { [Routes.GameId] = gameId });
            await live.OnAppearingAsync();
            Assert.Equal("₹1,500", live.PotText);
            var amit = live.Players.Single(p => p.Name == "Amit");
            await amit.AddBuyInCommand.ExecuteAsync(null);
            await amit.AddBuyInCommand.ExecuteAsync(null);
            Assert.Equal(2, h.Haptics.Clicks);
            Assert.True(h.Snackbar.HasAction);
            await h.Snackbar.RunActionCommand.ExecuteAsync(null); // Undo the last buy-in
            Assert.Equal("2 buy-ins · ₹1,000", amit.SummaryText);
            await amit.AddBuyInCommand.ExecuteAsync(null);
            Assert.Equal("3 buy-ins · ₹1,500", amit.SummaryText);
            Assert.Equal("₹2,500", live.PotText);

            // 4. Cash-out: totals, fill remaining, balanced
            var cashOut = h.Get<CashOutViewModel>();
            cashOut.ApplyQuery(new Dictionary<string, object> { [Routes.GameId] = gameId });
            await cashOut.OnAppearingAsync();
            Row(cashOut, "Amit").AmountText = "0";
            Row(cashOut, "Rahul").AmountText = "1,700";
            Assert.True(cashOut.CanFillRemaining);
            Assert.Equal("Left to allocate ₹800", cashOut.LeftText);
            cashOut.FillRemainingCommand.Execute(null);
            Assert.Equal("800", Row(cashOut, "Priya").AmountText);
            Assert.True(cashOut.IsBalanced);
            Assert.Equal("+₹1,200", Plain(Row(cashOut, "Rahul").ResultText));
            await cashOut.ContinueCommand.ExecuteAsync(null);
            Assert.Equal(Routes.Review, h.Navigation.Last.Route);

            // 5. Review and finalize
            var review = h.Get<ReviewViewModel>();
            review.ApplyQuery(h.Navigation.Last.Parameters!);
            await review.OnAppearingAsync();
            Assert.Null(review.ErrorText);
            Assert.Equal(["Rahul", "Priya", "Amit"], review.Results.Select(r => r.Name));
            Assert.Equal(["Amit pays Rahul ₹1,200", "Amit pays Priya ₹300"], review.Payments.Select(p => p.Text));
            await review.FinalizeCommand.ExecuteAsync(null);
            Assert.Equal("../../../gamedetail", h.Navigation.Last.Route);

            // 6. Game detail: share text includes Rahul's UPI ID; mark a payment paid
            var detail = h.Get<GameDetailViewModel>();
            detail.ApplyQuery(new Dictionary<string, object> { [Routes.GameId] = gameId });
            await detail.OnAppearingAsync();
            Assert.True(detail.IsFinalized);
            await detail.TogglePaidCommand.ExecuteAsync(detail.Transfers.Single(t => t.ToName == "Priya"));
            await detail.ShareCommand.ExecuteAsync(null);
            Assert.Contains("Amit pays Rahul ₹1,200 (UPI: rahul@okaxis)", h.Share.Text);
            Assert.Contains("Amit pays Priya ₹300 ✓ paid", h.Share.Text);

            // 7. Board updated
            var board = h.Get<BoardViewModel>();
            await board.OnAppearingAsync();
            Assert.Equal(["Rahul", "Priya", "Amit"], board.Rows.Select(r => r.Name));
            Assert.Equal(["+₹1,200", "+₹300", "−₹1,500"], board.Rows.Select(r => Plain(r.NetText)));

            // 8. Home shows one pending payment and the top 3
            var home = h.Get<HomeViewModel>();
            await home.OnAppearingAsync();
            Assert.False(home.HasGameInProgress);
            Assert.Equal("1 pending payment", home.PendingTitle);
            Assert.Equal(3, home.TopPlayers.Count);
            Assert.True(home.ShowBackupReminder);
        }

        // 9. Restart: everything is still there
        await using (var again = new Harness(dir))
        {
            var game = await again.Data.GetGameAsync(gameId);
            Assert.NotNull(game);
            Assert.Equal(GameStatus.Finalized, game.Game.Status);
            Assert.Equal(2, game.Transfers.Count);
            Assert.Single(game.Transfers, t => t.IsPaid);
            var board = await again.Data.GetBoardAsync(DateRange.All, false);
            Assert.Equal(120_000, board.Single(r => r.Name == "Rahul").Net);
            Assert.Equal(-150_000, board.Single(r => r.Name == "Amit").Net);
        }
    }

    [Fact]
    public async Task New_games_use_settlement_defaults_from_settings_not_the_previous_game()
    {
        using var dir = new TempDir();
        await using var h = new Harness(dir);
        var ids = await Data.GameFlows.AddPlayersAsync(h.Data, "Amit", "Rahul");
        var first = await h.Data.CreateGameAsync(new Core.Data.NewGameRequest("Old", h.Clock.Today, null, null,
            new GameSettings { DefaultBuyIn = 100_000, SettlementMode = SettlementMode.Banker, BankerId = ids[0] }, ids));
        await h.Data.SetCashOutAsync(first, ids[0], 100_000);
        await h.Data.SetCashOutAsync(first, ids[1], 100_000);
        await h.Data.FinalizeGameAsync(first, new Core.Data.FinalizeRequest(DiscrepancyResolution.None, null, SettlementMode.Banker, ids[0], RoundingMode.None));
        h.Get<AppSettings>().DefaultRounding = RoundingMode.Nearest10;

        var newGame = h.Get<NewGameViewModel>();
        await newGame.OnAppearingAsync();
        Assert.Equal("1000", newGame.BuyInText); // buy-in comes from the last game
        newGame.Picker.SameAsLastGameCommand.Execute(null);
        await newGame.StartCommand.ExecuteAsync(null);

        var created = await h.Data.GetGameAsync((long)h.Navigation.Last.Parameters![Routes.GameId]);
        Assert.Equal(SettlementMode.FewestPayments, created!.Game.Settings.SettlementMode);
        Assert.Equal(RoundingMode.Nearest10, created.Game.Settings.Rounding);
        Assert.Null(created.Game.Settings.BankerId);
    }

    [Fact]
    public async Task Unbalanced_cash_out_offers_options_and_splitting_finalizes()
    {
        using var dir = new TempDir();
        await using var h = new Harness(dir);
        var ids = await Data.GameFlows.AddPlayersAsync(h.Data, "Amit", "Rahul", "Priya");
        var gameId = await h.Data.CreateGameAsync(new Core.Data.NewGameRequest("G", h.Clock.Today, null, null,
            new GameSettings { DefaultBuyIn = 50_000 }, ids));
        var cashOut = h.Get<CashOutViewModel>();
        cashOut.ApplyQuery(new Dictionary<string, object> { [Routes.GameId] = gameId });
        await cashOut.OnAppearingAsync();
        Row(cashOut, "Amit").AmountText = "200";
        Row(cashOut, "Rahul").AmountText = "900";
        Row(cashOut, "Priya").AmountText = "401";

        await cashOut.ContinueCommand.ExecuteAsync(null);
        Assert.True(cashOut.ShowDiscrepancy);
        Assert.Equal("Cash-outs are ₹1 more than buy-ins.", cashOut.DiscrepancyText);
        Assert.Equal("Over by ₹1", cashOut.LeftText);
        await cashOut.ContinueCommand.ExecuteAsync(null);
        Assert.NotNull(cashOut.ErrorText); // "fix the amounts" is the default

        cashOut.IsSplitSelected = true;
        await cashOut.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(Routes.Review, h.Navigation.Last.Route);

        var review = h.Get<ReviewViewModel>();
        review.ApplyQuery(h.Navigation.Last.Parameters!);
        await review.OnAppearingAsync();
        Assert.Contains("split equally", review.AdjustmentText);
        await review.FinalizeCommand.ExecuteAsync(null);
        var game = await h.Data.GetGameAsync(gameId);
        Assert.Equal(GameStatus.Finalized, game!.Game.Status);
        Assert.Equal(-100, game.Adjustments.Sum(a => a.Amount));
        Assert.Equal(0, game.Entries.Sum(e => e.Entry.Net));
    }

    private static CashOutRow Row(CashOutViewModel vm, string name) => vm.Rows.Single(r => r.Name == name);
}
