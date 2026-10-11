using PlanetForge.Domain.Hydrology;

namespace PlanetForge.Domain.Climate;

/// <summary>
/// The least H2O ice mass required to cover a physical surface area with an
/// explicitly specified minimum thickness. This is an accounting constraint,
/// not a snow, sea-ice or ice-sheet formation-rate model.
/// </summary>
public readonly record struct PlanetIceCoverageRequirement
{
    public PlanetIceCoverageRequirement(
        double surfaceAreaSquareMeters,
        double coverageFraction,
        double minimumThicknessMeters,
        double iceDensityKilogramsPerCubicMeter)
    {
        if (!double.IsFinite(surfaceAreaSquareMeters) || surfaceAreaSquareMeters < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceAreaSquareMeters), "Physical surface area must be finite and nonnegative.");
        }

        if (!double.IsFinite(coverageFraction) || coverageFraction is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(coverageFraction), "Ice coverage must be a finite fraction between zero and one.");
        }

        if (!double.IsFinite(minimumThicknessMeters) || minimumThicknessMeters < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumThicknessMeters), "Ice thickness must be finite and nonnegative.");
        }

        if (!double.IsFinite(iceDensityKilogramsPerCubicMeter) || iceDensityKilogramsPerCubicMeter <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(iceDensityKilogramsPerCubicMeter), "Ice density must be finite and positive.");
        }

        if (coverageFraction > 0.0 && (surfaceAreaSquareMeters <= 0.0 || minimumThicknessMeters <= 0.0))
        {
            throw new ArgumentException("Positive ice coverage requires a nonzero physical area and thickness.", nameof(coverageFraction));
        }

        SurfaceAreaSquareMeters = surfaceAreaSquareMeters;
        CoverageFraction = coverageFraction;
        MinimumThicknessMeters = minimumThicknessMeters;
        IceDensityKilogramsPerCubicMeter = iceDensityKilogramsPerCubicMeter;

        if (!double.IsFinite(MinimumRequiredMassKilograms))
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceAreaSquareMeters), "The resulting ice mass must remain finite.");
        }
    }

    public double SurfaceAreaSquareMeters { get; }

    public double CoverageFraction { get; }

    public double MinimumThicknessMeters { get; }

    public double IceDensityKilogramsPerCubicMeter { get; }

    public double MinimumRequiredMassKilograms =>
        SurfaceAreaSquareMeters * CoverageFraction * MinimumThicknessMeters * IceDensityKilogramsPerCubicMeter;
}

/// <summary>
/// A necessary mass-conservation condition, not proof that the ice distribution
/// or thermodynamics are possible. The source ice mass is never modified.
/// </summary>
public readonly record struct PlanetCryosphereMassConstraint(
    double AvailableIceMassKilograms,
    double MinimumRequiredMassKilograms)
{
    public bool HasEnoughIceMass => AvailableIceMassKilograms >= MinimumRequiredMassKilograms;

    public double MissingIceMassKilograms => Math.Max(0.0, MinimumRequiredMassKilograms - AvailableIceMassKilograms);
}

public static class PlanetCryosphereMassBudget
{
    public static PlanetCryosphereMassConstraint Evaluate(
        WaterPhaseSnapshot water,
        PlanetIceCoverageRequirement seaIce,
        PlanetIceCoverageRequirement landIce)
    {
        ArgumentNullException.ThrowIfNull(water);
        if (!double.IsFinite(water.IceMassKilograms) || water.IceMassKilograms < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(water), "The planet's existing ice mass must be finite and nonnegative.");
        }

        var requiredMass = seaIce.MinimumRequiredMassKilograms + landIce.MinimumRequiredMassKilograms;
        if (!double.IsFinite(requiredMass))
        {
            throw new ArgumentOutOfRangeException(nameof(landIce), "Total required ice mass must remain finite.");
        }

        return new PlanetCryosphereMassConstraint(water.IceMassKilograms, requiredMass);
    }
}
