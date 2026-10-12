using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Planets;

[TestClass]
public sealed class PlanetOriginRenderProjectionTests
{
    [TestMethod]
    public void PublishEarthOrigin_HotSteamWorld_UsesCanonicalMassAndMeltInsteadOfSnowballWater()
    {
        var experience = CreateExperience();
        var hot = World(liquidMassKilograms: 0.0);
        var snapshot = experience.PublishEarthOrigin(hot);

        Assert.IsNotNull(snapshot.Origin);
        Assert.AreEqual(0L, snapshot.Origin.Revision);
        Assert.AreEqual(hot.Clock.YearsBeforePresent, snapshot.Origin.YearsBeforePresent);
        Assert.AreEqual(hot.Rock.MeltFraction, snapshot.Origin.MeltFraction, 0.0);
        Assert.AreEqual(hot.Rock.TemperatureKelvin, snapshot.Origin.RockTemperatureKelvin, 0.0);
        Assert.AreEqual(5.0, snapshot.Water.VaporMassKilograms, 0.0);
        Assert.AreEqual(0.0, snapshot.Water.LiquidMassKilograms, 0.0);
        Assert.AreEqual(-11_000.0, snapshot.SeaLevelMeters, 0.0);
        Assert.IsFalse(snapshot.Water.LiquidWaterStable, "No unsupported pressure/temperature stability claim.");
        Assert.AreEqual(0.0, snapshot.Water.BoilingPointKelvin, 0.0,
            "The old Snowball boiling-point diagnostic must not appear as Earth-origin phase physics.");
    }

    [TestMethod]
    public void PublishEarthOrigin_ExistingLiquidMass_UsesTheExistingPlanetSeaLevelSolver()
    {
        var experience = CreateExperience();
        var snapshot = experience.PublishEarthOrigin(World(liquidMassKilograms: 100_000.0));

        Assert.AreEqual(100_000.0, snapshot.Water.LiquidMassKilograms, 0.0);
        Assert.IsGreaterThan(-11_000.0, snapshot.SeaLevelMeters);
        Assert.AreEqual(5.0, snapshot.Origin!.AtmosphericVaporKilograms, 0.0);
    }

    [TestMethod]
    public void PublishEarthOrigin_LegacyOrRepeatedEpoch_IsRejectedWithoutChangingPublishedState()
    {
        var experience = CreateExperience();
        var hot = World(0.0);
        var first = experience.PublishEarthOrigin(hot);
        var legacy = new PlanetOriginEpochState(hot.Thermal, hot.Water);

        Assert.ThrowsExactly<InvalidOperationException>(() => experience.PublishEarthOrigin(legacy));
        Assert.ThrowsExactly<InvalidOperationException>(() => experience.PublishEarthOrigin(hot));
        Assert.ThrowsExactly<InvalidOperationException>(() => experience.AdvanceClimate(1));
        Assert.ThrowsExactly<InvalidOperationException>(() => experience.MoveOrbitInward());
        Assert.ThrowsExactly<InvalidOperationException>(() => experience.ExportState());
        Assert.AreEqual(first.Origin, experience.CreateSnapshot().Origin);
        experience.SetSeed(914);
        Assert.IsNull(experience.CreateSnapshot().Origin, "A new planet seed must not inherit the old world's magma.");
    }

    private static PlanetExperience CreateExperience()
    {
        var source = new FlatElevationSource();
        return new PlanetExperience(
            new PlanetSurfaceMeshCache(new PlanetSurfaceMeshBuilder(new PlanetSurfaceTileSampler(source), source)),
            new PlanetLocalSurfacePatchSampler(source), new PlanetLocalSurfaceMeshBuilder());
    }

    private static PlanetOriginEpochState World(double liquidMassKilograms)
    {
        var rock = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(100.0, 100.0, 10_000.0, 400.0, 800.0));
        var timeline = new PlanetOriginThermalTimeline(
            PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(rock.CalculateStoredEnergyJoules(650.0)), rock);
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 5.0)
            .Deliver(PlanetWaterReservoir.SurfaceLiquid, liquidMassKilograms);
        var heat = new PlanetEnvironmentHeatCapacityParameters(10.0, 1_000.0, 2_000.0, 4_000.0, 100.0, 800.0);
        var environment = new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(30_000.0, liquidMassKilograms * 4_000.0 * 300.0, 24_000_000.0));
        return new PlanetOriginEpochState(new PlanetOriginEnergyLedger(timeline, environment, heat));
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }
}
