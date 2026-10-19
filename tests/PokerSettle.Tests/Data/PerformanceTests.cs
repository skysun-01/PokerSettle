using System.Diagnostics;
using PokerSettle.Core.Data;
using PokerSettle.Core.Stats;
using Xunit.Abstractions;

namespace PokerSettle.Tests.Data;

public class PerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Board_loads_in_under_100ms_with_40_players_and_2000_games()
    {
        using var dir = new TempDir();
        var (players, games) = DemoData.Generate(playerCount: 40, gameCount: 2000, seed: 42, lastDate: new DateOnly(2026, 10, 19));
        await using (var seed = await Stores.OpenAsync(dir))
        {
            var seeding = Stopwatch.StartNew();
            await seed.SeedAsync(players, games);
            output.WriteLine($"Seeded {games.Count} games ({games.Sum(g => g.Entries.Count)} entries, {games.Sum(g => g.BuyIns.Count)} buy-ins) in {seeding.ElapsedMilliseconds} ms");
        }

        // Fresh process-like start: reopen the database, then time the first board load.
        await using var store = await Stores.OpenAsync(dir);
        var stopwatch = Stopwatch.StartNew();
        var board = await store.GetBoardAsync(DateRange.All, includeArchived: true);
        stopwatch.Stop();
        output.WriteLine($"Board (all time, {board.Count} players): {stopwatch.Elapsed.TotalMilliseconds:F1} ms");

        Assert.Equal(40, board.Count);
        Assert.Equal(0, board.Sum(r => r.Net));
        Assert.Equal(games.Sum(g => g.Entries.Count), board.Sum(r => r.Games));
        Assert.True(stopwatch.ElapsedMilliseconds < 100, $"Board took {stopwatch.ElapsedMilliseconds} ms");

        // The other heavy screens, for information.
        var timer = Stopwatch.StartNew();
        var month = await store.GetBoardAsync(TimeFilters.Resolve(TimeFilterKind.ThisMonth, new DateOnly(2026, 10, 19)), false);
        output.WriteLine($"Board (this month, {month.Count} players): {timer.Elapsed.TotalMilliseconds:F1} ms");
        timer.Restart();
        var history = await store.GetGamesAsync(new GameQuery());
        output.WriteLine($"Games history ({history.Count} games): {timer.Elapsed.TotalMilliseconds:F1} ms");
        timer.Restart();
        var home = await store.GetHomeAsync(null);
        output.WriteLine($"Home: {timer.Elapsed.TotalMilliseconds:F1} ms ({home.PendingPayments.Count} pending)");
        timer.Restart();
        var player = await store.GetPlayerHistoryAsync(1);
        output.WriteLine($"Player page ({player!.Games.Count} games): {timer.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal(2000, history.Count);
    }
}
