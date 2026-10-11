using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Origins;

/// <summary>
/// Geological chronology for the Earth-origin reconstruction. This clock is
/// not the year counter of an intervention mission or a terrain solver step.
/// The reference ages are rounded estimates, not exact formation timestamps.
/// </summary>
public readonly record struct PlanetOriginClock
{
    public const long SolarSystemReferenceAgeYears = 4_567_000_000L;
    public const long EarthAccretionReferenceAgeYearsBeforePresent = 4_540_000_000L;

    public static PlanetOriginClock SolarSystemFormation => new(0);

    public static PlanetOriginClock EarthAccretionReference =>
        new(SolarSystemReferenceAgeYears - EarthAccretionReferenceAgeYearsBeforePresent);

    public PlanetOriginClock(long yearsSinceSolarSystemFormation)
    {
        if (yearsSinceSolarSystemFormation < 0 || yearsSinceSolarSystemFormation > SolarSystemReferenceAgeYears)
        {
            throw new ArgumentOutOfRangeException(nameof(yearsSinceSolarSystemFormation),
                "The origin timeline must remain between the Solar System reference formation and the present.");
        }

        YearsSinceSolarSystemFormation = yearsSinceSolarSystemFormation;
    }

    public long YearsSinceSolarSystemFormation { get; }

    public long YearsBeforePresent => SolarSystemReferenceAgeYears - YearsSinceSolarSystemFormation;

    public PlanetGeologicalEpoch GeologicalEpoch => new(YearsBeforePresent);

    public PlanetOriginClock AdvanceYears(long years)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(years);
        return new PlanetOriginClock(checked(YearsSinceSolarSystemFormation + years));
    }
}
