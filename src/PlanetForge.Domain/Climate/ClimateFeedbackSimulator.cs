using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Physics;
using PlanetForge.Domain.Planets;

namespace PlanetForge.Domain.Climate;

public static class ClimateFeedbackSimulator
{
    private const double SeaIceColdLimitKelvin = 250.0;
    private const double SeaIceWarmLimitKelvin = 278.0;
    private const double LandIceColdLimitKelvin = 245.0;
    private const double LandIceWarmLimitKelvin = 275.0;
    private const double SnowColdLimitKelvin = 255.0;
    private const double SnowWarmLimitKelvin = 276.0;
    private const double MaximumIceAlbedoContribution = 0.28;
    private const double MaximumEffectiveBondAlbedo = 0.90;
    private const double ThermalResponseTimeYears = 12.0;
    private const double SeaIceResponseTimeYears = 7.0;
    private const double LandIceResponseTimeYears = 55.0;
    private const double SnowResponseTimeYears = 3.0;
    private const double MaximumAdvanceYears = 500.0;
    private const double SeaIceWeight = 0.50;
    private const double LandIceWeight = 0.40;
    private const double SnowCoverWeight = 0.10;

    public static ClimateFeedbackState Initialize(PlanetPhysicalParameters planet, AtmosphereSnapshot atmosphere, WaterParameters water)
    {
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(atmosphere);
        ArgumentNullException.ThrowIfNull(water);

        var physics = PlanetPhysicsCalculator.Calculate(planet);
        var climate = SurfaceClimateCalculator.Calculate(physics, atmosphere);
        var seaIceFraction = HasWater(water) ? EstimateCoverage(climate.SurfaceTemperatureKelvin, SeaIceColdLimitKelvin, SeaIceWarmLimitKelvin) : 0.0;
        var landIceFraction = HasWater(water) ? EstimateCoverage(climate.SurfaceTemperatureKelvin, LandIceColdLimitKelvin, LandIceWarmLimitKelvin) : 0.0;
        var snowCoverFraction = HasWater(water) ? EstimateCoverage(climate.SurfaceTemperatureKelvin, SnowColdLimitKelvin, SnowWarmLimitKelvin) : 0.0;
        var cryosphereFraction = CombineCryosphere(seaIceFraction, landIceFraction, snowCoverFraction);
        var effectiveBondAlbedo = CalculateEffectiveBondAlbedo(planet.BondAlbedo, cryosphereFraction);
        return new ClimateFeedbackState(0.0, climate.SurfaceTemperatureKelvin, cryosphereFraction, effectiveBondAlbedo)
        {
            SeaIceFraction = seaIceFraction,
            LandIceFraction = landIceFraction,
            SnowCoverFraction = snowCoverFraction,
        };
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

        var cryosphereFraction = HasWater(water)
            ? CombineCryosphere(state.SeaIceFraction, state.LandIceFraction, state.SnowCoverFraction)
            : 0.0;
        var effectiveBondAlbedo = CalculateEffectiveBondAlbedo(planet.BondAlbedo, cryosphereFraction);
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
            cryosphereFraction,
            planet.BondAlbedo,
            effectiveBondAlbedo,
            effectiveBondAlbedo - planet.BondAlbedo)
        {
            SeaIceFraction = HasWater(water) ? state.SeaIceFraction : 0.0,
            LandIceFraction = HasWater(water) ? state.LandIceFraction : 0.0,
            SnowCoverFraction = HasWater(water) ? state.SnowCoverFraction : 0.0,
        };

        var updatedState = state with { CryosphereFraction = cryosphereFraction, EffectiveBondAlbedo = effectiveBondAlbedo };
        return new ClimateFeedbackResult(updatedState, physics, climate, waterPhase, feedback);
    }

    private static ClimateFeedbackState AdvanceStep(
        PlanetPhysicalParameters planet,
        AtmosphereSnapshot atmosphere,
        WaterParameters water,
        ClimateFeedbackState state,
        double stepYears)
    {
        var hasWater = HasWater(water);
        var seaIceTarget = hasWater ? EstimateCoverage(state.SurfaceTemperatureKelvin, SeaIceColdLimitKelvin, SeaIceWarmLimitKelvin) : 0.0;
        var landIceTarget = hasWater ? EstimateCoverage(state.SurfaceTemperatureKelvin, LandIceColdLimitKelvin, LandIceWarmLimitKelvin) : 0.0;
        var snowTarget = hasWater ? EstimateCoverage(state.SurfaceTemperatureKelvin, SnowColdLimitKelvin, SnowWarmLimitKelvin) : 0.0;
        var seaIceFraction = Approach(state.SeaIceFraction, seaIceTarget, stepYears, SeaIceResponseTimeYears);
        var landIceFraction = Approach(state.LandIceFraction, landIceTarget, stepYears, LandIceResponseTimeYears);
        var snowCoverFraction = Approach(state.SnowCoverFraction, snowTarget, stepYears, SnowResponseTimeYears);
        var cryosphereFraction = CombineCryosphere(seaIceFraction, landIceFraction, snowCoverFraction);
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
            cryosphereFraction,
            effectiveBondAlbedo)
        {
            SeaIceFraction = seaIceFraction,
            LandIceFraction = landIceFraction,
            SnowCoverFraction = snowCoverFraction,
        };
    }

    private static double EstimateCoverage(double surfaceTemperatureKelvin, double coldLimitKelvin, double warmLimitKelvin)
    {
        var t = Math.Clamp((surfaceTemperatureKelvin - coldLimitKelvin) / (warmLimitKelvin - coldLimitKelvin), 0.0, 1.0);
        var smooth = t * t * (3.0 - (2.0 * t));
        return 1.0 - smooth;
    }

    private static double Approach(double current, double target, double stepYears, double responseTimeYears)
    {
        var response = 1.0 - Math.Exp(-stepYears / responseTimeYears);
        return Math.Clamp(current + ((target - current) * response), 0.0, 1.0);
    }

    private static double CombineCryosphere(double seaIceFraction, double landIceFraction, double snowCoverFraction)
        => Math.Clamp((SeaIceWeight * seaIceFraction) + (LandIceWeight * landIceFraction) + (SnowCoverWeight * snowCoverFraction), 0.0, 1.0);

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