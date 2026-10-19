using System.Globalization;
using System.Text;

namespace PokerSettle.Core.Players;

/// <summary>Name cleanup, duplicate keys, close-match detection, initials and avatar colors.</summary>
public static class PlayerNames
{
    public const int MaxNameLength = 60;

    /// <summary>Avatar palette; all pass WCAG AA with white initials.</summary>
    public static readonly IReadOnlyList<string> Palette =
    [
        "#1E6B47", "#1F5F99", "#8E3A8C", "#B23A3A", "#A35200",
        "#5B4FB3", "#0F766E", "#9D174D", "#4D6B12", "#6B4F2A",
    ];

    /// <summary>Trims and collapses runs of whitespace: "  Amit   K " → "Amit K".</summary>
    public static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var sb = new StringBuilder(name.Length);
        var pendingSpace = false;
        foreach (var ch in name.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = true;
                continue;
            }
            if (pendingSpace && sb.Length > 0) sb.Append(' ');
            pendingSpace = false;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Uniqueness key: case- and whitespace-insensitive ("Amit K" == "amitk").</summary>
    public static string Key(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var normalized = name.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (!char.IsWhiteSpace(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    public static string Initials(string? name)
    {
        var clean = Clean(name);
        if (clean.Length == 0) return "?";
        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var first = FirstLetter(words[0]);
        if (words.Length == 1)
        {
            var e = StringInfo.GetTextElementEnumerator(words[0]);
            e.MoveNext();
            return e.MoveNext() ? (first + (string)e.Current).ToUpperInvariant() : first.ToUpperInvariant();
        }
        return (first + FirstLetter(words[^1])).ToUpperInvariant();

        static string FirstLetter(string word)
        {
            var e = StringInfo.GetTextElementEnumerator(word);
            return e.MoveNext() ? (string)e.Current : string.Empty;
        }
    }

    /// <summary>Stable palette color from the name (FNV-1a; string.GetHashCode is randomized per process).</summary>
    public static string ColorFor(string? name)
    {
        var key = Key(name);
        uint hash = 2166136261;
        foreach (var ch in key)
        {
            hash ^= ch;
            hash *= 16777619;
        }
        return Palette[(int)(hash % (uint)Palette.Count)];
    }

    /// <summary>
    /// Existing names close to <paramref name="input"/> (but not identical): small edit distance,
    /// one name a prefix of the other, or the same first name. Best matches first.
    /// </summary>
    public static IReadOnlyList<T> CloseMatches<T>(string? input, IEnumerable<T> candidates, Func<T, string> nameOf, int max = 3)
    {
        var key = Key(input);
        if (key.Length < 2) return [];
        var firstWord = Key(Clean(input).Split(' ')[0]);

        var scored = new List<(T Item, int Score, string Name)>();
        foreach (var candidate in candidates)
        {
            var name = nameOf(candidate);
            var other = Key(name);
            if (other.Length == 0 || other == key) continue;

            var limit = Math.Max(key.Length, other.Length) <= 4 ? 1 : 2;
            var distance = Distance(key, other, limit);
            int score;
            if (distance <= limit) score = distance;
            else if (Math.Min(key.Length, other.Length) >= 3 && (other.StartsWith(key, StringComparison.Ordinal) || key.StartsWith(other, StringComparison.Ordinal))) score = 3;
            else if (firstWord.Length >= 3 && Key(Clean(name).Split(' ')[0]) == firstWord) score = 4;
            else continue;
            scored.Add((candidate, score, name));
        }
        return scored
            .OrderBy(s => s.Score)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(s => s.Item)
            .ToList();
    }

    /// <summary>Levenshtein distance, giving up (returning limit + 1) once it must exceed <paramref name="limit"/>.</summary>
    public static int Distance(string a, string b, int limit = int.MaxValue)
    {
        if (Math.Abs(a.Length - b.Length) > limit) return limit == int.MaxValue ? limit : limit + 1;
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }
            if (rowMin > limit) return limit == int.MaxValue ? limit : limit + 1;
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
