using PlanetForge.Application.Missions;
using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Missions;

[TestClass]
public sealed class FrozenWorldSnowballInitializationTests
{
    [TestMethod]
    public void Start_InitializesInheritedSnowballCryosphere()
    {
        var mission = new FrozenWorldMission(CreateExperience());

        var snapshot = mission.Start();

        Assert.AreEqual(1.0, snapshot.Planet.ClimateFeedback.CryosphereFraction, 0.000001);
        Assert.AreEqual(1.0, snapshot.Planet.ClimateFeedback.SeaIceFraction, 0.000001);
        Assert.AreEqual(1.0, snapshot.Planet.ClimateFeedback.LandIceFraction, 0.000001);
        Assert.AreEqual(1.0, snapshot.Planet.ClimateFeedback.SnowCoverFraction, 0.000001);
    }

    private static PlanetExperience CreateExperience()
    {
        var elevationSource = new FlatElevationSource();
        var sampler = new PlanetSurfaceTileSampler(elevationSource);
        var meshBuilder = new PlanetSurfaceMeshBuilder(sampler);
        var meshCache = new PlanetSurfaceMeshCache(meshBuilder);
        var localSampler = new PlanetLocalSurfacePatchSampler(elevationSource);
        var localMeshBuilder = new PlanetLocalSurfaceMeshBuilder();
        return new PlanetExperience(meshCache, localSampler, localMeshBuilder);
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }
}