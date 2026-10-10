namespace PlanetForge.Domain.Surface;

/// <summary>
/// Absolute geological time, independent of the number of numerical erosion
/// iterations. Zero is the present; positive values are years in the past.
/// Changing epochs does not advance a tectonic or hydrological solver.
/// </summary>
public readonly record struct PlanetGeologicalEpoch
{
    public static PlanetGeologicalEpoch Present { get; } = new(0);

    public PlanetGeologicalEpoch(long yearsBeforePresent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(yearsBeforePresent);
        YearsBeforePresent = yearsBeforePresent;
    }

    public long YearsBeforePresent { get; }

    public override string ToString() => FormattableString.Invariant($"YBP:{YearsBeforePresent}");
}
