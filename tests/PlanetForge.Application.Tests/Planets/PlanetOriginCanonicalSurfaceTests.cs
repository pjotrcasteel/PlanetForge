using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Planets;

[TestClass]
public sealed class PlanetOriginCanonicalSurfaceTests
{
    private const double EarthRadiusMeters = 6_371_000.0;
    private const double GravityMetersPerSecondSquared = 9.81;

    [TestMethod]
    public void Create_CoupledEpoch_ProjectsConservedWaterAndEnthalpyWithoutUsingMantleAsOcean()
    {
        var epoch = CreateEpoch(2.0e18);
        var projection = PlanetOriginSurfaceProjection.Create(epoch, EarthRadiusMeters, GravityMetersPerSecondSquared);
        var exposed = 5.0e16 + 2.0e18 + 1.0e17;

        Assert.AreEqual(epoch.Revision, projection.Revision);
        Assert.AreEqual(epoch.Clock.YearsBeforePresent, projection.YearsBeforePresent);
        Assert.AreEqual(epoch.Rock.TemperatureKelvin, projection.BulkRockTemperatureKelvin, 0.0);
        Assert.AreEqual(epoch.Rock.MeltFraction, projection.BulkRockMeltFraction, 0.0);
        Assert.IsGreaterThan(0.0, projection.BulkRockMeltFraction);
        Assert.IsLessThan(1.0, projection.BulkRockMeltFraction);
        Assert.AreEqual(5.0e16, projection.AtmosphericSteamMassKilograms, 0.0);
        Assert.AreEqual(2.0e18, projection.SurfaceLiquidMassKilograms, 0.0);
        Assert.AreEqual(1.0e17, projection.SurfaceIceMassKilograms, 0.0);
        Assert.AreEqual(2.0e18 / exposed, projection.SurfaceWaterLiquidFraction, 1e-12);
        Assert.AreEqual(5.0e16 / exposed, projection.SurfaceWaterVaporFraction, 1e-12);
        Assert.AreEqual(1.0e17 / exposed, projection.SurfaceWaterIceFraction, 1e-12);
        Assert.AreEqual(5.0e16 / (1.0e17 + 5.0e16), projection.AtmosphericSteamMassFraction, 1e-12);
        Assert.AreEqual(GravityMetersPerSecondSquared * (1.0e17 + 5.0e16) /
            (4.0 * Math.PI * EarthRadiusMeters * EarthRadiusMeters), projection.SurfacePressurePascals, 1e-10);
        Assert.AreEqual(3.0e20, epoch.Water.GetMassKilograms(PlanetWaterReservoir.DissolvedInMagma), 0.0);
        Assert.AreEqual(0.0, epoch.WaterConservationResidualKilograms, 0.0);
    }

    [TestMethod]
    public void PublishOriginEpoch_SurfaceLiquidFillsExistingTerrainHypsometryAndDryEpochDrains()
    {
        var source = new FlatElevationSource();
        var experience = CreateExperience(source, includeHypsometry: true);
        var epoch = CreateEpoch(2.0e18);

        var render = experience.PublishOriginEpoch(epoch);
        var expectedLevel = 2.0e18 / (1_000.0 * 4.0 * Math.PI * EarthRadiusMeters * EarthRadiusMeters);

        Assert.AreSame(epoch, experience.PublishedOriginEpoch);
        Assert.IsNotNull(render.Origin);
        Assert.AreEqual(expectedLevel, render.SeaLevelMeters, 0.02);
        Assert.AreEqual(epoch.Rock.MeltFraction, render.Origin.BulkRockMeltFraction, 0.0);
        Assert.AreEqual(2.0e18, render.Origin.SurfaceLiquidMassKilograms, 0.0);
        Assert.IsGreaterThan(0, source.SampleCount);
        var samples = source.SampleCount;
        _ = experience.CreateSnapshot();
        Assert.AreEqual(samples, source.SampleCount);

        experience.ClearOriginEpoch();
        var dry = experience.PublishOriginEpoch(CreateEpoch(0.0));
        Assert.AreEqual(-11_000.0, dry.SeaLevelMeters, 0.0);
        Assert.AreEqual(0.0, dry.Origin!.SurfaceLiquidMassKilograms, 0.0);
        Assert.AreEqual(samples, source.SampleCount);
    }

