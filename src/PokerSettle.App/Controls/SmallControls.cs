using Microsoft.Maui.Controls.Shapes;
using PokerSettle.Core.Services;

namespace PokerSettle.App.Controls;

/// <summary>Orange "DEMO" strip shown on every page while the Debug-only demo database is in use.</summary>
public sealed class DemoBanner : ContentView
{
    public DemoBanner()
    {
        var label = new Label
        {
            Text = "DEMO DATABASE · your real data is untouched",
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            HorizontalTextAlignment = TextAlignment.Center,
            Padding = new Thickness(8, 4),
        };
        if (Application.Current?.Resources.TryGetValue("DemoBannerText", out var text) == true) label.TextColor = (Color)text;
        if (Application.Current?.Resources.TryGetValue("DemoBanner", out var background) == true) BackgroundColor = (Color)background;
        Content = label;
        SemanticProperties.SetDescription(this, "Demo database in use. Your real data is untouched.");

        IsVisible = false;
        if (ServiceHelper.TryGet<AppState>() is { } state)
        {
            this.SetBinding(IsVisibleProperty, static (AppState s) => s.IsDemo, source: state);
        }
    }
}

/// <summary>Round avatar with initials on the player's color.</summary>
public sealed class AvatarView : Border
{
    public static readonly BindableProperty InitialsProperty = BindableProperty.Create(
        nameof(Initials), typeof(string), typeof(AvatarView), string.Empty,
        propertyChanged: static (b, _, n) => ((AvatarView)b)._label.Text = n as string);

    public static readonly BindableProperty ColorHexProperty = BindableProperty.Create(
        nameof(ColorHex), typeof(string), typeof(AvatarView), "#1E6B47",
        propertyChanged: static (b, _, n) => ((AvatarView)b).ApplyColor(n as string));

    public static readonly BindableProperty SizeProperty = BindableProperty.Create(
        nameof(Size), typeof(double), typeof(AvatarView), 44d,
        propertyChanged: static (b, _, n) => ((AvatarView)b).ApplySize((double)n));

    private readonly Label _label;

    public AvatarView()
    {
        StrokeThickness = 0;
        StrokeShape = new Ellipse();
        Padding = 0;
        _label = new Label
        {
            TextColor = Colors.White,
            FontFamily = "OpenSansSemibold",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap,
            FontAutoScalingEnabled = false,
        };
        Content = _label;
        // Decorative: the name next to it is what screen readers should read.
        AutomationProperties.SetIsInAccessibleTree(this, false);
        ApplySize(Size);
        ApplyColor(ColorHex);
    }

    public string Initials
    {
        get => (string)GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    public string ColorHex
    {
        get => (string)GetValue(ColorHexProperty);
        set => SetValue(ColorHexProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private void ApplySize(double size)
    {
        WidthRequest = size;
        HeightRequest = size;
        _label.FontSize = Math.Round(size * 0.38);
    }

    private void ApplyColor(string? hex)
    {
        BackgroundColor = Color.TryParse(hex, out var color) ? color : Color.FromArgb("#1E6B47");
    }
}

/// <summary>
/// A result label: green for +, red for −, neutral for zero, following the theme. The text always carries
/// the sign as well, so color is never the only cue.
/// </summary>
public sealed class SignedLabel : Label
{
    public static readonly BindableProperty SignProperty = BindableProperty.Create(
        nameof(Sign), typeof(int), typeof(SignedLabel), 0,
        propertyChanged: static (b, _, n) => ((SignedLabel)b).ApplySign((int)n));

    public SignedLabel()
    {
        FontFamily = "OpenSansSemibold";
        ApplySign(0);
    }

    public int Sign
    {
        get => (int)GetValue(SignProperty);
        set => SetValue(SignProperty, value);
    }

    private void ApplySign(int sign)
    {
        var (light, dark) = sign switch
        {
            > 0 => ("PositiveLight", "PositiveDark"),
            < 0 => ("NegativeLight", "NegativeDark"),
            _ => ("TextSecondaryLight", "TextSecondaryDark"),
        };
        this.SetAppThemeColor(TextColorProperty, Lookup(light), Lookup(dark));
    }

    private static Color Lookup(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color ? color : Colors.Gray;
}
