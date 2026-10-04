using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Climate;
using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Physics;

namespace PlanetForge.Domain.Tests.Climate;

[TestClass]
public sealed class CryosphereComponentTests
{
    [TestMethod]
    public void Advance_WarmingWorld_SeaIceRetreatsFasterThanLandIce()
    {
        var coldPlanet = EarthReference.CreateParameters() with { OrbitalDistanceMeters = PhysicalConstants.AstronomicalUnitMeters * 1.35 };
        var coldPhysics = PlanetPhysicsCalculator.Calculate(coldPlanet);
        var atmosphere = AtmosphereCalculator.Calculate(EarthAtmosphereReference.CreateParameters(), coldPlanet, coldPhysics);
        var water = EarthWaterReference.CreateParameters();
        var initial = ClimateFeedbackSimulator.Initialize(coldPlanet, atmosphere, water);
        var frozen = ClimateFeedbackSimulator.Advance(coldPlanet, atmosphere, water, initial, 100.0);
        var warmPlanet = EarthReference.CreateParameters() with { OrbitalDistanceMeters = PhysicalConstants.AstronomicalUnitMeters * 0.75 };

        var warming = ClimateFeedbackSimulator.Advance(warmPlanet, atmosphere, water, frozen, 20.0);

        Assert.IsLessThan(warming.LandIceFraction, warming.SeaIceFraction);
        Assert.IsLessThan(frozen.SeaIceFraction, warming.SeaIceFraction);
        Assert.IsLessThan(frozen.LandIceFraction, warming.LandIceFraction);
    }

    [TestMethod]
    public void Evaluate_ExposesCryosphereComponents()
    {
        var planet = EarthReference.CreateParameters() with { OrbitalDistanceMeters = PhysicalConstants.AstronomicalUnitMeters * 1.2 };
        var physics = PlanetPhysicsCalculator.Calculate(planet);
        var atmosphere = AtmosphereCalculator.Calculate(EarthAtmosphereReference.CreateParameters(), planet, physics);
        var water = EarthWaterReference.CreateParameters();
        var state = ClimateFeedbackSimulator.Initialize(planet, atmosphere, water);

        var result = ClimateFeedbackSimulator.Evaluate(planet, atmosphere, water, state);

        Assert.AreEqual(state.SeaIceFraction, result.Feedback.SeaIceFraction, 0.000001);
        Assert.AreEqual(state.LandIceFraction, result.Feedback.LandIceFraction, 0.000001);
        Assert.AreEqual(state.SnowCoverFraction, result.Feedback.SnowCoverFraction, 0.000001);
    }
}