    [TestMethod]
    public void PublishOriginEpoch_LegacyOrMissingHypsometry_DoesNotMutatePublication()
    {
        var experience = CreateExperience(new FlatElevationSource(), includeHypsometry: true);
        var initial = experience.CreateSnapshot();
        var coupled = CreateEpoch(2.0e18);
        var legacy = new PlanetOriginEpochState(coupled.Thermal, coupled.Water);

        Assert.ThrowsExactly<InvalidOperationException>(() => experience.PublishOriginEpoch(legacy));
        Assert.IsNull(experience.PublishedOriginEpoch);
        Assert.AreEqual(initial.SeaLevelMeters, experience.CreateSnapshot().SeaLevelMeters, 0.0);

        var withoutHypsometry = CreateExperience(new FlatElevationSource(), includeHypsometry: false);
        Assert.ThrowsExactly<InvalidOperationException>(() => withoutHypsometry.PublishOriginEpoch(coupled));
        Assert.IsNull(withoutHypsometry.PublishedOriginEpoch);
    }

    [TestMethod]
    public void PublishOriginEpoch_FrozenClimateCannotAdvanceBoundGeologicalClock()
    {
        var experience = CreateExperience(new FlatElevationSource(), includeHypsometry: true);
        var origin = CreateEpoch(2.0e18);
        experience.PublishOriginEpoch(origin);

        Assert.ThrowsExactly<InvalidOperationException>(() => experience.AdvanceClimate(1.0));
        Assert.ThrowsExactly<InvalidOperationException>(() => experience.MoveOrbitInward());
        Assert.ThrowsExactly<InvalidOperationException>(() => experience.ExportState());
        Assert.AreEqual(origin.Clock, experience.PublishedOriginEpoch!.Clock);
        Assert.AreEqual(0L, experience.PublishedOriginEpoch.Revision);

        var legacy = experience.ClearOriginEpoch();
        Assert.IsNull(legacy.Origin);
        Assert.IsNull(experience.PublishedOriginEpoch);
    }

