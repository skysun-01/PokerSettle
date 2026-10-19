using System.Globalization;
using PokerSettle.Core.Models;

namespace PokerSettle.Core.Money;

/// <summary>
/// Formats paise with the configured culture and currency symbol (never the device culture).
/// Whole amounts hide ".00". Results are signed: +₹1,250, −₹500, ₹0.
/// </summary>
public sealed class MoneyFormatter
{
    /// <summary>U+2212 MINUS SIGN; reads better than a hyphen and screen readers announce it as "minus".</summary>
    public const char MinusSign = '−';
    public const string DefaultCulture = "en-IN";
    public const string DefaultCurrency = "₹";

    private readonly NumberFormatInfo _numbers;
    private readonly int _symbolPattern;

    public MoneyFormatter(string? cultureName, string? currencySymbol)
    {
        Culture = ResolveCulture(cultureName);
        CurrencySymbol = string.IsNullOrWhiteSpace(currencySymbol) ? DefaultCurrency : currencySymbol.Trim();
        _numbers = Culture.NumberFormat;
        _symbolPattern = _numbers.CurrencyPositivePattern;
    }

    public static MoneyFormatter Default { get; } = new(DefaultCulture, DefaultCurrency);

    public CultureInfo Culture { get; }
    public string CurrencySymbol { get; }

    public static CultureInfo ResolveCulture(string? cultureName)
    {
        if (!string.IsNullOrWhiteSpace(cultureName))
        {
            try
            {
                return CultureInfo.GetCultureInfo(cultureName);
            }
            catch (CultureNotFoundException)
            {
                // Fall through to the default.
            }
        }
        return CultureInfo.GetCultureInfo(DefaultCulture);
    }

    /// <summary>"₹1,25,000", "₹1,250.50"; negative amounts get a leading minus sign.</summary>
    public string Format(long paise) => paise < 0 ? MinusSign + WithSymbol(paise) : WithSymbol(paise);

    /// <summary>"+₹1,250", "−₹500", "₹0".</summary>
    public string FormatSigned(long paise) => paise switch
    {
        > 0 => "+" + WithSymbol(paise),
        < 0 => MinusSign + WithSymbol(paise),
        _ => WithSymbol(0),
    };

    /// <summary>Grouped number without the symbol: "1,25,000" or "1,250.50".</summary>
    public string FormatNumber(long paise)
    {
        var text = Number(paise);
        return paise < 0 ? MinusSign + text : text;
    }

    /// <summary>Plain value for an input box: "1250" or "1250.50" (culture decimal separator, no grouping).</summary>
    public string FormatForInput(long paise)
    {
        var value = Math.Abs((decimal)paise) / 100m;
        var text = paise % 100 == 0
            ? value.ToString("0", _numbers)
            : value.ToString("0.00", _numbers);
        return paise < 0 ? "-" + text : text;
    }

    /// <summary>"1,000 chips".</summary>
    public string FormatChips(long chips) => FormatChipCount(chips) + (Math.Abs(chips) == 1 ? " chip" : " chips");

    /// <summary>"1,000" (grouped, no suffix).</summary>
    public string FormatChipCount(long chips)
    {
        var text = Math.Abs((decimal)chips).ToString("N0", _numbers);
        return chips < 0 ? MinusSign + text : text;
    }

    public string FormatSignedChips(long chips) => chips switch
    {
        > 0 => "+" + FormatChips(chips),
        < 0 => FormatChips(chips),
        _ => FormatChips(0),
    };

    /// <summary>Formats an amount in game units: chips in chip mode, money otherwise.</summary>
    public string FormatUnits(long units, bool chipMode) => chipMode ? FormatChips(units) : Format(units);

    public string FormatSignedUnits(long units, bool chipMode) => chipMode ? FormatSignedChips(units) : FormatSigned(units);

    /// <summary>Plain input text for game units.</summary>
    public string FormatUnitsForInput(long units, bool chipMode) =>
        chipMode ? units.ToString(CultureInfo.InvariantCulture) : FormatForInput(units);

    /// <summary>Money value of a chip count at a rate, rounded to the nearest paisa.</summary>
    public static long ChipsToMoney(long chips, ChipRate rate)
    {
        if (!rate.IsValid) return 0;
        var exact = (decimal)chips * rate.Money / rate.Chips;
        return (long)Math.Round(exact, MidpointRounding.AwayFromZero);
    }

    public string FormatDate(DateOnly date) => date.ToString("ddd, d MMM yyyy", Culture);

    public string FormatShortDate(DateOnly date) => date.ToString("ddd, d MMM", Culture);

    public string FormatDateTime(DateTimeOffset value) => value.ToLocalTime().ToString("d MMM yyyy, HH:mm", Culture);

    /// <summary>Default game title for a date, e.g. "Sat, 24 Oct".</summary>
    public string DefaultGameTitle(DateOnly date) => FormatShortDate(date);

    public string FormatPercent(double fraction) => (fraction * 100).ToString("0", _numbers) + "%";

    private string WithSymbol(long paise)
    {
        var number = Number(paise);
        // Like CLDR currency spacing: a letter symbol ("AED", "Rs") never touches the digits.
        return _symbolPattern switch
        {
            1 => char.IsLetter(CurrencySymbol[0]) ? number + " " + CurrencySymbol : number + CurrencySymbol,
            2 => CurrencySymbol + " " + number,
            3 => number + " " + CurrencySymbol,
            _ => char.IsLetter(CurrencySymbol[^1]) ? CurrencySymbol + " " + number : CurrencySymbol + number,
        };
    }

    private string Number(long paise)
    {
        var value = Math.Abs((decimal)paise) / 100m;
        return paise % 100 == 0 ? value.ToString("N0", _numbers) : value.ToString("N2", _numbers);
    }
}
