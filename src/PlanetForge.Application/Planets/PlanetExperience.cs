using PlanetForge.Application.Rendering;
using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Climate;
using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Physics;
using PlanetForge.Domain.Planets;

namespace PlanetForge.Application.Planets;

public sealed class PlanetExperience(PlanetSurfaceMeshCache surfaceMeshCache)
{
    private const int GlobalSurfaceLevel = 1;
    private const int GlobalSurfaceCellsPerAxis = 12;
    private const double MinimumOrbitalDistanceAu = 0.25;
    private const double MaximumOrbitalDistanceAu = 3.0;
    private const double MinimumStellarLuminositySolar = 0.2;
    private const double MaximumStellarLuminositySolar = 2.5;
    private const double MinimumPlanetMassEarth = 0.2;
    private const double MaximumPlanetMassEarth = 5.0;
    private const double MinimumPlanetRadiusEarth = 0.4;
    private const double MaximumPlanetRadiusEarth = 2.5;
    private const double MinimumBondAlbedo = 0.02;
    private const double MaximumBondAlbedo = 0.85;
    private const double MinimumAtmosphereEarthMasses = 0.0;
    private const double MaximumAtmosphereEarthMasses = 5.0;
    private const double MinimumCarbonDioxidePartsPerMillion = 10.0;
    private const double MaximumCarbonDioxidePartsPerMillion = 5_000.0;
    private const double MinimumWaterEarthHydrospheres = 0.0;
    private const double MaximumWaterEarthHydrospheres = 5.0;
    private const double MinimumVisualSeaLevel = -0.035;
    private const double MaximumVisualSeaLevel = 0.035;

    private readonly PlanetState state = new();

    public PlanetRenderSnapshot CreateSnapshot()
    {
        var physics = PlanetPhysicsCalculator.Calculate(state.PhysicalParameters);
        var atmosphere = AtmosphereCalculator.Calculate(state.AtmosphereParameters, state.PhysicalParameters, physics);
        var climate = SurfaceClimateCalculator.Calculate(physics, atmosphere);
        var water = WaterPhaseCalculator.Calculate(state.WaterParameters, climate.SurfaceTemperatureKelvin, atmosphere.SurfacePressurePascals);
        var seaLevel = CalculateVisualSeaLevel(water);
        var atmosphereDensity = CalculateAtmosphereDensity(atmosphere.SurfacePressurePascals);
        var surfaceTiles = surfaceMeshCache.GetOrBuildGlobal(GlobalSurfaceLevel, GlobalSurfaceCellsPerAxis, state.Seed);

        return new PlanetRenderSnapshot(
            surfaceTiles,
            seaLevel,
            atmosphereDensity,
            state.Seed,
            state.PhysicalParameters,
            physics,
            state.AtmosphereParameters,
            atmosphere,
            climate,
            state.WaterParameters,
            water);
    }

