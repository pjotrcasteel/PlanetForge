namespace PlanetForge.Domain.Origins;

/// <summary>
/// A read-only trial of one atmospheric longwave exchange against the current
/// conserved sensible-energy stores. This is not an accepted geological epoch,
/// does not advance the clock, and cannot commit a boundary loss to the ledger.
/// </summary>
public sealed record PlanetOriginGreyRadiationTrial(
    PlanetEnvironmentalEnergyBudget Environment,
    PlanetOriginGreyRadiativeFlux Flux,
    double OutgoingInfraredEnergyJoules);

/// <summary>
/// Atomically accounts for surface photons, atmospheric absorption, atmospheric
/// back-radiation and the separate TOA loss. Temperatures are obtained from
/// the same energy/mass ledger, never provided by a shader or external caller.
///
/// This is a proposal operator only. A future coupled energy-step integrator
/// must record OutgoingInfraredEnergyJoules in its external ledger and advance
/// the geological clock before a new epoch can be published.
/// </summary>
public static class PlanetOriginGreyRadiationEnergyOperator
{
    public static PlanetOriginGreyRadiationTrial Propose(
        PlanetOriginEnergyLedger energy, PlanetOriginDuration duration, double planetRadiusMeters, double longwaveOpticalDepth)
    {
        ArgumentNullException.ThrowIfNull(energy);
        if (duration.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Longwave exchange requires positive physical time.");
        }

        var temperatures = energy.Temperatures;
        var surface = temperatures.SurfaceRockTemperatureKelvin ??
            throw new InvalidOperationException("Grey longwave exchange requires a populated surface-rock heat reservoir.");
        var atmosphere = temperatures.AtmosphereTemperatureKelvin ??
            throw new InvalidOperationException("Grey longwave exchange requires an occupied atmospheric heat reservoir.");
        var flux = PlanetOriginGreyRadiativeTransfer.Calculate(
            new PlanetOriginGreyRadiativeColumn(planetRadiusMeters, surface, atmosphere, longwaveOpticalDepth));
        var seconds = duration.TotalSeconds;
        var outgoingJoules = RequireFinite(flux.TotalOutgoingToSpaceWatts * seconds, "outgoing infrared");
        var surfaceCoolingJoules = RequireFinite(flux.SurfaceNetCoolingWatts * seconds, "surface longwave exchange");
        var atmosphericCoolingJoules = RequireFinite(flux.AtmosphereNetCoolingWatts * seconds, "atmospheric longwave exchange");

        // The two signed exchanges must be applied as ONE operation: a cold
        // atmosphere can be warmed by freshly absorbed surface radiation even
        // when it could not pay its emitted photons up front. Never invent a
        // temporary external atmospheric energy reservoir.
        var previous = energy.Environment.Environment;
        var nextSurface = RequireNonnegative(previous.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock) -
            surfaceCoolingJoules, "surface rock");
        var nextAtmosphere = RequireNonnegative(previous.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere) -
            atmosphericCoolingJoules, "atmosphere");
        var updated = new PlanetEnvironmentalEnergyBudget(
            nextAtmosphere,
            previous.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean),
            nextSurface,
            previous.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceIce));

        // An accepted trial can update BOTH stores only. Liquid/ice sensible
        // energy, interior rock enthalpy and the H2O inventory are untouched.
        return new PlanetOriginGreyRadiationTrial(updated, flux, outgoingJoules);
    }

    private static double RequireFinite(double joules, string name)
    {
        if (!double.IsFinite(joules))
        {
            throw new ArgumentOutOfRangeException(name, "Radiative energy must remain finite.");
        }

        return joules;
    }

    private static double RequireNonnegative(double joules, string reservoir)
    {
        if (!double.IsFinite(joules) || joules < 0.0)
        {
            throw new InvalidOperationException($"The proposed longwave step overdraws the {reservoir} heat reservoir.");
        }

        return joules;
    }
}
