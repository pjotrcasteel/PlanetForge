namespace PlanetForge.Domain.Origins;

/// <summary>
/// The deliberately limited radiative boundary for a bare, airless rocky world.
/// This has no greenhouse, atmospheric emission altitude or steam-opacity closure.
/// </summary>
public readonly record struct PlanetOriginAirlessRadiationParameters(
    double PlanetRadiusMeters,
    double StellarIrradianceWattsPerSquareMeter,
    double BondAlbedo,
    double SurfaceInfraredEmissivity)
{
    public void Validate()
    {
        RequirePositive(PlanetRadiusMeters, nameof(PlanetRadiusMeters));
        RequireNonnegative(StellarIrradianceWattsPerSquareMeter, nameof(StellarIrradianceWattsPerSquareMeter));
        RequireFraction(BondAlbedo, nameof(BondAlbedo));
        RequireFraction(SurfaceInfraredEmissivity, nameof(SurfaceInfraredEmissivity));
    }

    private static void RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(name, "Airless planet radius must be finite and positive.");
        }
    }

    private static void RequireNonnegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0.0)
        {
            throw new ArgumentOutOfRangeException(name, "Airless stellar forcing must be finite and nonnegative.");
        }
    }

    private static void RequireFraction(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(name, "Airless radiative properties must be fractions between zero and one.");
        }
    }
}

/// <summary>
/// Fourier conduction across one prescribed effective boundary thickness.
/// This is a model parameter, not a claimed measured crust thickness or map.
/// </summary>
public readonly record struct PlanetOriginRockConductionParameters(
    double ConductivityWattsPerMeterKelvin,
    double EffectiveBoundaryThicknessMeters)
{
    public void Validate()
    {
        if (!double.IsFinite(ConductivityWattsPerMeterKelvin) || ConductivityWattsPerMeterKelvin < 0.0 ||
            !double.IsFinite(EffectiveBoundaryThicknessMeters) || EffectiveBoundaryThicknessMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(EffectiveBoundaryThicknessMeters),
                "Conduction requires nonnegative finite conductivity and positive finite effective thickness.");
        }
    }
}

/// <summary>
/// Explicit physical boundary and external heat sources. All coefficients are
/// caller-supplied; no universal Hadean cooling time or opacity is assumed.
/// </summary>
public readonly record struct PlanetOriginAirlessThermalParameters(
    PlanetOriginAirlessRadiationParameters Radiation,
    PlanetOriginRockConductionParameters Conduction,
    double ImpactHeatingWatts = 0.0,
    double RadiogenicHeatingWatts = 0.0)
{
    public void Validate()
    {
        Radiation.Validate();
        Conduction.Validate();
        if (!double.IsFinite(ImpactHeatingWatts) || ImpactHeatingWatts < 0.0 ||
            !double.IsFinite(RadiogenicHeatingWatts) || RadiogenicHeatingWatts < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(ImpactHeatingWatts),
                "External energy sources must be nonnegative finite powers.");
        }
    }
}

/// <summary>
/// First law-conserving thermal process for a genuinely *airless*, dry rock
/// reference world. The interior supplies heat across an effective crust
/// boundary by Fourier conduction and the actual surface store radiates with
/// Stefan-Boltzmann. It is unsuitable for steam/CO2 atmospheres and must never
/// be silently reused as a Hadean atmospheric cooling or rainfall model.
/// </summary>
public static class PlanetOriginAirlessThermalProcess
{
    public static PlanetOriginEnergyStep Propose(
        PlanetOriginEnergyLedger energy,
        PlanetOriginDuration duration,
        PlanetOriginAirlessThermalParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(energy);
        parameters.Validate();
        if (duration.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "A thermal proposal must advance physical time.");
        }

        RequireDryAirlessSurface(energy);
        var surface = energy.Temperatures.SurfaceRockTemperatureKelvin ??
            throw new InvalidOperationException("A dry rock cooling surface needs a populated surface-rock heat store.");
        var interior = energy.Interior.Rock.TemperatureKelvin;
        if (parameters.Conduction.ConductivityWattsPerMeterKelvin > 0.0 && surface > interior)
        {
            throw new InvalidOperationException(
                "Reverse heat transfer from hotter surface rock to the interior is unsupported by the one-way coupled heat ledger.");
        }

        var area = 4.0 * Math.PI * parameters.Radiation.PlanetRadiusMeters * parameters.Radiation.PlanetRadiusMeters;
        var heatFlux = parameters.Conduction.ConductivityWattsPerMeterKelvin *
            Math.Max(interior - surface, 0.0) / parameters.Conduction.EffectiveBoundaryThicknessMeters;
        var interiorExportWatts = area * heatFlux;
        if (!double.IsFinite(area) || !double.IsFinite(heatFlux) || !double.IsFinite(interiorExportWatts))
        {
            throw new ArgumentOutOfRangeException(nameof(parameters),
                "Conductive heat flux must remain within finite physical range.");
        }

        var boundary = PlanetRadiationBalanceModel.Calculate(new PlanetRadiationBoundaryConditions(
            parameters.Radiation.PlanetRadiusMeters,
            parameters.Radiation.StellarIrradianceWattsPerSquareMeter,
            parameters.Radiation.BondAlbedo,
            parameters.Radiation.SurfaceInfraredEmissivity,
            surface));

        return new PlanetOriginEnergyStep(
            duration,
            parameters.ImpactHeatingWatts,
            parameters.RadiogenicHeatingWatts,
            new PlanetInteriorHeatExport(interiorExportWatts, PlanetEnvironmentHeatReservoir.SurfaceRock),
            new PlanetRadiativeExchange(boundary, PlanetEnvironmentHeatReservoir.SurfaceRock,
                PlanetEnvironmentHeatReservoir.SurfaceRock),
            []);
    }

    private static void RequireDryAirlessSurface(PlanetOriginEnergyLedger energy)
    {
        var water = energy.Environment.Water;
        if (energy.HeatCapacities.NoncondensableAtmosphereMassKilograms != 0.0 ||
            water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor) != 0.0 ||
            water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid) != 0.0 ||
            water.GetMassKilograms(PlanetWaterReservoir.SurfaceIce) != 0.0)
        {
            throw new InvalidOperationException(
                "Airless rock radiation cannot stand in for atmospheric, steam, ocean or ice thermodynamics.");
        }
    }
}