    public PlanetRenderSnapshot MoveOrbitInward()
    {
        ChangeOrbitalDistance(-0.1);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot MoveOrbitOutward()
    {
        ChangeOrbitalDistance(0.1);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DimStar()
    {
        ChangeStellarLuminosity(-0.1);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot BrightenStar()
    {
        ChangeStellarLuminosity(0.1);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DarkenSurface()
    {
        var parameters = state.PhysicalParameters;
        state.SetPhysicalParameters(parameters with { BondAlbedo = Math.Clamp(parameters.BondAlbedo - 0.03, MinimumBondAlbedo, MaximumBondAlbedo) });
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot BrightenSurface()
    {
        var parameters = state.PhysicalParameters;
        state.SetPhysicalParameters(parameters with { BondAlbedo = Math.Clamp(parameters.BondAlbedo + 0.03, MinimumBondAlbedo, MaximumBondAlbedo) });
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DecreaseMass()
    {
        ChangeMass(-0.1);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot IncreaseMass()
    {
        ChangeMass(0.1);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DecreaseRadius()
    {
        ChangeRadius(-0.05);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot IncreaseRadius()
    {
        ChangeRadius(0.05);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DecreaseAtmosphereMass()
    {
        ChangeAtmosphereMass(-0.25);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot IncreaseAtmosphereMass()
    {
        ChangeAtmosphereMass(0.25);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot HalveCarbonDioxide()
    {
        ChangeCarbonDioxide(0.5);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DoubleCarbonDioxide()
    {
        ChangeCarbonDioxide(2.0);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DecreaseWater()
    {
        ChangeWater(-0.25);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot IncreaseWater()
    {
        ChangeWater(0.25);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot Reseed()
    {
        state.Reseed(unchecked((state.Seed * 397) ^ 7919));
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot ResetEarthReference()
    {
        state.ResetEarthReference();
        return CreateSnapshot();
    }

    private void ChangeOrbitalDistance(double deltaAstronomicalUnits)
    {
        var parameters = state.PhysicalParameters;
        var current = parameters.OrbitalDistanceMeters / PhysicalConstants.AstronomicalUnitMeters;
        var next = Math.Clamp(current + deltaAstronomicalUnits, MinimumOrbitalDistanceAu, MaximumOrbitalDistanceAu);
        state.SetPhysicalParameters(parameters with { OrbitalDistanceMeters = next * PhysicalConstants.AstronomicalUnitMeters });
    }

    private void ChangeStellarLuminosity(double deltaSolarLuminosity)
    {
        var parameters = state.PhysicalParameters;
        var current = parameters.StellarLuminosityWatts / PhysicalConstants.NominalSolarLuminosityWatts;
        var next = Math.Clamp(current + deltaSolarLuminosity, MinimumStellarLuminositySolar, MaximumStellarLuminositySolar);
        state.SetPhysicalParameters(parameters with { StellarLuminosityWatts = next * PhysicalConstants.NominalSolarLuminosityWatts });
    }

    private void ChangeMass(double deltaEarthMass)
    {
        var parameters = state.PhysicalParameters;
        var current = parameters.MassKilograms / EarthReference.MassKilograms;
        var next = Math.Clamp(current + deltaEarthMass, MinimumPlanetMassEarth, MaximumPlanetMassEarth);
        state.SetPhysicalParameters(parameters with { MassKilograms = next * EarthReference.MassKilograms });
    }

    private void ChangeRadius(double deltaEarthRadius)
    {
        var parameters = state.PhysicalParameters;
        var current = parameters.RadiusMeters / EarthReference.MeanRadiusMeters;
        var next = Math.Clamp(current + deltaEarthRadius, MinimumPlanetRadiusEarth, MaximumPlanetRadiusEarth);
        state.SetPhysicalParameters(parameters with { RadiusMeters = next * EarthReference.MeanRadiusMeters });
    }

    private void ChangeAtmosphereMass(double deltaEarthAtmospheres)
    {
        var parameters = state.AtmosphereParameters;
        var current = parameters.MassKilograms / EarthAtmosphereReference.TotalMassKilograms;
        var next = Math.Clamp(current + deltaEarthAtmospheres, MinimumAtmosphereEarthMasses, MaximumAtmosphereEarthMasses);
        state.SetAtmosphereParameters(parameters with { MassKilograms = next * EarthAtmosphereReference.TotalMassKilograms });
    }

    private void ChangeCarbonDioxide(double factor)
    {
        var parameters = state.AtmosphereParameters;
        var next = Math.Clamp(parameters.CarbonDioxidePartsPerMillion * factor, MinimumCarbonDioxidePartsPerMillion, MaximumCarbonDioxidePartsPerMillion);
        state.SetAtmosphereParameters(parameters with { CarbonDioxidePartsPerMillion = next });
    }

    private void ChangeWater(double deltaEarthHydrospheres)
    {
        var parameters = state.WaterParameters;
        var current = parameters.TotalMassKilograms / EarthWaterReference.TotalHydrosphereMassKilograms;
        var next = Math.Clamp(current + deltaEarthHydrospheres, MinimumWaterEarthHydrospheres, MaximumWaterEarthHydrospheres);
        state.SetWaterParameters(parameters with { TotalMassKilograms = next * EarthWaterReference.TotalHydrosphereMassKilograms });
    }

    private static double CalculateVisualSeaLevel(WaterPhaseSnapshot water)
    {
        var liquidEarthHydrospheres = water.LiquidMassKilograms / EarthWaterReference.TotalHydrosphereMassKilograms;
        if (liquidEarthHydrospheres <= 0.0)
        {
            return MinimumVisualSeaLevel;
        }

        return Math.Clamp(MinimumVisualSeaLevel + 0.031 * Math.Sqrt(liquidEarthHydrospheres), MinimumVisualSeaLevel, MaximumVisualSeaLevel);
    }

    private static double CalculateAtmosphereDensity(double surfacePressurePascals)
    {
        var pressureRatio = surfacePressurePascals / 101_325.0;
        return Math.Clamp(0.62 * Math.Sqrt(Math.Max(pressureRatio, 0.0)), 0.0, 1.0);
    }
}
