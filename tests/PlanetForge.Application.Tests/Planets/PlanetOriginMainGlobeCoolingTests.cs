using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Planets;

/// <summary>
/// Vertical acceptance from an enthalpy-backed origin state, through a real
/// deterministic thermal process and adaptive integrator, to the ordinary
/// canonical 3D planet snapshot. The scenario is strictly AIRLESS, not a
/// calibrated steam-Hadean atmosphere or a spatial crust model.
/// </summary>
[TestClass]
public sealed class PlanetOriginMainGlobeCoolingTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    private static PlanetOriginAirlessThermalParameters Parameters => new(
        new PlanetOriginAirlessRadiationParameters(EarthRadiusMeters, 1_361.0, 0.3, 0.8),
        new PlanetOriginRockConductionParameters(4.0, 100.0));

    private static PlanetOriginAdaptiveIntegrationOptions Integration => new(
        PlanetOriginDuration.FromSeconds(300.0), PlanetOriginDuration.FromSeconds(1.0));

    [TestMethod]
    public void AdvanceOriginEpochAdaptive_HotAirlessRock_CoolsThroughMainPlanetWithoutInventingOceans()
    {
        var planet = CreateExperience();
        var original = CreateOrigin();
        var initial = planet.PublishOriginEpoch(original);

        var result = Advance(planet);

        Assert.IsNotNull(initial.Origin);
        Assert.IsNotNull(result.Origin);
        Assert.AreEqual(0L, initial.Origin.Revision);
        Assert.AreEqual(1L, result.Origin.Revision);
        Assert.AreEqual(3_600_000_000L, planet.PublishedOriginEpoch!.Clock.MicrosecondsIntoYear);
        Assert.IsLessThan(initial.Origin.BulkRockTemperatureKelvin, result.Origin.BulkRockTemperatureKelvin);
        Assert.IsLessThan(initial.Origin.BulkRockMeltFraction, result.Origin.BulkRockMeltFraction);
        Assert.IsLessThan(initial.Origin.NearSurfaceRockTemperatureKelvin!.Value,
            result.Origin.NearSurfaceRockTemperatureKelvin!.Value);
        Assert.AreEqual(0.0, result.Water.LiquidMassKilograms, 0.0);
        Assert.AreEqual(0.0, result.Water.VaporMassKilograms, 0.0);
        Assert.AreEqual(0.0, result.Atmosphere.SurfacePressurePascals, 0.0);
        Assert.AreEqual(-11_000.0, result.SeaLevelMeters, 0.0);
        Assert.AreEqual(initial.Seed, result.Seed);
        Assert.AreEqual(initial.TerrainRevision, result.TerrainRevision);
        Assert.AreEqual(initial.SurfaceTiles.Count, result.SurfaceTiles.Count);
        Assert.AreEqual(0.0, planet.PublishedOriginEpoch.EnergyConservationResidualJoules,
            original.CoupledEnergy!.StoredEnergyJoules * 1e-12);
        Assert.AreEqual(0.0, planet.PublishedOriginEpoch.WaterConservationResidualKilograms, 0.0);
        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(0L, original.Clock.MicrosecondsIntoYear);
    }

    [TestMethod]
    public void AdvanceOriginEpochAdaptive_IdenticalHotWorlds_ReplayDeterministicCanonicalFrames()
    {
        var leftPlanet = CreateExperience();
        var rightPlanet = CreateExperience();
        leftPlanet.PublishOriginEpoch(CreateOrigin());
        rightPlanet.PublishOriginEpoch(CreateOrigin());

        var first = Advance(leftPlanet);
        var replay = Advance(rightPlanet);

        Assert.AreEqual(first.Origin, replay.Origin);
        Assert.AreEqual(first.Water, replay.Water);
        Assert.AreEqual(first.Atmosphere, replay.Atmosphere);
        Assert.AreEqual(first.SeaLevelMeters, replay.SeaLevelMeters, 0.0);
        Assert.AreEqual(first.TerrainRevision, replay.TerrainRevision);
        Assert.AreEqual(leftPlanet.PublishedOriginEpoch!.CoupledEnergy!.StoredEnergyJoules,
            rightPlanet.PublishedOriginEpoch!.CoupledEnergy!.StoredEnergyJoules, 0.0);
    }

    [TestMethod]
    public void AdvanceOriginEpochAdaptive_SteamPresent_RejectsVacuumLawWithoutPublishing()
    {
        var planet = CreateExperience();
        var initial = CreateOrigin(steamMassKilograms: 1.0e16);
        var rendered = planet.PublishOriginEpoch(initial);

        Assert.ThrowsExactly<InvalidOperationException>(() => Advance(planet));

        Assert.AreSame(initial, planet.PublishedOriginEpoch);
        Assert.AreEqual(rendered.Origin, planet.CreateSnapshot().Origin);
        Assert.AreEqual(1.0e16, planet.CreateSnapshot().Water.VaporMassKilograms, 0.0);
        Assert.AreEqual(0L, planet.PublishedOriginEpoch!.Clock.MicrosecondsIntoYear);
    }

    [TestMethod]
    public void AdvanceOriginEpochAdaptive_CancelledCooling_LeavesVisibleEpochUnchanged()
    {
        var planet = CreateExperience();
        var initial = CreateOrigin();
        var rendered = planet.PublishOriginEpoch(initial);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => planet.AdvanceOriginEpochAdaptive(
            PlanetOriginDuration.FromSeconds(3_600.0), Integration,
            (energy, duration) => PlanetOriginAirlessThermalProcess.Propose(energy, duration, Parameters),
            cancellation.Token));
        Assert.AreSame(initial, planet.PublishedOriginEpoch);
        Assert.AreEqual(rendered.Origin, planet.CreateSnapshot().Origin);
    }

    private static PlanetForge.Application.Rendering.PlanetRenderSnapshot Advance(PlanetExperience planet) =>
        planet.AdvanceOriginEpochAdaptive(PlanetOriginDuration.FromSeconds(3_600.0), Integration,
            (energy, duration) => PlanetOriginAirlessThermalProcess.Propose(energy, duration, Parameters));

    private static PlanetOriginEpochState CreateOrigin(double steamMassKilograms = 0.0)
    {
        var rock = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(
            2.0e22, 1_000.0, 400_000.0, 1_200.0, 1_800.0));
        var interior = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(rock.CalculateStoredEnergyJoules(1_500.0)), rock);
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, steamMassKilograms);
        var capacities = new PlanetEnvironmentHeatCapacityParameters(
            0.0, 1_000.0, 2_000.0, 4_200.0, 1.0e18, 850.0);
        var environment = new PlanetWaterEnvironmentalState(water, new PlanetEnvironmentalEnergyBudget(
            steamMassKilograms * 2_000.0 * 800.0,
            0.0,
            1.0e18 * 850.0 * 800.0));
        return new PlanetOriginEpochState(new PlanetOriginEnergyLedger(interior, environment, capacities));
    }

    private static PlanetExperience CreateExperience()
    {
        var source = new FlatElevationSource();
        var surface = new PlanetSurfaceTileSampler(source);
        var cache = new PlanetSurfaceMeshCache(new PlanetSurfaceMeshBuilder(surface, source));
        var hypsometry = new PlanetHypsometryCache(new PlanetHypsometrySampler(source), source);
        return new PlanetExperience(cache, new PlanetLocalSurfacePatchSampler(source),
            new PlanetLocalSurfaceMeshBuilder(), hypsometryCache: hypsometry);
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }
}
