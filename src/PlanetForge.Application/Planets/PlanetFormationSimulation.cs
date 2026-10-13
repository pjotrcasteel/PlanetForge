using PlanetForge.Application.Rendering;
using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Physics;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Planets;

/// <summary>
/// One formation world owned by PlanetExperience, starting with funded molten rock.
/// This initial vertical slice is dry conductive cooling; later atmosphere/material/ecology must extend this same state.
/// No old climate mission, scripted era transition or independent formation clock is advanced.
/// </summary>
public static class PlanetFormationSimulation
{
    public const double InitialThermalContrastFraction = 0.25;

    public static PlanetRenderSnapshot Start(PlanetExperience experience, int seed)
    {
        ArgumentNullException.ThrowIfNull(experience);
        experience.SetSeed(seed);
        var solarFlux = PlanetPhysicsCalculator.Calculate(EarthReference.CreateParameters()).SolarFluxWattsPerSquareMeter;
        var candidate = PlanetRockColumnReference.CreateResolvedInitial(EarthReference.MeanRadiusMeters, 64, solarFlux, InitialThermalContrastFraction);
        _ = experience.PublishRegionalRock(candidate);
        return experience.CreateInitialRenderSnapshot();
    }

    public static PlanetRenderSnapshot Advance(PlanetExperience experience, int seconds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experience);
        if (seconds is < 1 or > 3600) throw new ArgumentOutOfRangeException(nameof(seconds));
        var current = experience.PublishedRegionalRock ?? throw new InvalidOperationException("Start the formation world before advancing it.");
        var elapsed = ElapsedSeconds(current.Epoch.Clock);
        // Early surface freezing is a fast transient. Do not use the previous one-hour/30-second reference step to skip it.
        var cap = elapsed < 60.0 ? 0.25 : elapsed < 600.0 ? 1.0 : 5.0;
        var candidate = PlanetOriginRockColumnIntegrator.Advance(current, PlanetOriginDuration.FromSeconds(seconds),
            Boundary(experience.CreateSnapshot().Physics.SolarFluxWattsPerSquareMeter), new(cap, 20_000), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return experience.PublishRegionalRock(candidate);
    }

    public static double ElapsedSeconds(PlanetOriginClock clock) =>
        (clock.YearsSinceSolarSystemFormation - PlanetOriginClock.EarthAccretionReference.YearsSinceSolarSystemFormation) *
        PlanetThermalEnergyBudget.JulianYearSeconds + clock.MicrosecondsIntoYear / 1_000_000.0;

    private static PlanetOriginRegionalAirlessParameters Boundary(double solarFlux) => new(PlanetVector.UnitX, solarFlux, 0.15, 0.9, 3.0, 25.0);
}
