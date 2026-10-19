namespace PokerSettle.Core.Money;

/// <summary>
/// Largest-remainder rounding. Turns exact fractional amounts that sum to zero into
/// whole multiples of a unit that still sum to exactly zero.
/// </summary>
public static class Apportionment
{
    /// <summary>
    /// Rounds each exact value <c>numerators[i] / denominator</c> (in minor units) to a multiple of
    /// <paramref name="unit"/>. Every result is the floor or the ceiling of its exact value, and the
    /// results sum to zero. Units left over after flooring go to the largest remainders; ties go to
    /// the lower index, so callers get deterministic output by passing values in a stable order.
    /// </summary>
    /// <exception cref="ArgumentException">The numerators don't sum to zero.</exception>
    public static long[] LargestRemainder(IReadOnlyList<Int128> numerators, Int128 denominator, long unit)
    {
        ArgumentNullException.ThrowIfNull(numerators);
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator), "Denominator must be positive.");
        if (unit <= 0) throw new ArgumentOutOfRangeException(nameof(unit), "Unit must be positive.");

        Int128 sum = 0;
        foreach (var n in numerators) sum += n;
        if (sum != 0) throw new ArgumentException("Values must sum to zero.", nameof(numerators));

        var count = numerators.Count;
        var scale = denominator * unit;
        var quotients = new Int128[count];
        var remainders = new Int128[count];
        Int128 quotientSum = 0;
        for (var i = 0; i < count; i++)
        {
            quotients[i] = FloorDiv(numerators[i], scale);
            remainders[i] = numerators[i] - quotients[i] * scale;
            quotientSum += quotients[i];
        }

        // Remainders sum to -scale * quotientSum, so exactly -quotientSum values (0 <= k < count) round up.
        var bumps = (int)(-quotientSum);
        if (bumps > 0)
        {
            var order = new int[count];
            for (var i = 0; i < count; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                var byRemainder = remainders[b].CompareTo(remainders[a]);
                return byRemainder != 0 ? byRemainder : a.CompareTo(b);
            });
            for (var j = 0; j < bumps; j++) quotients[order[j]] += 1;
        }

        var result = new long[count];
        for (var i = 0; i < count; i++) result[i] = checked((long)(quotients[i] * unit));
        return result;
    }

    /// <summary>Rounds values that sum to zero to multiples of <paramref name="unit"/>, keeping the sum at zero.</summary>
    public static long[] RoundToUnit(IReadOnlyList<long> values, long unit)
    {
        var numerators = new Int128[values.Count];
        for (var i = 0; i < values.Count; i++) numerators[i] = values[i];
        return LargestRemainder(numerators, 1, unit);
    }

    /// <summary>
    /// Splits <paramref name="total"/> into <paramref name="parts"/> near-equal whole amounts.
    /// The first <c>total mod parts</c> parts get one extra unit, so the result is deterministic.
    /// </summary>
    public static long[] SplitEvenly(long total, int parts)
    {
        if (parts <= 0) throw new ArgumentOutOfRangeException(nameof(parts));
        var quotient = FloorDiv(total, parts);
        var remainder = total - quotient * parts; // 0 <= remainder < parts
        var result = new long[parts];
        for (var i = 0; i < parts; i++) result[i] = quotient + (i < remainder ? 1 : 0);
        return result;
    }

    public static Int128 FloorDiv(Int128 a, Int128 b)
    {
        var q = a / b;
        if (a % b != 0 && (a < 0) != (b < 0)) q -= 1;
        return q;
    }

    public static long FloorDiv(long a, long b)
    {
        var q = a / b;
        if (a % b != 0 && (a < 0) != (b < 0)) q -= 1;
        return q;
    }
}
