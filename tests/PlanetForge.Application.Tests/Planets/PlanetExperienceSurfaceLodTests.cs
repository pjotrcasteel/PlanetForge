using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Planets;

[TestClass]
public sealed class PlanetExperienceSurfaceLodTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

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
    public void UpdateSurfaceView_CloseToSurface_SwitchesToLocalMetreMesh()
    {
        var experience = CreateExperience();
        var view = new PlanetSurfaceView(PlanetVector.UnitZ, 1.005, 1080, Math.PI / 4.2) { ViewportAspectRatio = 16.0 / 9.0 };

        var snapshot = experience.UpdateSurfaceView(view);

        Assert.IsNotNull(snapshot.LocalSurface);
        Assert.AreEqual(0, snapshot.SurfaceTiles.Count);
        Assert.IsTrue(snapshot.LocalSurface.CameraAltitudeMeters > 0.0);
        Assert.IsTrue(snapshot.LocalSurface.SizeMeters > 0.0);
    }

    [TestMethod]
    public void UpdateSurfaceView_ThreeMetresAboveEarth_UsesSubMetreTerrainCells()
    {
        var experience = CreateExperience();
        var cameraDistance = 1.0 + (3.0 / EarthRadiusMeters);
        var view = new PlanetSurfaceView(PlanetVector.UnitZ, cameraDistance, 1080, Math.PI / 4.2) { ViewportAspectRatio = 16.0 / 9.0 };

        var snapshot = experience.UpdateSurfaceView(view);

        Assert.IsNotNull(snapshot.LocalSurface);
        Assert.AreEqual(16.0, snapshot.LocalSurface.SizeMeters, 0.001);
        Assert.IsLessThanOrEqualTo(3.01, snapshot.LocalSurface.CameraAltitudeMeters);
        Assert.IsGreaterThanOrEqualTo(2.99, snapshot.LocalSurface.CameraAltitudeMeters);
    }

    [TestMethod]
    public void MoveLocalSurfaceAnchor_ChangesCanonicalAddressAndPreservesTravelDistance()
    {
        var experience = CreateExperience();
        var cameraDistance = 1.0 + (10.0 / EarthRadiusMeters);
        var view = new PlanetSurfaceView(PlanetVector.UnitZ, cameraDistance, 1080, Math.PI / 4.2) { ViewportAspectRatio = 16.0 / 9.0 };
        var initial = experience.UpdateSurfaceView(view);

        var moved = experience.MoveLocalSurfaceAnchor(100.0, 0.0);

        Assert.IsNotNull(initial.LocalSurface);
        Assert.IsNotNull(moved.LocalSurface);
        Assert.AreNotEqual(initial.LocalSurface.AnchorAddress, moved.LocalSurface.AnchorAddress);
        var angularDistance = Math.Acos(Math.Clamp(PlanetVector.Dot(initial.LocalSurface.AnchorDirection, moved.LocalSurface.AnchorDirection), -1.0, 1.0));
        Assert.AreEqual(100.0, angularDistance * EarthRadiusMeters, 0.01);
    }

    [TestMethod]
    public void UpdateSurfaceView_WhileLocal_DoesNotReplaceTravelledAnchorWithCameraDirection()
    {
        var experience = CreateExperience();
        var cameraDistance = 1.0 + (10.0 / EarthRadiusMeters);
        var view = new PlanetSurfaceView(PlanetVector.UnitZ, cameraDistance, 1080, Math.PI / 4.2) { ViewportAspectRatio = 16.0 / 9.0 };
        experience.UpdateSurfaceView(view);
        var travelled = experience.MoveLocalSurfaceAnchor(250.0, 125.0);

        var afterCameraUpdate = experience.UpdateSurfaceView(view with { CameraDirection = PlanetVector.UnitX });

        Assert.IsNotNull(travelled.LocalSurface);
        Assert.IsNotNull(afterCameraUpdate.LocalSurface);
        Assert.AreEqual(travelled.LocalSurface.AnchorAddress, afterCameraUpdate.LocalSurface.AnchorAddress);
        Assert.AreEqual(travelled.LocalSurface.AnchorDirection, afterCameraUpdate.LocalSurface.AnchorDirection);
    }

    [TestMethod]
    public void MoveLocalSurfaceAnchor_WithoutLocalView_Throws()
    {
        var experience = CreateExperience();

        Assert.ThrowsExactly<InvalidOperationException>(() => experience.MoveLocalSurfaceAnchor(1.0, 0.0));
    }

    [TestMethod]
    public void CreateSnapshot_BeforeCameraView_UsesCompleteGlobalFallback()
    {
        var experience = CreateExperience();

        var snapshot = experience.CreateSnapshot();

        Assert.AreEqual(24, snapshot.SurfaceTiles.Count);
        Assert.IsNull(snapshot.LocalSurface);
        Assert.IsTrue(snapshot.SurfaceTiles.All(tile => tile.Id.Level == 1));
    }

    private static PlanetExperience CreateExperience()
    {
        var elevationSource = new FlatElevationSource();
        var sampler = new PlanetSurfaceTileSampler(elevationSource);
        var meshBuilder = new PlanetSurfaceMeshBuilder(sampler);
        var meshCache = new PlanetSurfaceMeshCache(meshBuilder);
        var lodSelector = new PlanetSurfaceLodSelector(PlanetSurfaceLodOptions.Default);
        var localSampler = new PlanetLocalSurfacePatchSampler(elevationSource);
        var localMeshBuilder = new PlanetLocalSurfaceMeshBuilder();
        return new PlanetExperience(meshCache, lodSelector, localSampler, localMeshBuilder);
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }
}