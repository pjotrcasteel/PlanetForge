namespace PlanetForge.Domain.Origins;

/// <summary>
/// Explicitly parameterized first-order magma/interior-to-environment and
/// grey-body radiative closure. Coefficients require scenario calibration:
/// this is not a Hadean greenhouse, convection, or steam-opacity solver.
/// </summary>
public readonly record struct PlanetOriginThermalBoundaryParameters(
    double RadiusMeters,
    double StellarIrradianceWattsPerSquareMeter,
    double BondAlbedo,
    double EffectiveInfraredEmissivity,
    double EffectiveEmissionTemperatureRatio,
    double InteriorHeatTransferWattsPerSquareMeterKelvin,
    double ImpactHeatingWatts = 0.0,
    double RadiogenicHeatingWatts = 0.0)
{
    public void Validate()
    {
        RequirePositive(RadiusMeters, nameof(RadiusMeters));
        RequireNonnegative(StellarIrradianceWattsPerSquareMeter, nameof(StellarIrradianceWattsPerSquareMeter));
        RequireFraction(BondAlbedo, nameof(BondAlbedo));
        RequireFraction(EffectiveInfraredEmissivity, nameof(EffectiveInfraredEmissivity));
        RequirePositive(EffectiveEmissionTemperatureRatio, nameof(EffectiveEmissionTemperatureRatio));
        if (EffectiveEmissionTemperatureRatio > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(EffectiveEmissionTemperatureRatio),
                "The current grey-body closure supports emission temperatures at or below the well-mixed material temperature.");
        }

        RequireNonnegative(InteriorHeatTransferWattsPerSquareMeterKelvin, nameof(InteriorHeatTransferWattsPerSquareMeterKelvin));
        RequireNonnegative(ImpactHeatingWatts, nameof(ImpactHeatingWatts));
        RequireNonnegative(RadiogenicHeatingWatts, nameof(RadiogenicHeatingWatts));
    }

    private static void RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(name, "A thermal parameter must be finite and positive.");
        }
    }

    private static void RequireNonnegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0.0)
        {
            throw new ArgumentOutOfRangeException(name, "A thermal rate must be finite and nonnegative.");
        }
    }

    private static void RequireFraction(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(name, "A thermal fraction must lie between zero and one.");
        }
    }
}

/// <summary>
/// Proposes one conservative, state-dependent thermal step. Its rates are
/// re-evaluated on every adaptive trial. Rock enthalpy controls melting; the
/// environmental heat store owns both incoming interior heat and radiation.
/// Never invents rain, steam, solidification dates or additional water.
/// </summary>
public static class PlanetOriginThermalProcess
{
    public static PlanetOriginEnergyStep Propose(
        PlanetOriginEnergyLedger energy,
        PlanetOriginDuration duration,
        PlanetOriginThermalBoundaryParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(energy);
        parameters.Validate();
        if (duration.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "A thermal process requires a positive physical interval.");
        }

        var temperatures = energy.Temperatures;
        var hasAtmosphere = temperatures.AtmosphereTemperatureKelvin.HasValue;
        var reservoir = hasAtmosphere ? PlanetEnvironmentHeatReservoir.Atmosphere : PlanetEnvironmentHeatReservoir.SurfaceRock;
        var environmentTemperature = temperatures.AtmosphereTemperatureKelvin ?? temperatures.SurfaceRockTemperatureKelvin ??
            throw new InvalidOperationException("Thermal evolution requires an occupied atmosphere or active surface-rock store.");
        var rockTemperature = energy.Interior.Rock.TemperatureKelvin;
        var surfaceArea = 4.0 * Math.PI * parameters.RadiusMeters * parameters.RadiusMeters;

        // Interior heat is an internal debit/credit, not an additional loss
        // to space. An adaptive rejection handles source-energy overdraw.
        var heatExportWatts = surfaceArea * parameters.InteriorHeatTransferWattsPerSquareMeterKelvin *
            Math.Max(rockTemperature - environmentTemperature, 0.0);
        if (!double.IsFinite(heatExportWatts))
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "Interior-to-environment thermal power must remain finite.");
        }

        var effectiveEmissionTemperature = environmentTemperature * parameters.EffectiveEmissionTemperatureRatio;
        var radiation = PlanetRadiationBalanceModel.Calculate(new PlanetRadiationBoundaryConditions(
            parameters.RadiusMeters, parameters.StellarIrradianceWattsPerSquareMeter,
            parameters.BondAlbedo, parameters.EffectiveInfraredEmissivity, effectiveEmissionTemperature));

        return new PlanetOriginEnergyStep(duration, parameters.ImpactHeatingWatts, parameters.RadiogenicHeatingWatts,
            new PlanetInteriorHeatExport(heatExportWatts, reservoir),
            new PlanetRadiativeExchange(radiation, reservoir, reservoir), []);
    }
}
