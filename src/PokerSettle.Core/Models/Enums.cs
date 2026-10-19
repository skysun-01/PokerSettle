namespace PokerSettle.Core.Models;

public enum GameStatus
{
    InProgress = 0,
    Finalized = 1,
}

public enum SettlementMode
{
    FewestPayments = 0,
    Banker = 1,
}

/// <summary>How settlement payments are rounded. Amounts are in minor units (paise).</summary>
public enum RoundingMode
{
    None = 0,
    /// <summary>Nearest whole currency unit (₹1 = 100 paise).</summary>
    Nearest1 = 1,
    /// <summary>Nearest ten currency units (₹10 = 1,000 paise).</summary>
    Nearest10 = 2,
}

public enum AdjustmentKind
{
    /// <summary>Share of a discrepancy split equally across all players.</summary>
    SplitDifference = 0,
    /// <summary>A discrepancy assigned to one player.</summary>
    AssignedDifference = 1,
}

public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>How the cash-out screen resolves cash-outs that don't match buy-ins.</summary>
public enum DiscrepancyResolution
{
    None = 0,
    SplitEqually = 1,
    AssignToPlayer = 2,
}
