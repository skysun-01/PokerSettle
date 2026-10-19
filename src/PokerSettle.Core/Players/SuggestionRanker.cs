namespace PokerSettle.Core.Players;

/// <summary>A player who could be added to a game, with the signals used to rank suggestions.</summary>
public sealed record SuggestionCandidate(
    long PlayerId,
    string Name,
    string? Nickname,
    string Color,
    bool InLastGame,
    int RecentCount,
    DateOnly? LastPlayed);

/// <summary>
/// Ranks suggested players so regulars come first: played last game → most frequent in the last 10
/// games → most recently played → name (then id, so the order never changes).
/// </summary>
public static class SuggestionRanker
{
    public const int RecentGameCount = 10;

    public static IReadOnlyList<SuggestionCandidate> Rank(IEnumerable<SuggestionCandidate> candidates) =>
        candidates
            .OrderByDescending(c => c.InLastGame)
            .ThenByDescending(c => c.RecentCount)
            .ThenByDescending(c => c.LastPlayed ?? DateOnly.MinValue)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.PlayerId)
            .ToList();
}
