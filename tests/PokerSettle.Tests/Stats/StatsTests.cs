using CommunityToolkit.Mvvm.Messaging;
using PokerSettle.Core.Data;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Models;
using PokerSettle.Core.Money;
using PokerSettle.Core.Stats;
using PokerSettle.Core.ViewModels.Board;
using PokerSettle.Tests.Data;

namespace PokerSettle.Tests.Stats;

public class TimeFilterTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    [Fact]
    public void All_time_is_unbounded() =>
        Assert.Equal(DateRange.All, TimeFilters.Resolve(TimeFilterKind.AllTime, Today));

    [Fact]
    public void This_month_is_the_whole_calendar_month() =>
        Assert.Equal(new DateRange(new(2026, 10, 1), new(2026, 10, 31)), TimeFilters.Resolve(TimeFilterKind.ThisMonth, Today));

    [Fact]
    public void This_month_handles_february_in_a_leap_year() =>
        Assert.Equal(new DateRange(new(2028, 2, 1), new(2028, 2, 29)), TimeFilters.Resolve(TimeFilterKind.ThisMonth, new(2028, 2, 10)));

    [Fact]
    public void Last_three_months_ends_today() =>
        Assert.Equal(new DateRange(new(2026, 7, 20), Today), TimeFilters.Resolve(TimeFilterKind.Last3Months, Today));

    [Fact]
    public void This_year_is_january_to_december() =>
        Assert.Equal(new DateRange(new(2026, 1, 1), new(2026, 12, 31)), TimeFilters.Resolve(TimeFilterKind.ThisYear, Today));

    [Fact]
    public void Custom_range_is_inclusive_and_reversed_ranges_are_swapped()
    {
        Assert.Equal(new DateRange(new(2026, 1, 5), new(2026, 2, 5)),
            TimeFilters.Resolve(TimeFilterKind.Custom, Today, new(2026, 2, 5), new(2026, 1, 5)));
        Assert.Equal(new DateRange(new(2026, 1, 5), DateOnly.MaxValue),
            TimeFilters.Resolve(TimeFilterKind.Custom, Today, new(2026, 1, 5), null));
        Assert.True(new DateRange(new(2026, 1, 5), new(2026, 1, 5)).Contains(new(2026, 1, 5)));
    }
}

public class BoardTests
{
    private static BoardRow Row(long id, string name, long net, int games = 1) =>
        new(id, name, null, "#000000", false, games, net, Math.Max(net, 0), Math.Min(net, 0), net > 0 ? 1 : 0, net > 0 ? net : null, net < 0 ? net : null, null);

    [Fact]
    public void Ranks_by_net_with_ties_sharing_a_rank()
    {
        var ranked = BoardMath.RankByNet([Row(1, "Amit", 500), Row(2, "Bea", 900), Row(3, "Cal", 500), Row(4, "Dev", -100)]);
        Assert.Equal([("Bea", 1), ("Amit", 2), ("Cal", 2), ("Dev", 4)], ranked.Select(r => (r.Row.Name, r.Rank)));
    }

    [Fact]
    public void Sorting_by_a_column_keeps_net_ranks_and_breaks_ties_by_net()
    {
        var ranked = BoardMath.RankByNet([Row(1, "Amit", 500, games: 3), Row(2, "Bea", 900, games: 3), Row(3, "Cal", -50, games: 5)]);
        var byGames = BoardMath.Sort(ranked, BoardColumn.Games, descending: true);
        Assert.Equal(["Cal", "Bea", "Amit"], byGames.Select(r => r.Row.Name));
        Assert.Equal([3, 1, 2], byGames.Select(r => r.Rank));
        var byName = BoardMath.Sort(ranked, BoardColumn.Name, descending: false);
        Assert.Equal(["Amit", "Bea", "Cal"], byName.Select(r => r.Row.Name));
    }

