namespace PlanetForge.Domain.Origins;

/// <summary>
/// Configurable effective properties for a zero-K-reference sensible-energy
/// approximation. These properties are NOT Earth-history calibrations.
/// In particular, noncondensable atmosphere and active surface rock masses
/// must be provided by a future planetary-state model.
/// </summary>
public readonly record struct PlanetEnvironmentHeatCapacityParameters(
    double NoncondensableAtmosphereMassKilograms,
    double NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin,
    double VaporSpecificHeatJoulesPerKilogramKelvin,
    double LiquidWaterSpecificHeatJoulesPerKilogramKelvin,
    double SurfaceRockMassKilograms,
    double SurfaceRockSpecificHeatJoulesPerKilogramKelvin)
{
    public void Validate()
    {
        RequireNonnegative(NoncondensableAtmosphereMassKilograms, nameof(NoncondensableAtmosphereMassKilograms));
        RequireNonnegative(SurfaceRockMassKilograms, nameof(SurfaceRockMassKilograms));
        RequirePositive(NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin, nameof(NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin));
        RequirePositive(VaporSpecificHeatJoulesPerKilogramKelvin, nameof(VaporSpecificHeatJoulesPerKilogramKelvin));
        RequirePositive(LiquidWaterSpecificHeatJoulesPerKilogramKelvin, nameof(LiquidWaterSpecificHeatJoulesPerKilogramKelvin));
        RequirePositive(SurfaceRockSpecificHeatJoulesPerKilogramKelvin, nameof(SurfaceRockSpecificHeatJoulesPerKilogramKelvin));
    }

    private static void RequireNonnegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0.0)
        {
            throw new ArgumentOutOfRangeException(name, "Effective material mass must be finite and nonnegative.");
        }
    }

    private static void RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(name, "Specific heat capacity must be finite and positive.");
        }
    }
}

/// <summary>
/// Temperatures are only defined for stores with a positive effective heat
/// capacity. A null means the corresponding store contains no modelled matter.
/// These are bulk effective temperatures, NOT atmospheric lapse profiles,
/// local sea-surface temperatures or estimates of the full mantle.
/// </summary>
public readonly record struct PlanetEnvironmentTemperatureSnapshot(
    double AtmosphereHeatCapacityJoulesPerKelvin,
    double OceanHeatCapacityJoulesPerKelvin,
    double SurfaceRockHeatCapacityJoulesPerKelvin,
    double? AtmosphereTemperatureKelvin,
    double? OceanTemperatureKelvin,
    double? SurfaceRockTemperatureKelvin);

/// <summary>
/// Pure diagnostic conversion of conserved environmental sensible energy to
/// bulk temperature using explicit masses and effective specific heat.
/// No water is moved and no heat is added. Latent energy is tracked separately
/// in PlanetWaterEnvironmentalState. This zero-K reference is deliberately
/// limited: phase transitions still require compatible full enthalpy, pressure,
/// compositional and transport models before thermal evolution is automated.
/// </summary>
public static class PlanetEnvironmentTemperatureModel
{
    public static PlanetEnvironmentTemperatureSnapshot Resolve(
        PlanetWaterEnvironmentalState state, PlanetEnvironmentHeatCapacityParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(state);
        parameters.Validate();

        var atmosphereCapacity = CheckedCapacity(
            state.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor),
            parameters.VaporSpecificHeatJoulesPerKilogramKelvin);
        var dryCapacity = CheckedCapacity(
            parameters.NoncondensableAtmosphereMassKilograms,
            parameters.NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin);
        atmosphereCapacity = CheckedSum(atmosphereCapacity, dryCapacity);

        var oceanCapacity = CheckedCapacity(
            state.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid),
            parameters.LiquidWaterSpecificHeatJoulesPerKilogramKelvin);
        var rockCapacity = CheckedCapacity(
            parameters.SurfaceRockMassKilograms,
            parameters.SurfaceRockSpecificHeatJoulesPerKilogramKelvin);

        return new PlanetEnvironmentTemperatureSnapshot(
            atmosphereCapacity, oceanCapacity, rockCapacity,
            ResolveTemperature(state.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), atmosphereCapacity),
            ResolveTemperature(state.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean), oceanCapacity),
            ResolveTemperature(state.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock), rockCapacity));
    }

    private static double CheckedCapacity(double mass, double specificHeat)
    {
        var capacity = mass * specificHeat;
        if (!double.IsFinite(capacity))
        {
            throw new ArgumentOutOfRangeException(nameof(mass), "The effective bulk heat capacity exceeds finite range.");
        }

        return capacity;
    }

    private static double CheckedSum(double first, double second)
    {
        var total = first + second;
        if (!double.IsFinite(total))
        {
            throw new ArgumentOutOfRangeException(nameof(second), "Combined atmospheric heat capacity exceeds finite range.");
        }

        return total;
    }

    private static double? ResolveTemperature(double joules, double capacityJoulesPerKelvin)
    {
        if (capacityJoulesPerKelvin == 0.0)
        {
            if (joules != 0.0)
            {
                throw new InvalidOperationException("An unoccupied thermal reservoir cannot contain sensible energy.");
            }

            return null;
        }

        var kelvin = joules / capacityJoulesPerKelvin;
        if (!double.IsFinite(kelvin))
        {
            throw new InvalidOperationException("An effective temperature must remain finite.");
        }

        return kelvin;
    }
}
