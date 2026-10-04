using PlanetForge.Application.Missions;
using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Missions;

[TestClass]
public sealed class FrozenWorldInitialRenderTests
{
    [TestMethod]
    public void Start_AfterMissionSpinUp_ContainsFullGlobeGeometryForFirstWebGlRender()
    {
        var mission = new FrozenWorldMission(CreateExperience());

        var snapshot = mission.Start();

        Assert.AreEqual(24, snapshot.Planet.SurfaceTiles.Count);
        Assert.IsTrue(snapshot.Planet.SurfaceTiles.All(tile => tile.Positions.Length > 0));
        Assert.IsTrue(snapshot.Planet.SurfaceTiles.All(tile => tile.Normals.Length > 0));
    }

    [TestMethod]
    public void CreateInitialRenderSnapshot_AfterReferenceOnlySnapshot_ReturnsFullGeometryAgain()
    {
        var experience = CreateExperience();
        experience.CreateSnapshot();
        var referenceOnly = experience.CreateSnapshot();

        var initialRender = experience.CreateInitialRenderSnapshot();

        Assert.IsTrue(referenceOnly.SurfaceTiles.All(tile => tile.Positions.Length == 0));
        Assert.IsTrue(initialRender.SurfaceTiles.All(tile => tile.Positions.Length > 0));
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