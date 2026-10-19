using System.Globalization;
using System.Text;

namespace PokerSettle.Core.Money;

public readonly record struct ParseResult(bool Success, long Value, string? Error)
{
    public static ParseResult Ok(long value) => new(true, value, null);
    public static ParseResult Fail(string error) => new(false, 0, error);
}

/// <summary>
/// Parses amounts typed by the user. Money becomes paise (at most two decimals); chips must be whole.
/// Accepts grouping separators, a currency symbol and surrounding spaces. Never uses floating point.
/// </summary>
public static class MoneyParser
{
    /// <summary>Largest accepted amount: 10^13 paise (₹1,00,00,00,00,000); far above any home game.</summary>
    public const long MaxPaise = 10_000_000_000_000;
    public const long MaxChips = 100_000_000_000;

    // Longest first, so "A$" goes before "$" can leave a stray "A".
    private static readonly string[] KnownSymbols = ["Rs.", "INR", "AED", "A$", "C$", "S$", "Rs", "₹", "$", "€", "£"];

    public static ParseResult ParseMoney(string? text, MoneyFormatter formatter, bool allowZero = true)
    {
        var cleaned = Clean(text, formatter.CurrencySymbol);
        if (cleaned.Length == 0) return ParseResult.Fail("Enter an amount");
        if (cleaned[0] is '-' or MoneyFormatter.MinusSign) return ParseResult.Fail("Amount can't be negative");
        if (cleaned[0] == '+') cleaned = cleaned[1..];

        var numbers = formatter.Culture.NumberFormat;
        cleaned = NormalizeDecimalSeparator(cleaned, numbers);

        if (!decimal.TryParse(cleaned, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint, numbers, out var value))
            return ParseResult.Fail("Enter a valid amount");

        var paise = value * 100m;
        if (paise != decimal.Truncate(paise)) return ParseResult.Fail("Use at most 2 decimal places");
        if (paise > MaxPaise) return ParseResult.Fail("Amount is too large");
        if (paise == 0 && !allowZero) return ParseResult.Fail("Enter an amount greater than 0");
        return ParseResult.Ok((long)paise);
    }

    public static ParseResult ParseChips(string? text, MoneyFormatter formatter, bool allowZero = true)
    {
        var cleaned = Clean(text, null);
        if (cleaned.EndsWith("chips", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned[..^5];
        else if (cleaned.EndsWith("chip", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned[..^4];
        if (cleaned.Length == 0) return ParseResult.Fail("Enter a chip count");
        if (cleaned[0] is '-' or MoneyFormatter.MinusSign) return ParseResult.Fail("Chips can't be negative");
        if (cleaned[0] == '+') cleaned = cleaned[1..];

        var numbers = formatter.Culture.NumberFormat;
        if (!decimal.TryParse(cleaned, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint, numbers, out var value))
            return ParseResult.Fail("Enter a whole number of chips");
        if (value != decimal.Truncate(value)) return ParseResult.Fail("Chips must be a whole number");
        if (value > MaxChips) return ParseResult.Fail("Chip count is too large");
        if (value == 0 && !allowZero) return ParseResult.Fail("Enter more than 0 chips");
        return ParseResult.Ok((long)value);
    }

    /// <summary>Parses game units: chips in chip mode, money otherwise.</summary>
    public static ParseResult ParseUnits(string? text, MoneyFormatter formatter, bool chipMode, bool allowZero = true) =>
        chipMode ? ParseChips(text, formatter, allowZero) : ParseMoney(text, formatter, allowZero);

    private static string Clean(string? text, string? currencySymbol)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var s = text.Trim();
        if (!string.IsNullOrEmpty(currencySymbol)) s = s.Replace(currencySymbol, string.Empty, StringComparison.OrdinalIgnoreCase);
        foreach (var symbol in KnownSymbols) s = s.Replace(symbol, string.Empty, StringComparison.OrdinalIgnoreCase);

        // Drop every kind of whitespace (fr-FR groups with U+202F, others with U+00A0).
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (!char.IsWhiteSpace(ch) && ch != ' ' && ch != ' ' && ch != MoneyFormatter.Joiner) sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Phone number pads often only offer '.', so in cultures whose decimal separator is ','
    /// a lone '.' followed by one or two digits is read as the decimal point.
    /// </summary>
    private static string NormalizeDecimalSeparator(string s, NumberFormatInfo numbers)
    {
        if (numbers.NumberDecimalSeparator != "," || s.Contains(',')) return s;
        var dot = s.IndexOf('.');
        if (dot < 0 || dot != s.LastIndexOf('.')) return s;
        var decimals = s.Length - dot - 1;
        return decimals is 1 or 2 ? string.Concat(s.AsSpan(0, dot), ",", s.AsSpan(dot + 1)) : s;
    }
}
