namespace PlanetForge.Domain.Origins;

/// <summary>
/// Exact physical integration interval expressed as whole Julian years plus
/// microseconds within the next year. Unlike an absolute floating-point
/// timestamp this retains sub-second resolution even after billions of
/// geological years. Rates use Julian-year seconds, not calendar leap years.
/// </summary>
public readonly record struct PlanetOriginDuration
{
    public const long MicrosecondsPerJulianYear = 31_557_600_000_000L;

    public PlanetOriginDuration(long wholeYears, long remainingMicroseconds = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wholeYears);
        if (remainingMicroseconds < 0 || remainingMicroseconds >= MicrosecondsPerJulianYear)
        {
            throw new ArgumentOutOfRangeException(nameof(remainingMicroseconds),
                "A duration's remaining microseconds must fit within one Julian year.");
        }

        WholeYears = wholeYears;
        RemainingMicroseconds = remainingMicroseconds;
    }

    public long WholeYears { get; }

    public long RemainingMicroseconds { get; }

    public bool IsZero => WholeYears == 0 && RemainingMicroseconds == 0;

    public double TotalSeconds => WholeYears * PlanetThermalEnergyBudget.JulianYearSeconds + RemainingMicroseconds / 1_000_000.0;

    public static PlanetOriginDuration FromYears(long years) => new(years);

    /// <summary>
    /// Converts a bounded sub-year interval into deterministic microsecond
    /// ticks. Values smaller than half a microsecond are rejected rather
    /// than accidentally becoming a zero-duration physical operation.
    /// </summary>
    public static PlanetOriginDuration FromSeconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0.0 || seconds > PlanetThermalEnergyBudget.JulianYearSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "The seconds factory accepts intervals from zero to one Julian year.");
        }

        var microseconds = checked((long)Math.Round(seconds * 1_000_000.0, MidpointRounding.AwayFromZero));
        if (seconds > 0.0 && microseconds == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "A positive physical interval must be at least half a microsecond.");
        }

        return microseconds == MicrosecondsPerJulianYear ? new PlanetOriginDuration(1) : new PlanetOriginDuration(0, microseconds);
    }
}
