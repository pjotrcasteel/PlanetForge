using PlanetForge.Application.Rendering;
using PlanetForge.Domain.Physics;
using PlanetForge.Domain.Planets;

namespace PlanetForge.Application.Planets;

public sealed class PlanetExperience(PlanetMeshBuilder meshBuilder)
{
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

    private readonly PlanetState visualState = new();
    private PlanetPhysicalParameters physicalParameters = EarthReference.CreateParameters();

    public PlanetRenderSnapshot CreateSnapshot()
    {
        var physics = PlanetPhysicsCalculator.Calculate(physicalParameters);
        return new PlanetRenderSnapshot(meshBuilder.Build(visualState.Seed), visualState.SeaLevel, visualState.AtmosphereDensity, visualState.Seed, physicalParameters, physics);
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
        physicalParameters = physicalParameters with { BondAlbedo = Math.Clamp(physicalParameters.BondAlbedo - 0.03, MinimumBondAlbedo, MaximumBondAlbedo) };
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot BrightenSurface()
    {
        physicalParameters = physicalParameters with { BondAlbedo = Math.Clamp(physicalParameters.BondAlbedo + 0.03, MinimumBondAlbedo, MaximumBondAlbedo) };
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

    public PlanetRenderSnapshot RaiseSeaLevel()
    {
        visualState.ChangeSeaLevel(0.004);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot LowerSeaLevel()
    {
        visualState.ChangeSeaLevel(-0.004);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot IncreaseAtmosphere()
    {
        visualState.ChangeAtmosphereDensity(0.08);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DecreaseAtmosphere()
    {
        visualState.ChangeAtmosphereDensity(-0.08);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot Reseed()
    {
        visualState.Reseed(unchecked((visualState.Seed * 397) ^ 7919));
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot ResetEarthReference()
    {
        physicalParameters = EarthReference.CreateParameters();
        return CreateSnapshot();
    }

    private void ChangeOrbitalDistance(double deltaAstronomicalUnits)
    {
        var current = physicalParameters.OrbitalDistanceMeters / PhysicalConstants.AstronomicalUnitMeters;
        var next = Math.Clamp(current + deltaAstronomicalUnits, MinimumOrbitalDistanceAu, MaximumOrbitalDistanceAu);
        physicalParameters = physicalParameters with { OrbitalDistanceMeters = next * PhysicalConstants.AstronomicalUnitMeters };
    }

    private void ChangeStellarLuminosity(double deltaSolarLuminosity)
    {
        var current = physicalParameters.StellarLuminosityWatts / PhysicalConstants.NominalSolarLuminosityWatts;
        var next = Math.Clamp(current + deltaSolarLuminosity, MinimumStellarLuminositySolar, MaximumStellarLuminositySolar);
        physicalParameters = physicalParameters with { StellarLuminosityWatts = next * PhysicalConstants.NominalSolarLuminosityWatts };
    }

    private void ChangeMass(double deltaEarthMass)
    {
        var current = physicalParameters.MassKilograms / EarthReference.MassKilograms;
        var next = Math.Clamp(current + deltaEarthMass, MinimumPlanetMassEarth, MaximumPlanetMassEarth);
        physicalParameters = physicalParameters with { MassKilograms = next * EarthReference.MassKilograms };
    }

    private void ChangeRadius(double deltaEarthRadius)
    {
        var current = physicalParameters.RadiusMeters / EarthReference.MeanRadiusMeters;
        var next = Math.Clamp(current + deltaEarthRadius, MinimumPlanetRadiusEarth, MaximumPlanetRadiusEarth);
        physicalParameters = physicalParameters with { RadiusMeters = next * EarthReference.MeanRadiusMeters };
    }
}
