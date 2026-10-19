using PokerSettle.Core.Stats;

namespace PokerSettle.App.Controls;

/// <summary>Cumulative profit/loss line chart, drawn with Microsoft.Maui.Graphics (no chart library).</summary>
public sealed class ProfitChartView : GraphicsView
{
    public static readonly BindableProperty DataProperty = BindableProperty.Create(
        nameof(Data), typeof(ChartData), typeof(ProfitChartView), ChartData.Empty,
        propertyChanged: static (b, _, n) => ((ProfitChartView)b).OnDataChanged((ChartData)n));

    private readonly ProfitChartDrawable _drawable = new();

    public ProfitChartView()
    {
        Drawable = _drawable;
        HeightRequest = 220;
        SemanticProperties.SetDescription(this, ChartData.Empty.Description);
    }

    public ChartData Data
    {
        get => (ChartData)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Application.Current is not { } app) return;
        app.RequestedThemeChanged -= OnThemeChanged;
        if (Handler is not null) app.RequestedThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => Invalidate();

    private void OnDataChanged(ChartData data)
    {
        _drawable.Data = data;
        SemanticProperties.SetDescription(this, data.Description);
        Invalidate();
    }
}

internal sealed class ProfitChartDrawable : IDrawable
{
    public ChartData Data { get; set; } = ChartData.Empty;

    public void Draw(ICanvas canvas, RectF rect)
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var text = Color.FromArgb(dark ? "#A7B4AC" : "#4C5852");
        var grid = Color.FromArgb(dark ? "#34433A" : "#CBD5CE");
        var line = Color.FromArgb(dark ? "#5CC98F" : "#1E6B47");
        var positive = Color.FromArgb(dark ? "#6BD99A" : "#146C34");
        var negative = Color.FromArgb(dark ? "#FF8F86" : "#B3261E");

        canvas.FontColor = text;
        canvas.FontSize = 12;
        if (!Data.HasPoints)
        {
            canvas.DrawString(Data.Description, rect, HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        const float labelTop = 18, labelBottom = 20, pad = 8;
        var plot = new RectF(rect.Left + pad, rect.Top + labelTop, rect.Width - 2 * pad, rect.Height - labelTop - labelBottom);
        var points = Data.Points;
        double min = Data.Min, max = Data.Max;
        if (max - min < 1) max = min + 1;

        float X(int index) => points.Count == 1
            ? plot.Center.X
            : plot.Left + plot.Width * index / (points.Count - 1);
        float Y(double value) => (float)(plot.Top + plot.Height * (max - value) / (max - min));

        // Zero line
        var zeroY = Y(0);
        canvas.StrokeColor = grid;
        canvas.StrokeSize = 1;
        canvas.StrokeDashPattern = [4, 4];
        canvas.DrawLine(plot.Left, zeroY, plot.Right, zeroY);
        canvas.StrokeDashPattern = null;

        // Area under the line (to zero), then the line itself
        var area = new PathF();
        area.MoveTo(X(0), zeroY);
        for (var i = 0; i < points.Count; i++) area.LineTo(X(i), Y(points[i].Cumulative));
        area.LineTo(X(points.Count - 1), zeroY);
        area.Close();
        canvas.FillColor = line.WithAlpha(0.12f);
        canvas.FillPath(area);

        var path = new PathF();
        path.MoveTo(X(0), Y(points[0].Cumulative));
        for (var i = 1; i < points.Count; i++) path.LineTo(X(i), Y(points[i].Cumulative));
        canvas.StrokeColor = line;
        canvas.StrokeSize = 2.5f;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(path);

        var last = points[^1];
        canvas.FillColor = last.Cumulative >= 0 ? positive : negative;
        canvas.FillCircle(X(points.Count - 1), Y(last.Cumulative), 4.5f);

        // Labels: range on the left, first and last dates underneath
        canvas.FontColor = text;
        canvas.DrawString(Data.MaxLabel, rect.Left + pad, rect.Top, 200, labelTop, HorizontalAlignment.Left, VerticalAlignment.Top);
        if (Data.Min < 0)
            canvas.DrawString(Data.MinLabel, rect.Left + pad, plot.Bottom - 16, 200, 16, HorizontalAlignment.Left, VerticalAlignment.Bottom);
        canvas.DrawString(Data.StartLabel, rect.Left + pad, plot.Bottom + 2, 150, labelBottom, HorizontalAlignment.Left, VerticalAlignment.Top);
        canvas.DrawString(Data.EndLabel, rect.Right - pad - 150, plot.Bottom + 2, 150, labelBottom, HorizontalAlignment.Right, VerticalAlignment.Top);
    }
}
