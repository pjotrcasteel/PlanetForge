namespace PlanetForge.Domain.Origins;

/// <summary>
/// Radiation boundary inputs for a simplified globally averaged sphere.
/// Solar irradiance is measured normal to the incoming stellar beam at the
/// planet's orbit. Effective emission temperature belongs to the emitting
/// layer, not necessarily the ground/ocean or atmosphere's bulk temperature.
/// </summary>
public readonly record struct PlanetRadiationBoundaryConditions(
    double PlanetRadiusMeters,
    double StellarIrradianceWattsPerSquareMeter,
    double BondAlbedo,
    double EffectiveInfraredEmissivity,
    double EffectiveEmissionTemperatureKelvin)
{
    public void Validate()
    {
        RequirePositive(PlanetRadiusMeters, nameof(PlanetRadiusMeters));
        RequireNonnegative(StellarIrradianceWattsPerSquareMeter, nameof(StellarIrradianceWattsPerSquareMeter));
        RequireFraction(BondAlbedo, nameof(BondAlbedo));
        RequireFraction(EffectiveInfraredEmissivity, nameof(EffectiveInfraredEmissivity));
        RequireNonnegative(EffectiveEmissionTemperatureKelvin, nameof(EffectiveEmissionTemperatureKelvin));
    }

    private static void RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(name, "Physical radius must be finite and positive.");
        }
    }

    private static void RequireNonnegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0.0)
        {
            throw new ArgumentOutOfRangeException(name, "Radiative parameter must be finite and nonnegative.");
        }
    }

    private static void RequireFraction(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(name, "Radiative fraction must be between zero and one.");
        }
    }
}

/// <summary>
/// Power absorbed from the host star and radiated to space, including
/// surface-area scaling. Positive net watts warm the planet's external energy
/// accounts; negative net watts cool them. This is NOT an atmosphere/greenhouse
/// transport solver, nor a complete geothermal/radiogenic heat budget.
/// </summary>
public readonly record struct PlanetRadiativeBalance(
    double PlanetSurfaceAreaSquareMeters,
    double AbsorbedFluxWattsPerSquareMeter,
    double EmittedFluxWattsPerSquareMeter,
    double AbsorbedStellarPowerWatts,
    double EmittedThermalPowerWatts)
{
    public double NetPowerWatts => AbsorbedStellarPowerWatts - EmittedThermalPowerWatts;
}

/// <summary>
/// Stefan-Boltzmann radiative boundary for a spherical planet, with solar
/// geometric cross-section pi*R² and emission area 4*pi*R².
/// The emitting temperature, Bond albedo and emissivity must be supplied by
/// a calibrated atmosphere, stellar and climate model. No time advancement
/// or energy transfer happens inside this diagnostic.
/// </summary>
public static class PlanetRadiationBalanceModel
{
    public const double StefanBoltzmannWattsPerSquareMeterKelvinFourth = 5.670374419e-8;

    public static PlanetRadiativeBalance Calculate(PlanetRadiationBoundaryConditions conditions)
    {
        conditions.Validate();

        var sphereArea = 4.0 * Math.PI * conditions.PlanetRadiusMeters * conditions.PlanetRadiusMeters;
        var absorbedFlux = conditions.StellarIrradianceWattsPerSquareMeter * (1.0 - conditions.BondAlbedo) / 4.0;
        var temperatureSquared = conditions.EffectiveEmissionTemperatureKelvin * conditions.EffectiveEmissionTemperatureKelvin;
        var fourthPower = temperatureSquared * temperatureSquared;
        var emittedFlux = conditions.EffectiveInfraredEmissivity * StefanBoltzmannWattsPerSquareMeterKelvinFourth * fourthPower;
        var absorbedPower = sphereArea * absorbedFlux;
        var emittedPower = sphereArea * emittedFlux;

        if (!double.IsFinite(sphereArea) || sphereArea <= 0.0 || !double.IsFinite(absorbedFlux) ||
            !double.IsFinite(emittedFlux) || !double.IsFinite(absorbedPower) || !double.IsFinite(emittedPower) ||
            !double.IsFinite(absorbedPower - emittedPower))
        {
            throw new ArgumentOutOfRangeException(nameof(conditions), "Radiative areas, fluxes and net power must remain finite.");
        }

        return new PlanetRadiativeBalance(sphereArea, absorbedFlux, emittedFlux, absorbedPower, emittedPower);
    }
}