    [TestMethod]
    public void AdvanceOriginEpochAdaptive_AcceptedCoolingChangesMainPlanetRockClockAndRevisionTogether()
    {
        var experience = CreateExperience(new FlatElevationSource(), includeHypsometry: true);
        var origin = CreateEpoch(2.0e18);
        var initial = experience.PublishOriginEpoch(origin);
        var second = PlanetOriginDuration.FromSeconds(1.0);
        var limits = new PlanetOriginAdaptiveIntegrationOptions(second, second);

        var cooled = experience.AdvanceOriginEpochAdaptive(second, limits, (energy, dt) =>
            new PlanetOriginEnergyStep(dt, 0.0, 0.0,
                new PlanetInteriorHeatExport(1.0e19, PlanetEnvironmentHeatReservoir.SurfaceRock),
                new PlanetRadiativeExchange(
                    new PlanetRadiativeBalance(1.0, 0.0, 0.0, 0.0, 0.0),
                    PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.Atmosphere), []));

        Assert.IsNotNull(initial.Origin);
        Assert.IsNotNull(cooled.Origin);
        Assert.AreEqual(0L, initial.Origin.Revision);
        Assert.AreEqual(1L, cooled.Origin.Revision);
        Assert.IsLessThan(initial.Origin.BulkRockTemperatureKelvin, cooled.Origin.BulkRockTemperatureKelvin);
        Assert.AreEqual(1_000_000L, experience.PublishedOriginEpoch!.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(cooled.Origin.BulkRockMeltFraction, experience.PublishedOriginEpoch.Rock.MeltFraction, 0.0);
        Assert.AreEqual(initial.SeaLevelMeters, cooled.SeaLevelMeters, 0.0, "Cooling alone must not invent or remove water.");
        Assert.AreEqual(0L, origin.Revision);
    }

    [TestMethod]
    public void AdvanceOriginEpochAdaptive_ImpossibleCoolingDoesNotPublishPartialState()
    {
        var experience = CreateExperience(new FlatElevationSource(), includeHypsometry: true);
        var origin = CreateEpoch(2.0e18);
        experience.PublishOriginEpoch(origin);
        var oneSecond = PlanetOriginDuration.FromSeconds(1.0);
        var limits = new PlanetOriginAdaptiveIntegrationOptions(oneSecond, oneSecond);

        Assert.ThrowsExactly<InvalidOperationException>(() => experience.AdvanceOriginEpochAdaptive(
            oneSecond, limits, (energy, dt) => new PlanetOriginEnergyStep(dt, 0.0, 0.0,
                new PlanetInteriorHeatExport(1.0e30, PlanetEnvironmentHeatReservoir.SurfaceRock),
                new PlanetRadiativeExchange(
                    new PlanetRadiativeBalance(1.0, 0.0, 0.0, 0.0, 0.0),
                    PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.Atmosphere), [])));

        Assert.AreSame(origin, experience.PublishedOriginEpoch);
        Assert.AreEqual(0L, experience.CreateSnapshot().Origin!.Revision);
        Assert.AreEqual(0L, experience.PublishedOriginEpoch!.Clock.MicrosecondsIntoYear);
    }

    [TestMethod]
    public void PublishOriginEpoch_RepeatedOrStaleGeologicalPublication_IsRejected()
    {
        var experience = CreateExperience(new FlatElevationSource(), includeHypsometry: true);
        var first = CreateEpoch(2.0e18);
        experience.PublishOriginEpoch(first);
        var duration = PlanetOriginDuration.FromSeconds(1.0);
        var noFlux = new PlanetOriginEnergyStep(duration, 0.0, 0.0,
            new PlanetInteriorHeatExport(0.0, PlanetEnvironmentHeatReservoir.SurfaceRock),
            new PlanetRadiativeExchange(
                new PlanetRadiativeBalance(1.0, 0.0, 0.0, 0.0, 0.0),
                PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.Atmosphere), []);
        var second = first.AdvanceCoupled(noFlux);
        experience.PublishOriginEpoch(second);

        Assert.ThrowsExactly<InvalidOperationException>(() => experience.PublishOriginEpoch(first));
        Assert.ThrowsExactly<InvalidOperationException>(() => experience.PublishOriginEpoch(second));
        Assert.AreSame(second, experience.PublishedOriginEpoch);
        Assert.AreEqual(1L, experience.CreateSnapshot().Origin!.Revision);
    }

    private static PlanetOriginEpochState CreateEpoch(double liquidMassKilograms)
    {
        var rock = new PlanetRockEnthalpyModel(
            new PlanetRockThermalProperties(2.0e18, 1_000.0, 400_000.0, 1_200.0, 1_800.0));
        var interior = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(rock.CalculateStoredEnergyJoules(1_500.0)), rock);
        var water = PlanetWaterInventory.Empty
            .Deliver(PlanetWaterReservoir.DissolvedInMagma, 3.0e20)
            .Deliver(PlanetWaterReservoir.AtmosphericVapor, 5.0e16)
            .Deliver(PlanetWaterReservoir.SurfaceLiquid, liquidMassKilograms)
            .Deliver(PlanetWaterReservoir.SurfaceIce, 1.0e17);
        var environment = new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(1.0e23, liquidMassKilograms == 0.0 ? 0.0 : 2.52e24, 1.1e24));
        var thermalProperties = new PlanetEnvironmentHeatCapacityParameters(
            1.0e17, 1_000.0, 2_000.0, 4_200.0, 1.0e18, 850.0);
        return new PlanetOriginEpochState(new PlanetOriginEnergyLedger(interior, environment, thermalProperties));
    }

    private static PlanetExperience CreateExperience(FlatElevationSource source, bool includeHypsometry)
    {
        var surfaceSampler = new PlanetSurfaceTileSampler(source);
        var cache = new PlanetSurfaceMeshCache(new PlanetSurfaceMeshBuilder(surfaceSampler, source));
        var hypsometry = includeHypsometry
            ? new PlanetHypsometryCache(new PlanetHypsometrySampler(source), source) : null;
        return new PlanetExperience(cache, new PlanetLocalSurfacePatchSampler(source),
            new PlanetLocalSurfaceMeshBuilder(), hypsometryCache: hypsometry);
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public int SampleCount { get; private set; }

        public double SampleElevationMeters(PlanetVector direction, int seed)
        {
            SampleCount++;
            return 0.0;
        }
    }
}
