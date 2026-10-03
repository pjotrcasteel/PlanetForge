using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Planets;

[TestClass]
public sealed class PlanetExperienceSurfaceLodTests
{
    [TestMethod]
    public void UpdateSurfaceView_NearCamera_SelectsHigherDetailThanFarCamera()
    {
        var experience = CreateExperience();
        var direction = new PlanetVector(0.0, 0.0, 1.0);

        var far = experience.UpdateSurfaceView(new PlanetSurfaceView(direction, 5.0, 1080, Math.PI / 4.2));
        var near = experience.UpdateSurfaceView(new PlanetSurfaceView(direction, 1.08, 1080, Math.PI / 4.2));

        Assert.IsTrue(near.SurfaceTiles.Max(tile => tile.Id.Level) > far.SurfaceTiles.Max(tile => tile.Id.Level));
        Assert.AreEqual(far.Seed, near.Seed);
        Assert.AreEqual(far.PhysicalParameters, near.PhysicalParameters);
    }

    [TestMethod]
    public void CreateSnapshot_BeforeCameraView_UsesCompleteGlobalFallback()
    {
        var experience = CreateExperience();

        var snapshot = experience.CreateSnapshot();

        Assert.AreEqual(24, snapshot.SurfaceTiles.Count);
        Assert.IsTrue(snapshot.SurfaceTiles.All(tile => tile.Id.Level == 1));
    }

    private static PlanetExperience CreateExperience()
    {
        var sampler = new PlanetSurfaceTileSampler(new FlatElevationSource());
        var meshBuilder = new PlanetSurfaceMeshBuilder(sampler);
        var meshCache = new PlanetSurfaceMeshCache(meshBuilder);
        var lodSelector = new PlanetSurfaceLodSelector(PlanetSurfaceLodOptions.Default);
        return new PlanetExperience(meshCache, lodSelector);
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }
}
