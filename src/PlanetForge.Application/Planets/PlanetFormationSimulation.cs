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
    public static PlanetRenderSnapshot Start(PlanetExperience experience, int seed)
    {
        ArgumentNullException.ThrowIfNull(experience);
        experience.SetSeed(seed);
        var solarFlux = PlanetPhysicsCalculator.Calculate(EarthReference.CreateParameters()).SolarFluxWattsPerSquareMeter;
        var funded = PlanetFormationInitialHeat.Create(EarthReference.MeanRadiusMeters, seed);
        var candidate = PlanetRockColumnReference.CreateResolvedInitial(funded, 64, solarFlux);
        _ = experience.PublishRegionalRock(candidate);
        return experience.CreateInitialRenderSnapshot();
    }

    public static PlanetRenderSnapshot Advance(PlanetExperience experience, int seconds, CancellationToken cancellationToken = default)
    {
        var (current, boundary, cap) = Prepare(experience, seconds);
        var candidate = PlanetOriginRockColumnIntegrator.Advance(current, PlanetOriginDuration.FromSeconds(seconds), boundary, new(cap, 20_000), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return experience.PublishRegionalRock(candidate);
    }

    /// <summary>Same accepted substeps and one atomic publication; UI work may run between bounded candidate batches.</summary>
    public static async Task<PlanetRenderSnapshot> AdvanceAsync(PlanetExperience experience, int seconds, CancellationToken cancellationToken = default)
    {
        var (candidate, boundary, cap) = Prepare(experience, seconds);
        var remaining = seconds;
        var batchSeconds = Math.Max(1, (int)cap);
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = Math.Min(remaining, batchSeconds);
            candidate = PlanetOriginRockColumnIntegrator.Advance(candidate, PlanetOriginDuration.FromSeconds(batch), boundary, new(cap, 20_000), cancellationToken);
            remaining -= batch;
            await Task.Delay(1, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return experience.PublishRegionalRock(candidate);
    }

    private static (PlanetOriginCanonicalRockCells World, PlanetOriginRegionalAirlessParameters Boundary, double Cap) Prepare(PlanetExperience experience, int seconds)
    {
        ArgumentNullException.ThrowIfNull(experience);
        if (seconds is < 1 or > 3600) throw new ArgumentOutOfRangeException(nameof(seconds));
        var world = experience.PublishedRegionalRock ?? throw new InvalidOperationException("Start the formation world before advancing it.");
        var elapsed = ElapsedSeconds(world.Epoch.Clock);
        var cap = elapsed < 60.0 ? 0.25 : elapsed < 600.0 ? 1.0 : 5.0;
        return (world, Boundary(experience.CreateSnapshot().Physics.SolarFluxWattsPerSquareMeter), cap);
    }

    public static double ElapsedSeconds(PlanetOriginClock clock) =>
        (clock.YearsSinceSolarSystemFormation - PlanetOriginClock.EarthAccretionReference.YearsSinceSolarSystemFormation) *
        PlanetThermalEnergyBudget.JulianYearSeconds + clock.MicrosecondsIntoYear / 1_000_000.0;

    private static PlanetOriginRegionalAirlessParameters Boundary(double solarFlux) => new(PlanetVector.UnitX, solarFlux, 0.15, 0.9, 3.0, 25.0);
}
