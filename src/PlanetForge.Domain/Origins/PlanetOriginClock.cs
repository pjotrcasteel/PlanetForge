using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Origins;

/// <summary>
/// Geological chronology for the Earth-origin reconstruction. The absolute
/// age is stored as whole Julian years plus exact sub-year microseconds, so
/// physical seconds never disappear through floating-point addition to a
/// 4.5-billion-year timestamp. Legacy year/epoch properties remain a coarse
/// whole-year geological view; they must not be used as a process timestep.
/// </summary>
public readonly record struct PlanetOriginClock
{
    public const long SolarSystemReferenceAgeYears = 4_567_000_000L;
    public const long EarthAccretionReferenceAgeYearsBeforePresent = 4_540_000_000L;

    public static PlanetOriginClock SolarSystemFormation => new(0);

    public static PlanetOriginClock EarthAccretionReference =>
        new(SolarSystemReferenceAgeYears - EarthAccretionReferenceAgeYearsBeforePresent);

    public PlanetOriginClock(long yearsSinceSolarSystemFormation)
        : this(yearsSinceSolarSystemFormation, 0)
    {
    }

    public PlanetOriginClock(long yearsSinceSolarSystemFormation, long microsecondsIntoYear)
    {
        if (yearsSinceSolarSystemFormation < 0 || yearsSinceSolarSystemFormation > SolarSystemReferenceAgeYears)
        {
            throw new ArgumentOutOfRangeException(nameof(yearsSinceSolarSystemFormation),
                "The origin timeline must remain between the Solar System reference formation and the present.");
        }

        if (microsecondsIntoYear < 0 || microsecondsIntoYear >= PlanetOriginDuration.MicrosecondsPerJulianYear ||
            (yearsSinceSolarSystemFormation == SolarSystemReferenceAgeYears && microsecondsIntoYear != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(microsecondsIntoYear),
                "The sub-year timestamp must be inside the Julian year and never extend beyond the present.");
        }

        YearsSinceSolarSystemFormation = yearsSinceSolarSystemFormation;
        MicrosecondsIntoYear = microsecondsIntoYear;
    }

    public long YearsSinceSolarSystemFormation { get; }

    public long MicrosecondsIntoYear { get; }

    /// <summary>Whole-year geological label; sub-year precision is retained separately.</summary>
    public long YearsBeforePresent => SolarSystemReferenceAgeYears - YearsSinceSolarSystemFormation;

    /// <summary>Whole-year geography epoch label. Physical operators use this clock, not this truncation.</summary>
    public PlanetGeologicalEpoch GeologicalEpoch => new(YearsBeforePresent);

    public PlanetOriginClock AdvanceYears(long years) => Advance(PlanetOriginDuration.FromYears(years));

    public PlanetOriginClock Advance(PlanetOriginDuration duration)
    {
        var years = checked(YearsSinceSolarSystemFormation + duration.WholeYears);
        var microseconds = MicrosecondsIntoYear + duration.RemainingMicroseconds;
        if (microseconds >= PlanetOriginDuration.MicrosecondsPerJulianYear)
        {
            years = checked(years + 1);
            microseconds -= PlanetOriginDuration.MicrosecondsPerJulianYear;
        }

        return new PlanetOriginClock(years, microseconds);
    }
}
