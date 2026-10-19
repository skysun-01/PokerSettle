namespace PokerSettle.Core.Stats;

public enum TimeFilterKind
{
    AllTime = 0,
    ThisMonth = 1,
    Last3Months = 2,
    ThisYear = 3,
    Custom = 4,
}

/// <summary>Inclusive date range.</summary>
public readonly record struct DateRange(DateOnly From, DateOnly To)
{
    public static readonly DateRange All = new(DateOnly.MinValue, DateOnly.MaxValue);

    public bool Contains(DateOnly date) => date >= From && date <= To;
}

public static class TimeFilters
{
    public static DateRange Resolve(TimeFilterKind kind, DateOnly today, DateOnly? customFrom = null, DateOnly? customTo = null)
    {
        switch (kind)
        {
            case TimeFilterKind.ThisMonth:
                var monthStart = new DateOnly(today.Year, today.Month, 1);
                return new DateRange(monthStart, monthStart.AddMonths(1).AddDays(-1));
            case TimeFilterKind.Last3Months:
                return new DateRange(today.AddMonths(-3).AddDays(1), today);
            case TimeFilterKind.ThisYear:
                return new DateRange(new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31));
            case TimeFilterKind.Custom:
                var from = customFrom ?? DateOnly.MinValue;
                var to = customTo ?? DateOnly.MaxValue;
                return from <= to ? new DateRange(from, to) : new DateRange(to, from);
            default:
                return DateRange.All;
        }
    }

    public static string Label(TimeFilterKind kind) => kind switch
    {
        TimeFilterKind.ThisMonth => "This month",
        TimeFilterKind.Last3Months => "Last 3 months",
        TimeFilterKind.ThisYear => "This year",
        TimeFilterKind.Custom => "Custom range",
        _ => "All time",
    };
}
