namespace PlanetForge.Domain.Origins;

/// <summary>
/// Bulk rock properties supplied by a calibrated scenario. One effective
/// solidus/liquidus interval is a first-order material approximation, not a
/// resolved composition-dependent mantle or magma-ocean column.
/// </summary>
public readonly record struct PlanetRockThermalProperties(
    double RockMassKilograms,
    double SpecificHeatCapacityJoulesPerKilogramKelvin,
    double LatentHeatOfFusionJoulesPerKilogram,
    double SolidusTemperatureKelvin,
    double LiquidusTemperatureKelvin)
{
    public void Validate()
    {
        RequirePositive(RockMassKilograms, nameof(RockMassKilograms));
        RequirePositive(SpecificHeatCapacityJoulesPerKilogramKelvin, nameof(SpecificHeatCapacityJoulesPerKilogramKelvin));
        RequirePositive(LatentHeatOfFusionJoulesPerKilogram, nameof(LatentHeatOfFusionJoulesPerKilogram));
        RequirePositive(SolidusTemperatureKelvin, nameof(SolidusTemperatureKelvin));
        RequirePositive(LiquidusTemperatureKelvin, nameof(LiquidusTemperatureKelvin));
        if (LiquidusTemperatureKelvin <= SolidusTemperatureKelvin)
        {
            throw new ArgumentOutOfRangeException(nameof(LiquidusTemperatureKelvin), "Liquidus must exceed solidus.");
        }
    }

    private static void RequirePositive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "A rock property must be finite and positive.");
        }
    }
}

/// <summary>
/// Thermodynamic interpretation of conserved stored energy. MeltFraction is
/// a mass fraction under the assumed effective two-phase linear melt interval.
/// The renderer must not independently invent a melt or solidification event.
/// </summary>
public readonly record struct PlanetRockThermalState(
    double TemperatureKelvin,
    double MeltFraction,
    double StoredEnergyJoules);

/// <summary>
/// Invertible bulk enthalpy relation. Sensible heat uses a constant effective
/// heat capacity, while latent heat is absorbed across [solidus, liquidus].
/// Input parameters are not hard-coded to terrestrial mantle constants.
/// </summary>
public sealed class PlanetRockEnthalpyModel
{
    private readonly PlanetRockThermalProperties properties;
    private readonly double solidusSpecificEnthalpy;
    private readonly double liquidusSpecificEnthalpy;
    private readonly double mushySpecificHeatCapacity;

    public PlanetRockEnthalpyModel(PlanetRockThermalProperties properties)
    {
        properties.Validate();
        this.properties = properties;
        solidusSpecificEnthalpy = properties.SpecificHeatCapacityJoulesPerKilogramKelvin * properties.SolidusTemperatureKelvin;
        liquidusSpecificEnthalpy = properties.SpecificHeatCapacityJoulesPerKilogramKelvin * properties.LiquidusTemperatureKelvin
            + properties.LatentHeatOfFusionJoulesPerKilogram;
        mushySpecificHeatCapacity = properties.SpecificHeatCapacityJoulesPerKilogramKelvin
            + properties.LatentHeatOfFusionJoulesPerKilogram /
            (properties.LiquidusTemperatureKelvin - properties.SolidusTemperatureKelvin);
        if (!double.IsFinite(solidusSpecificEnthalpy) || !double.IsFinite(liquidusSpecificEnthalpy)
            || !double.IsFinite(mushySpecificHeatCapacity))
        {
            throw new ArgumentOutOfRangeException(nameof(properties), "The material's specific enthalpy must remain finite.");
        }
    }

    /// <summary>Conservative lower bound on dH/dT, including when fusion increases effective capacity.</summary>
    public double MinimumHeatCapacityJoulesPerKelvin => properties.RockMassKilograms * properties.SpecificHeatCapacityJoulesPerKilogramKelvin;

    public PlanetRockThermalState Resolve(PlanetThermalEnergyBudget energy) =>
        Resolve(energy?.StoredEnergyJoules ?? throw new ArgumentNullException(nameof(energy)));

    public PlanetRockThermalState Resolve(double storedEnergyJoules)
    {
        ValidateNonnegativeFinite(storedEnergyJoules, nameof(storedEnergyJoules));
        var specificEnthalpy = storedEnergyJoules / properties.RockMassKilograms;
        double temperature;
        double meltFraction;
        if (specificEnthalpy <= solidusSpecificEnthalpy)
        {
            temperature = specificEnthalpy / properties.SpecificHeatCapacityJoulesPerKilogramKelvin;
            meltFraction = 0.0;
        }
        else if (specificEnthalpy < liquidusSpecificEnthalpy)
        {
            temperature = properties.SolidusTemperatureKelvin
                + (specificEnthalpy - solidusSpecificEnthalpy) / mushySpecificHeatCapacity;
            meltFraction = (temperature - properties.SolidusTemperatureKelvin)
                / (properties.LiquidusTemperatureKelvin - properties.SolidusTemperatureKelvin);
        }
        else
        {
            temperature = properties.LiquidusTemperatureKelvin
                + (specificEnthalpy - liquidusSpecificEnthalpy) / properties.SpecificHeatCapacityJoulesPerKilogramKelvin;
            meltFraction = 1.0;
        }

        if (!double.IsFinite(temperature))
        {
            throw new ArgumentOutOfRangeException(nameof(storedEnergyJoules), "The resulting temperature must be finite.");
        }

        return new PlanetRockThermalState(temperature, Math.Clamp(meltFraction, 0.0, 1.0), storedEnergyJoules);
    }

    public double CalculateStoredEnergyJoules(double temperatureKelvin)
    {
        ValidateNonnegativeFinite(temperatureKelvin, nameof(temperatureKelvin));
        var meltFraction = Math.Clamp(
            (temperatureKelvin - properties.SolidusTemperatureKelvin) /
            (properties.LiquidusTemperatureKelvin - properties.SolidusTemperatureKelvin), 0.0, 1.0);
        var specificEnthalpy = properties.SpecificHeatCapacityJoulesPerKilogramKelvin * temperatureKelvin
            + properties.LatentHeatOfFusionJoulesPerKilogram * meltFraction;
        var joules = specificEnthalpy * properties.RockMassKilograms;
        if (!double.IsFinite(joules))
        {
            throw new ArgumentOutOfRangeException(nameof(temperatureKelvin), "The resulting stored energy must be finite.");
        }

        return joules;
    }

    private static void ValidateNonnegativeFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0.0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "A thermal value must be finite and nonnegative.");
        }
    }
}