    [Fact]
    public async Task Board_aggregates_only_finalized_games_in_the_range()
    {
        using var dir = new TempDir();
        var clock = new FakeClock();
        await using var store = await Stores.OpenAsync(dir, clock);
        var ids = await GameFlows.AddPlayersAsync(store, "Amit", "Rahul", "Priya");
        await GameFlows.PlayFinalizedAsync(store, clock, new DateOnly(2026, 8, 10), ids[0], ids[1], 30_000);
        await GameFlows.PlayFinalizedAsync(store, clock, new DateOnly(2026, 9, 12), ids[1], ids[0], 10_000);
        await GameFlows.PlayFinalizedAsync(store, clock, new DateOnly(2026, 10, 3), ids[0], ids[2], 5_000);
        // In progress: never counts.
        var live = await store.CreateGameAsync(new NewGameRequest("Live", new DateOnly(2026, 10, 18), null, null,
            new GameSettings { DefaultBuyIn = 50_000 }, [ids[0], ids[1]]));
        await store.SetCashOutAsync(live, ids[0], 100_000);

        var all = (await store.GetBoardAsync(DateRange.All, includeArchived: false)).ToDictionary(r => r.Name);
        var amit = all["Amit"];
        Assert.Equal(3, amit.Games);
        Assert.Equal(25_000, amit.Net);
        Assert.Equal(35_000, amit.Won);
        Assert.Equal(-10_000, amit.Lost);
        Assert.Equal(2, amit.Wins);
        Assert.Equal(2.0 / 3, amit.WinRate, 6);
        Assert.Equal(8_333, amit.Average);
        Assert.Equal(30_000, amit.BiggestWin);
        Assert.Equal(-10_000, amit.BiggestLoss);
        Assert.Equal(new DateOnly(2026, 10, 3), amit.LastPlayed);
        Assert.Equal(0, all.Values.Sum(r => r.Net));
        Assert.Null(all["Priya"].BiggestWin);

        var october = await store.GetBoardAsync(TimeFilters.Resolve(TimeFilterKind.ThisMonth, new DateOnly(2026, 10, 19)), false);
        Assert.Equal(2, october.Count);
        Assert.Equal(5_000, october.Single(r => r.Name == "Amit").Net);
    }

    [Fact]
    public async Task Player_page_stats_match_the_board_and_the_chart_is_cumulative()
    {
        using var dir = new TempDir();
        var clock = new FakeClock();
        await using var store = await Stores.OpenAsync(dir, clock);
        var ids = await GameFlows.AddPlayersAsync(store, "Amit", "Rahul");
        await GameFlows.PlayFinalizedAsync(store, clock, new DateOnly(2026, 8, 10), ids[0], ids[1], 30_000);
        await GameFlows.PlayFinalizedAsync(store, clock, new DateOnly(2026, 9, 12), ids[1], ids[0], 45_000);
        await GameFlows.PlayFinalizedAsync(store, clock, new DateOnly(2026, 10, 3), ids[0], ids[1], 5_000);

        var history = (await store.GetPlayerHistoryAsync(ids[0]))!;
        var summary = PlayerStatsMath.Summarize(ids[0], "Amit", null, "#000000", false, history.Games);
        var board = (await store.GetBoardAsync(DateRange.All, true)).Single(r => r.PlayerId == ids[0]);
        Assert.Equal(board with { Color = "#000000" }, summary with { Color = "#000000" });

        var chart = ChartData.From(history.Games, MoneyFormatter.Default);
        Assert.Equal([30_000, -15_000, -10_000], chart.Points.Select(p => p.Cumulative));
        Assert.Equal(30_000, chart.Max);
        Assert.Equal(-15_000, chart.Min);
        Assert.Contains("now", chart.Description);
    }

    [Fact]
    public async Task Board_recomputes_only_when_results_change()
    {
        using var dir = new TempDir();
        await using var harness = new Harness(dir);
        var ids = await GameFlows.AddPlayersAsync(harness.Data, "Amit", "Rahul");
        await GameFlows.PlayFinalizedAsync(harness.Data, harness.Clock, harness.Clock.Today, ids[0], ids[1]);
        var board = harness.Get<BoardViewModel>();

        await board.OnAppearingAsync();
        Assert.Equal(1, board.LoadCount);
        Assert.Equal(2, board.Rows.Count);
        board.OnDisappearing();

        await board.OnAppearingAsync();
        board.OnDisappearing();
        harness.Messenger.Send(new DataChangedMessage(DataArea.Payments));
        await board.OnAppearingAsync();
        Assert.Equal(1, board.LoadCount);
        board.OnDisappearing();

        harness.Messenger.Send(new DataChangedMessage(DataArea.Results));
        await board.OnAppearingAsync();
        Assert.Equal(2, board.LoadCount);
    }

    [Fact]
    public async Task Board_remembers_sort_and_filter()
    {
        using var dir = new TempDir();
        await using var harness = new Harness(dir);
        var board = harness.Get<BoardViewModel>();
        board.SortByCommand.Execute("Games");
        board.SortByCommand.Execute("Games");
        board.FilterIndex = (int)TimeFilterKind.ThisYear;
        board.IncludeArchived = true;

        var settings = harness.Get<Core.Services.AppSettings>();
        Assert.Equal(BoardColumn.Games, settings.BoardSort);
        Assert.False(settings.BoardSortDescending);
        Assert.Equal(TimeFilterKind.ThisYear, settings.BoardFilter);
        Assert.True(settings.BoardIncludeArchived);
        Assert.Equal("Games ▲", board.GamesHeader);
    }
}
