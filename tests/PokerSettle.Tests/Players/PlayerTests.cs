using PokerSettle.Core.Data;
using PokerSettle.Core.Players;
using PokerSettle.Tests.Data;

namespace PokerSettle.Tests.Players;

public class PlayerNameTests
{
    [Theory]
    [InlineData("  Amit   Kumar ", "Amit Kumar")]
    [InlineData("Rahul", "Rahul")]
    [InlineData("   ", "")]
    public void Clean_trims_and_collapses_whitespace(string input, string expected) =>
        Assert.Equal(expected, PlayerNames.Clean(input));

    [Theory]
    [InlineData("Amit Kumar", "amit  kumar")]
    [InlineData("AMIT KUMAR", "amitkumar")]
    [InlineData(" Priya ", "priya")]
    public void Keys_are_case_and_whitespace_insensitive(string a, string b) =>
        Assert.Equal(PlayerNames.Key(a), PlayerNames.Key(b));

    [Theory]
    [InlineData("Amit Kumar", "AK")]
    [InlineData("rahul", "RA")]
    [InlineData("Priya S Rao", "PR")]
    [InlineData("", "?")]
    public void Initials(string name, string expected) => Assert.Equal(expected, PlayerNames.Initials(name));

    [Fact]
    public void Color_is_stable_and_from_the_palette()
    {
        Assert.Equal(PlayerNames.ColorFor("Amit"), PlayerNames.ColorFor(" amit "));
        Assert.Contains(PlayerNames.ColorFor("Rahul Sharma"), PlayerNames.Palette);
    }

    [Theory]
    [InlineData("Rahl", "Rahul")]
    [InlineData("Rahul", "Rahul Sharma")]
    [InlineData("Amit S", "Amit Kumar")]
    [InlineData("Priyaa", "Priya")]
    public void Close_matches_are_suggested(string typed, string existing) =>
        Assert.Single(PlayerNames.CloseMatches(typed, [existing], n => n));

    [Theory]
    [InlineData("Rahul", "Priya")]
    [InlineData("Dev", "Mohan Iyer")]
    [InlineData("Rahul", "rahul")]
    public void Unrelated_or_identical_names_are_not_close_matches(string typed, string existing) =>
        Assert.Empty(PlayerNames.CloseMatches(typed, [existing], n => n));
}

public class SuggestionRankingTests
{
    private static SuggestionCandidate C(long id, string name, bool last, int recent, DateOnly? played) =>
        new(id, name, null, "#000000", last, recent, played);

    [Fact]
    public void Ranks_last_game_then_frequency_then_recency_then_name()
    {
        var d = new DateOnly(2026, 10, 1);
        var ranked = SuggestionRanker.Rank(
        [
            C(1, "Zed", last: false, recent: 0, played: null),
            C(2, "Bea", last: false, recent: 5, played: d),
            C(3, "Cal", last: false, recent: 5, played: d.AddDays(3)),
            C(4, "Ann", last: true, recent: 2, played: d.AddDays(7)),
            C(5, "Abe", last: false, recent: 0, played: null),
            C(6, "Dan", last: false, recent: 1, played: d.AddDays(-30)),
            C(7, "Eve", last: true, recent: 9, played: d.AddDays(7)),
        ]);

        Assert.Equal(["Eve", "Ann", "Cal", "Bea", "Dan", "Abe", "Zed"], ranked.Select(r => r.Name));
    }

    [Fact]
    public async Task Database_supplies_the_ranking_signals()
    {
        using var dir = new TempDir();
        var clock = new FakeClock();
        await using var store = await Stores.OpenAsync(dir, clock);
        var ids = new Dictionary<string, long>();
        foreach (var name in new[] { "Amit", "Rahul", "Priya", "Sam", "Newbie" })
            ids[name] = (await store.AddPlayerAsync(new PlayerDraft(name, null, null, null, null, null))).Id;

        // Older games: Sam plays a lot. Last game: Amit and Priya.
        for (var i = 0; i < 3; i++)
            await GameFlows.PlayFinalizedAsync(store, clock, new DateOnly(2026, 9, 1 + i), ids["Sam"], ids["Rahul"]);
        await GameFlows.PlayFinalizedAsync(store, clock, new DateOnly(2026, 10, 1), ids["Amit"], ids["Priya"]);

        var ranked = SuggestionRanker.Rank(await store.GetSuggestionCandidatesAsync());

        Assert.Equal(["Amit", "Priya", "Rahul", "Sam", "Newbie"], ranked.Select(r => r.Name));
        Assert.Equal([ids["Amit"], ids["Priya"]], await store.GetLastGamePlayerIdsAsync());
        var sam = ranked.Single(r => r.Name == "Sam");
        Assert.Equal(3, sam.RecentCount);
        Assert.Equal(new DateOnly(2026, 9, 3), sam.LastPlayed);
    }
}
