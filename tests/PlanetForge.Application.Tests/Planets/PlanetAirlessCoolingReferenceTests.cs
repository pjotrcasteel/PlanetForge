using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Planets;

[TestClass]
public sealed class PlanetAirlessCoolingReferenceTests
{
    [TestMethod]
    public void CreateInitial_DryReference_StartsWithPartlyMoltenInteriorAndHotRockSurface()
    {
        var epoch = PlanetAirlessCoolingReference.CreateInitial();

        Assert.IsNotNull(epoch.CoupledEnergy);
        Assert.AreEqual(0L, epoch.Revision);
        Assert.AreEqual(PlanetOriginAirlessCoolingReferenceInitialAge(), epoch.Clock.YearsBeforePresent);
        Assert.AreEqual(PlanetAirlessCoolingReference.InitialInteriorTemperatureKelvin, epoch.Rock.TemperatureKelvin, 0.0);
        Assert.AreEqual(PlanetAirlessCoolingReference.InitialSurfaceTemperatureKelvin,
            epoch.Temperatures!.Value.SurfaceRockTemperatureKelvin!.Value, 0.0);
        Assert.IsGreaterThan(0.0, epoch.Rock.MeltFraction);
        Assert.IsLessThan(1.0, epoch.Rock.MeltFraction);
        Assert.AreEqual(0.0, epoch.CoupledEnergy.HeatCapacities.NoncondensableAtmosphereMassKilograms, 0.0);
        Assert.AreEqual(0.0, epoch.Water.PlanetaryWaterMassKilograms, 0.0);
        Assert.AreEqual(0.0, epoch.EnergyConservationResidualJoules, 0.0);
    }

    [TestMethod]
    public void Advance_OneHour_UpdatesTheMainGlobeFromRealCoolingWithoutInventingVolatiles()
    {
        var planet = CreateExperience();
        var before = planet.PublishOriginEpoch(PlanetAirlessCoolingReference.CreateInitial());

        var after = PlanetAirlessCoolingReference.Advance(planet);
        Assert.IsNotNull(after.Origin);
        Assert.AreEqual(1L, after.Origin.Revision);
        Assert.AreEqual(PlanetAirlessCoolingReference.AdvanceSeconds * 1_000_000L,
            planet.PublishedOriginEpoch!.Clock.MicrosecondsIntoYear);
        Assert.IsLessThan(before.Origin!.NearSurfaceRockTemperatureKelvin!.Value,
            after.Origin.NearSurfaceRockTemperatureKelvin!.Value);
        Assert.AreEqual(before.Seed, after.Seed);
        Assert.AreEqual(before.TerrainRevision, after.TerrainRevision);
        Assert.AreEqual(before.SurfaceTiles.Count, after.SurfaceTiles.Count);
        Assert.AreEqual(0.0, after.Atmosphere.SurfacePressurePascals, 0.0);
        Assert.AreEqual(0.0, after.Water.VaporMassKilograms, 0.0);
        Assert.AreEqual(0.0, after.Water.LiquidMassKilograms, 0.0);
        Assert.AreEqual(-11_000.0, after.SeaLevelMeters, 0.0);
        Assert.AreEqual(0.0, planet.PublishedOriginEpoch.WaterConservationResidualKilograms, 0.0);
        Assert.AreEqual(0.0, planet.PublishedOriginEpoch.EnergyConservationResidualJoules,
            planet.PublishedOriginEpoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
    }

    [TestMethod]
    public void Advance_RestartAndReplay_ReturnsTheSameConservedSurfaceTemperature()
    {
        var planet = CreateExperience();
        planet.PublishOriginEpoch(PlanetAirlessCoolingReference.CreateInitial());
        var first = PlanetAirlessCoolingReference.Advance(planet);

        planet.ClearOriginEpoch();
        planet.PublishOriginEpoch(PlanetAirlessCoolingReference.CreateInitial());
        var replay = PlanetAirlessCoolingReference.Advance(planet);

        Assert.AreEqual(first.Origin, replay.Origin);
        Assert.AreEqual(first.Water, replay.Water);
        Assert.AreEqual(first.SeaLevelMeters, replay.SeaLevelMeters, 0.0);
    }

    [TestMethod]
    public void Advance_SteamBearingWorldOrCancelledStep_CannotPublishReferenceCooling()
    {
        var planet = CreateExperience();
        var dry = PlanetAirlessCoolingReference.CreateInitial();
        planet.PublishOriginEpoch(dry);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            PlanetAirlessCoolingReference.Advance(planet, cancellation.Token));
        Assert.AreSame(dry, planet.PublishedOriginEpoch);
    }

    private static long PlanetOriginAirlessCoolingReferenceInitialAge() =>
        PlanetOriginClock.EarthAccretionReference.YearsBeforePresent;

    private static PlanetExperience CreateExperience()
    {
        var elevation = new FlatElevationSource();
        var sampler = new PlanetSurfaceTileSampler(elevation);
        var cache = new PlanetSurfaceMeshCache(new PlanetSurfaceMeshBuilder(sampler, elevation));
        var hypsometry = new PlanetHypsometryCache(new PlanetHypsometrySampler(elevation), elevation);
        return new PlanetExperience(cache, new PlanetLocalSurfacePatchSampler(elevation),
            new PlanetLocalSurfaceMeshBuilder(), hypsometryCache: hypsometry);
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }
}
