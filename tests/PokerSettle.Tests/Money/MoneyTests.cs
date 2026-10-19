using System.Globalization;
using PokerSettle.Core.Models;
using PokerSettle.Core.Money;

namespace PokerSettle.Tests.Money;

public class MoneyFormatterTests
{
    private static readonly MoneyFormatter Inr = new("en-IN", "₹");

    [Theory]
    [InlineData(12_500_000, "₹1,25,000")]
    [InlineData(1_000_000_000, "₹1,00,00,000")]
    [InlineData(125_000, "₹1,250")]
    [InlineData(125_050, "₹1,250.50")]
    [InlineData(5, "₹0.05")]
    [InlineData(0, "₹0")]
    [InlineData(-50_000, "−₹500")]
    public void Formats_indian_grouping_and_hides_whole_decimals(long paise, string expected) =>
        Assert.Equal(expected, Inr.Format(paise));

    [Theory]
    [InlineData(125_000, "+₹1,250")]
    [InlineData(-50_000, "−₹500")]
    [InlineData(0, "₹0")]
    [InlineData(12_500_050, "+₹1,25,000.50")]
    public void Signed_results_always_carry_a_sign(long paise, string expected) =>
        Assert.Equal(expected, Inr.FormatSigned(paise));

    [Fact]
    public void Minus_is_the_unicode_minus_sign() =>
        Assert.Equal('−', Inr.FormatSigned(-100)[0]);

    [Fact]
    public void Uses_the_configured_culture_not_the_device_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("₹1,25,000.50", Inr.Format(12_500_050));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData("en-US", "$", 12_345_678, "+$123,456.78")]
    [InlineData("de-DE", "€", 125_050, "+1.250,50 €")]
    [InlineData("en-GB", "£", -99, "−£0.99")]
    [InlineData("en-US", "AED", 125_000, "+AED 1,250")]
    public void Other_cultures_and_symbols(string culture, string symbol, long paise, string expected) =>
        Assert.Equal(expected, new MoneyFormatter(culture, symbol).FormatSigned(paise));

    [Fact]
    public void Unknown_culture_falls_back_to_en_IN() =>
        Assert.Equal("₹1,25,000", new MoneyFormatter("xx-NOPE", "₹").Format(12_500_000));

    [Fact]
    public void Chips_and_chip_conversion()
    {
        Assert.Equal("1,000 chips", Inr.FormatChips(1_000));
        Assert.Equal("1 chip", Inr.FormatChips(1));
        Assert.Equal("+1,500 chips", Inr.FormatSignedChips(1_500));
        Assert.Equal(50_000, MoneyFormatter.ChipsToMoney(1_000, new ChipRate(1_000, 50_000)));
        Assert.Equal(33, MoneyFormatter.ChipsToMoney(1, new ChipRate(3, 100)));
    }

    [Fact]
    public void Input_text_has_no_grouping_or_symbol()
    {
        Assert.Equal("125000", Inr.FormatForInput(12_500_000));
        Assert.Equal("1250.50", Inr.FormatForInput(125_050));
        Assert.Equal("1250,50", new MoneyFormatter("de-DE", "€").FormatForInput(125_050));
    }

    [Fact]
    public void Default_title_is_short_date() =>
        Assert.Equal("Sat, 24 Oct", Inr.DefaultGameTitle(new DateOnly(2026, 10, 24)));

    [Fact]
    public void Game_money_shows_chips_with_money_value()
    {
        var money = new GameMoney(Inr, new GameSettings { ChipMode = true, ChipRate = new ChipRate(1_000, 50_000) });
        Assert.Equal("2,000 chips · ₹1,000", money.UnitsWithMoney(2_000));
        Assert.Equal("+500 chips (+₹250)", money.SignedUnitsWithMoney(500));
        Assert.Equal("+ 1,000", money.BuyInButton(1_000));
    }
}

public class MoneyParserTests
{
    private static readonly MoneyFormatter Inr = new("en-IN", "₹");

    [Theory]
    [InlineData("500", 50_000)]
    [InlineData("1,25,000", 12_500_000)]
    [InlineData("₹ 1,250.50", 125_050)]
    [InlineData(" 1250.5 ", 125_050)]
    [InlineData("0", 0)]
    [InlineData("+300", 30_000)]
    [InlineData("Rs. 200", 20_000)]
    [InlineData("0.01", 1)]
    public void Parses_valid_amounts(string text, long expected)
    {
        var result = MoneyParser.ParseMoney(text, Inr);
        Assert.True(result.Success, result.Error);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("−5")]
    [InlineData("1.234")]
    [InlineData("12.3.4")]
    [InlineData("99999999999999999")]
    public void Rejects_invalid_amounts(string text)
    {
        var result = MoneyParser.ParseMoney(text, Inr);
        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void Zero_can_be_disallowed() =>
        Assert.False(MoneyParser.ParseMoney("0", Inr, allowZero: false).Success);

    [Theory]
    [InlineData("1.250,50", 125_050)]
    [InlineData("12.5", 1_250)]
    [InlineData("1.250", 125_000)]
    [InlineData("1 250,5", 125_050)]
    public void Comma_decimal_cultures_accept_phone_keypad_dots(string text, long expected)
    {
        var result = MoneyParser.ParseMoney(text, new MoneyFormatter("de-DE", "€"));
        Assert.True(result.Success, result.Error);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("1,000", 1_000)]
    [InlineData("500 chips", 500)]
    [InlineData("0", 0)]
    public void Parses_whole_chips(string text, long expected)
    {
        var result = MoneyParser.ParseChips(text, Inr);
        Assert.True(result.Success, result.Error);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("10.5")]
    [InlineData("-10")]
    [InlineData("lots")]
    public void Rejects_fractional_or_negative_chips(string text) =>
        Assert.False(MoneyParser.ParseChips(text, Inr).Success);
}
