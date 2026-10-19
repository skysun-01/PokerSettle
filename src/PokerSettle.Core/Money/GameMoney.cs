using PokerSettle.Core.Models;

namespace PokerSettle.Core.Money;

/// <summary>
/// Formatting and parsing for one game's units: paise normally, whole chips in chip mode
/// (with the money value alongside).
/// </summary>
public sealed class GameMoney(MoneyFormatter formatter, GameSettings settings)
{
    public MoneyFormatter Formatter { get; } = formatter;
    public bool ChipMode => settings.ChipMode;
    public ChipRate? Rate => settings.ChipRate;

    /// <summary>"₹500" or "1,000 chips".</summary>
    public string Units(long units) => Formatter.FormatUnits(units, ChipMode);

    public string SignedUnits(long units) => Formatter.FormatSignedUnits(units, ChipMode);

    /// <summary>Money value of a chip amount ("₹500"); null outside chip mode.</summary>
    public string? MoneyHint(long units) =>
        ChipMode && Rate is { IsValid: true } rate ? Formatter.Format(MoneyFormatter.ChipsToMoney(units, rate)) : null;

    /// <summary>"₹500", or "1,000 chips · ₹500" in chip mode.</summary>
    public string UnitsWithMoney(long units) => MoneyHint(units) is { } money ? $"{Units(units)} · {money}" : Units(units);

    /// <summary>"+₹500", or "+1,000 chips (+₹500)" in chip mode.</summary>
    public string SignedUnitsWithMoney(long units)
    {
        if (!ChipMode || Rate is not { IsValid: true } rate) return SignedUnits(units);
        return $"{SignedUnits(units)} ({Formatter.FormatSigned(MoneyFormatter.ChipsToMoney(units, rate))})";
    }

    public string ForInput(long units) => Formatter.FormatUnitsForInput(units, ChipMode);

    public ParseResult Parse(string? text, bool allowZero) => MoneyParser.ParseUnits(text, Formatter, ChipMode, allowZero);

    /// <summary>Short label for the default buy-in button: "+ ₹500" or "+ 1,000".</summary>
    public string BuyInButton(long units) => ChipMode ? $"+ {Formatter.FormatChipCount(units)}" : $"+ {Formatter.Format(units)}";
}
