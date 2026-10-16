using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Planets;

[TestClass]
public sealed class PlanetRegionalCoolingReferenceTests
{
    private const double Radius = 6_371_000.0;

    [TestMethod]
    public void CreateInitial_DeclaredHeatContrast_ConservesFundedFusionEnthalpyAndProducesRealRegionalPhase()
    {
        var canonical = PlanetRegionalCoolingReference.CreateInitial(Radius);
        var state = canonical.Epoch;
        var field = canonical.Field;
        var phases = field.Cells.Select(c => c.Resolve(field.Material)).ToArray();

        Assert.AreEqual(0L, state.Revision);
        Assert.AreEqual(0.0, state.EnergyConservationResidualJoules,
            state.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
        Assert.AreEqual(0.0, state.Water.PlanetaryWaterMassKilograms, 0.0);
        Assert.AreEqual(field.TotalStoredEnthalpyJoules,
            state.CoupledEnergy.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock),
            field.TotalStoredEnthalpyJoules * 1e-12);
        Assert.IsGreaterThan(0.0, phases.Max(p => p.MeltFraction) - phases.Min(p => p.MeltFraction));
        Assert.IsGreaterThan(0.0, phases.Max(p => p.TemperatureKelvin) - phases.Min(p => p.TemperatureKelvin));
        Assert.IsGreaterThan(0.0, phases.Max(p => p.MeltFraction));
    }

    [TestMethod]
    public void Advance_OneHour_RegionalMeltAndTemperatureRemainCanonicalInMainPlanet()
    {
        var experience = CreateExperience();
        var initial = PlanetRegionalCoolingReference.CreateInitial(Radius);
        var first = experience.PublishRegionalRock(initial);
        var proposed = PlanetRegionalCoolingReference.Advance(initial, first);
        var next = experience.PublishRegionalRock(proposed);

        Assert.IsNotNull(first.Origin);
        Assert.IsNotNull(first.SpatialRock);
        Assert.IsNotNull(next.SpatialRock);
        Assert.AreEqual(proposed.Epoch.Revision, next.Origin!.Revision);
        Assert.AreEqual(next.Origin.Revision, next.SpatialRock.Revision);
        Assert.AreEqual(PlanetRegionalCoolingReference.AdvanceSeconds * 1_000_000L,
            experience.PublishedOriginEpoch!.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(-11_000.0, next.SeaLevelMeters, 0.0);
        Assert.AreEqual(0.0, next.Water.LiquidMassKilograms, 0.0);
        Assert.AreEqual(0.0, next.Atmosphere.SurfacePressurePascals, 0.0);
        Assert.AreEqual(first.Seed, next.Seed);
        Assert.AreEqual(first.TerrainRevision, next.TerrainRevision);
        Assert.AreEqual(first.SurfaceTiles.Count, next.SurfaceTiles.Count);
        Assert.AreEqual(0.0, proposed.Epoch.EnergyConservationResidualJoules,
            proposed.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-11);
        Assert.AreEqual(0.0, proposed.Epoch.WaterConservationResidualKilograms, 0.0);

        Assert.AreEqual(128, next.SpatialRock.Width);
        Assert.AreEqual(64, next.SpatialRock.Height);
        Assert.AreEqual(128 * 64, next.SpatialRock.TemperatureKelvin.Length);
        Assert.AreEqual(128 * 64, next.SpatialRock.MeltFraction.Length);
        Assert.IsGreaterThan(next.SpatialRock.TemperatureKelvin.Min(), next.SpatialRock.TemperatureKelvin.Max());
        Assert.IsLessThan(first.SpatialRock.TemperatureKelvin.Average(), next.SpatialRock.TemperatureKelvin.Average());
        Assert.AreEqual(0L, initial.Epoch.Revision);
    }

    [TestMethod]
    public void PublishRegionalRock_RejectsStaleEpochAndPreservesVisibleAtlas()
    {
        var experience = CreateExperience();
        var initial = PlanetRegionalCoolingReference.CreateInitial(Radius);
        var baseline = experience.PublishRegionalRock(initial);

        Assert.ThrowsExactly<InvalidOperationException>(() => experience.PublishRegionalRock(initial));
        Assert.AreSame(initial, experience.PublishedRegionalRock);
        Assert.AreSame(baseline.SpatialRock, experience.CreateSnapshot().SpatialRock);
    }

    [TestMethod]
    public void RegionalProjection_SameCanonicalEpoch_IsDeterministicAndUnaffectedByCameraLOD()
    {
        var first = PlanetRegionalCoolingReference.CreateInitial(Radius);
        var atlasA = PlanetOriginSpatialSurfaceProjection.Create(first);
        var atlasB = PlanetOriginSpatialSurfaceProjection.Create(first);

        CollectionAssert.AreEqual(atlasA.TemperatureKelvin, atlasB.TemperatureKelvin);
        CollectionAssert.AreEqual(atlasA.MeltFraction, atlasB.MeltFraction);
        Assert.AreEqual(first.Epoch.Revision, atlasA.Revision);
        Assert.AreEqual(0.0, first.Epoch.EnergyConservationResidualJoules,
            first.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
    }

    private static PlanetExperience CreateExperience()
    {
        var elevations = new FlatElevationSource();
        var sampler = new PlanetSurfaceTileSampler(elevations);
        var cache = new PlanetSurfaceMeshCache(new PlanetSurfaceMeshBuilder(sampler, elevations));
        var hypsometry = new PlanetHypsometryCache(new PlanetHypsometrySampler(elevations), elevations);
        return new PlanetExperience(cache, new PlanetLocalSurfacePatchSampler(elevations),
            new PlanetLocalSurfaceMeshBuilder(), hypsometryCache: hypsometry);
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }
}
