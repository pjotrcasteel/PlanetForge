using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Planets;

namespace PlanetForge.Domain.Climate;

public static class ClimateFeedbackSimulator
{
    private const double CryosphereColdLimitKelvin = 255.0;
    private const double CryosphereWarmLimitKelvin = 285.0;
    private const double MaximumIceAlbedoContribution = 0.28;
    private const double MaximumEffectiveBondAlbedo = 0.90;
    private const double ThermalResponseTimeYears = 12.0;
    private const double CryosphereResponseTimeYears = 8.0;
    private const double MaximumAdvanceYears = 500.0;

    public static ClimateFeedbackState Initialize(PlanetPhysicalParameters planet, AtmosphereSnapshot atmosphere, WaterParameters water)
    {
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(atmosphere);
        ArgumentNullException.ThrowIfNull(water);

        var physics = PlanetPhysicsCalculator.Calculate(planet);
        var climate = SurfaceClimateCalculator.Calculate(physics, atmosphere);
        var cryosphereFraction = HasWater(water) ? EstimateCryosphereFraction(climate.SurfaceTemperatureKelvin) : 0.0;
        var effectiveBondAlbedo = CalculateEffectiveBondAlbedo(planet.BondAlbedo, cryosphereFraction);
        return new ClimateFeedbackState(0.0, climate.SurfaceTemperatureKelvin, cryosphereFraction, effectiveBondAlbedo);
    }

    public static ClimateFeedbackState Advance(
        PlanetPhysicalParameters planet,
        AtmosphereSnapshot atmosphere,
        WaterParameters water,
        ClimateFeedbackState state,
        double years)
    {
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(atmosphere);
        ArgumentNullException.ThrowIfNull(water);
        ArgumentNullException.ThrowIfNull(state);
        ValidateYears(years);

        var remainingYears = years;
        var current = state;
        while (remainingYears > 0.0)
        {
            var stepYears = Math.Min(1.0, remainingYears);
            current = AdvanceStep(planet, atmosphere, water, current, stepYears);
            remainingYears -= stepYears;
        }

        return current;
    }

    public static ClimateFeedbackResult Evaluate(
        PlanetPhysicalParameters planet,
        AtmosphereSnapshot atmosphere,
        WaterParameters water,
        ClimateFeedbackState state)
    {
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(atmosphere);
        ArgumentNullException.ThrowIfNull(water);
        ArgumentNullException.ThrowIfNull(state);

        var effectiveBondAlbedo = CalculateEffectiveBondAlbedo(planet.BondAlbedo, HasWater(water) ? state.CryosphereFraction : 0.0);
        var effectivePlanet = planet with { BondAlbedo = effectiveBondAlbedo };
        var physics = PlanetPhysicsCalculator.Calculate(effectivePlanet);
        var targetClimate = SurfaceClimateCalculator.Calculate(physics, atmosphere);
        var climate = new ClimateSnapshot(
            Math.Max(2.7, state.SurfaceTemperatureKelvin),
            targetClimate.BackgroundGreenhouseWarmingKelvin,
            targetClimate.CarbonDioxideWarmingKelvin);
        var waterPhase = WaterPhaseCalculator.Calculate(water, climate.SurfaceTemperatureKelvin, atmosphere.SurfacePressurePascals);
        var feedback = new ClimateFeedbackSnapshot(
            state.ElapsedYears,
            targetClimate.SurfaceTemperatureKelvin,
            HasWater(water) ? state.CryosphereFraction : 0.0,
            planet.BondAlbedo,
            effectiveBondAlbedo,
            effectiveBondAlbedo - planet.BondAlbedo);

        return new ClimateFeedbackResult(state with { EffectiveBondAlbedo = effectiveBondAlbedo }, physics, climate, waterPhase, feedback);
    }

    private static ClimateFeedbackState AdvanceStep(
        PlanetPhysicalParameters planet,
        AtmosphereSnapshot atmosphere,
        WaterParameters water,
        ClimateFeedbackState state,
        double stepYears)
    {
        var cryosphereTarget = HasWater(water) ? EstimateCryosphereFraction(state.SurfaceTemperatureKelvin) : 0.0;
        var cryosphereResponse = 1.0 - Math.Exp(-stepYears / CryosphereResponseTimeYears);
        var cryosphereFraction = state.CryosphereFraction + ((cryosphereTarget - state.CryosphereFraction) * cryosphereResponse);
        var effectiveBondAlbedo = CalculateEffectiveBondAlbedo(planet.BondAlbedo, cryosphereFraction);
        var effectivePlanet = planet with { BondAlbedo = effectiveBondAlbedo };
        var targetPhysics = PlanetPhysicsCalculator.Calculate(effectivePlanet);
        var targetClimate = SurfaceClimateCalculator.Calculate(targetPhysics, atmosphere);
        var thermalResponse = 1.0 - Math.Exp(-stepYears / ThermalResponseTimeYears);
        var surfaceTemperature = state.SurfaceTemperatureKelvin
            + ((targetClimate.SurfaceTemperatureKelvin - state.SurfaceTemperatureKelvin) * thermalResponse);

        return new ClimateFeedbackState(
            state.ElapsedYears + stepYears,
            Math.Max(2.7, surfaceTemperature),
            Math.Clamp(cryosphereFraction, 0.0, 1.0),
            effectiveBondAlbedo);
    }

    private static double EstimateCryosphereFraction(double surfaceTemperatureKelvin)
    {
        var t = Math.Clamp(
            (surfaceTemperatureKelvin - CryosphereColdLimitKelvin) / (CryosphereWarmLimitKelvin - CryosphereColdLimitKelvin),
            0.0,
            1.0);
        var smooth = t * t * (3.0 - (2.0 * t));
        return 1.0 - smooth;
    }

    private static double CalculateEffectiveBondAlbedo(double baseBondAlbedo, double cryosphereFraction)
        => Math.Clamp(baseBondAlbedo + (MaximumIceAlbedoContribution * cryosphereFraction), 0.0, MaximumEffectiveBondAlbedo);

    private static bool HasWater(WaterParameters water) => water.TotalMassKilograms > 0.0;

    private static void ValidateYears(double years)
    {
        if (!double.IsFinite(years) || years <= 0.0 || years > MaximumAdvanceYears)
        {
            throw new ArgumentOutOfRangeException(nameof(years), years, $"Simulation years must be finite and between zero and {MaximumAdvanceYears}.");
        }
    }
}