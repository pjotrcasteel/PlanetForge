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
    public void UpdateSurfaceView_AboveLocalTransition_UsesStableGlobalFallback()
    {
        var experience = CreateExperience();
        var far = experience.UpdateSurfaceView(new PlanetSurfaceView(PlanetVector.UnitZ, 5.0, 1080, Math.PI / 4.2));
        var cameraDistance = 1.0 + (25_000.0 / EarthRadiusMeters);
        var nearGlobe = experience.UpdateSurfaceView(new PlanetSurfaceView(PlanetVector.UnitZ, cameraDistance, 1080, Math.PI / 4.2));

        Assert.IsNull(far.LocalSurface);
        Assert.IsNull(nearGlobe.LocalSurface);
        Assert.AreEqual(24, far.SurfaceTiles.Count);
        Assert.AreEqual(24, nearGlobe.SurfaceTiles.Count);
        Assert.IsTrue(far.SurfaceTiles.All(tile => tile.Id.Level == 1));
        Assert.IsTrue(nearGlobe.SurfaceTiles.All(tile => tile.Id.Level == 1));
    }

    [TestMethod]
    public void UpdateSurfaceView_JustInsideLocalTransition_UsesCheapCoarseLocalMesh()
    {
        var experience = CreateExperience();
        var cameraDistance = 1.0 + (19_000.0 / EarthRadiusMeters);
        var view = new PlanetSurfaceView(PlanetVector.UnitZ, cameraDistance, 1080, Math.PI / 4.2) { ViewportAspectRatio = 16.0 / 9.0 };

        var snapshot = experience.UpdateSurfaceView(view);

        Assert.IsNotNull(snapshot.LocalSurface);
        Assert.AreEqual(0, snapshot.SurfaceTiles.Count);
        Assert.AreEqual(128, snapshot.LocalSurface.TriangleCount);
    }

    [TestMethod]
    public void UpdateSurfaceView_CloseToSurface_SwitchesToLocalMetreMesh()
    {
        var experience = CreateExperience();
        var cameraDistance = 1.0 + (5_000.0 / EarthRadiusMeters);
        var view = new PlanetSurfaceView(PlanetVector.UnitZ, cameraDistance, 1080, Math.PI / 4.2) { ViewportAspectRatio = 16.0 / 9.0 };

        var snapshot = experience.UpdateSurfaceView(view);

        Assert.IsNotNull(snapshot.LocalSurface);
        Assert.AreEqual(0, snapshot.SurfaceTiles.Count);
        Assert.IsTrue(snapshot.LocalSurface.CameraAltitudeMeters > 0.0);
        Assert.IsTrue(snapshot.LocalSurface.SizeMeters > 0.0);
    }

    [TestMethod]
    public void UpdateSurfaceView_HundredMetresAboveEarth_DoesNotUseFineGroundMesh()
    {
        var experience = CreateExperience();
        var cameraDistance = 1.0 + (100.0 / EarthRadiusMeters);
        var view = new PlanetSurfaceView(PlanetVector.UnitZ, cameraDistance, 1080, Math.PI / 4.2) { ViewportAspectRatio = 16.0 / 9.0 };

        var snapshot = experience.UpdateSurfaceView(view);

        Assert.IsNotNull(snapshot.LocalSurface);
        Assert.AreEqual(1_152, snapshot.LocalSurface.TriangleCount);
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
        Assert.AreEqual(2_048, snapshot.LocalSurface.TriangleCount);
        Assert.IsLessThanOrEqualTo(3.01, snapshot.LocalSurface.CameraAltitudeMeters);
        Assert.IsGreaterThanOrEqualTo(2.99, snapshot.LocalSurface.CameraAltitudeMeters);
    }

    [TestMethod]
    public void UpdateSurfaceView_SameLocalGeometryBand_ReusesGeneratedTerrainAndReturnsReferenceOnly()
    {
        var elevationSource = new CountingElevationSource();
        var experience = CreateExperience(elevationSource);
        var firstDistance = 1.0 + (10_000.0 / EarthRadiusMeters);
        var secondDistance = 1.0 + (9_000.0 / EarthRadiusMeters);
        var firstView = new PlanetSurfaceView(PlanetVector.UnitZ, firstDistance, 1080, Math.PI / 4.2) { ViewportAspectRatio = 16.0 / 9.0 };
        var secondView = firstView with { CameraDistanceFromCenter = secondDistance };

        var first = experience.UpdateSurfaceView(firstView);
        var callsAfterFirst = elevationSource.SampleCount;
        var second = experience.UpdateSurfaceView(secondView);

        Assert.IsNotNull(first.LocalSurface);
        Assert.IsNotNull(second.LocalSurface);
        Assert.AreEqual(first.LocalSurface.Key, second.LocalSurface.Key);
        Assert.AreEqual(callsAfterFirst, elevationSource.SampleCount);
        Assert.AreNotEqual(first.LocalSurface.CameraAltitudeMeters, second.LocalSurface.CameraAltitudeMeters);
        Assert.IsGreaterThan(0, first.LocalSurface.PositionsMeters.Length);
        Assert.IsGreaterThan(0, first.LocalSurface.Normals.Length);
        Assert.IsGreaterThan(0, first.LocalSurface.ElevationsMeters.Length);
        Assert.AreEqual(0, second.LocalSurface.PositionsMeters.Length);
        Assert.AreEqual(0, second.LocalSurface.Normals.Length);
        Assert.AreEqual(0, second.LocalSurface.ElevationsMeters.Length);
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

    [TestMethod]
    public void InvalidateTerrain_ChangedTerrainRevision_PropagatesToRenderSnapshot()
    {
        var terrainStore = new RevisionTerrainStore();
        var experience = CreateExperience(terrainDeformationStore: terrainStore);
        var before = experience.CreateSnapshot();
        terrainStore.Apply(before.Seed, [new PlanetTerrainDeformation(PlanetVector.UnitX, 0.001, 0.002, 10.0, 5.0)]);

        var after = experience.InvalidateTerrain();

        Assert.AreEqual(0, before.TerrainRevision);
        Assert.AreEqual(1, after.TerrainRevision);
        Assert.IsTrue(after.SurfaceTiles.All(tile => tile.IncludesGeometry));
    }

    private static PlanetExperience CreateExperience(IPlanetElevationSource? elevationSource = null, IPlanetTerrainDeformationStore? terrainDeformationStore = null)
    {
        elevationSource ??= new FlatElevationSource();
        var sampler = new PlanetSurfaceTileSampler(elevationSource);
        var meshBuilder = new PlanetSurfaceMeshBuilder(sampler);
        var meshCache = new PlanetSurfaceMeshCache(meshBuilder);
        var localSampler = new PlanetLocalSurfacePatchSampler(elevationSource);
        var localMeshBuilder = new PlanetLocalSurfaceMeshBuilder();
        return new PlanetExperience(meshCache, localSampler, localMeshBuilder, terrainDeformationStore);
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }

    private sealed class CountingElevationSource : IPlanetElevationSource
    {
        public int SampleCount { get; private set; }

        public double SampleElevationMeters(PlanetVector direction, int seed)
        {
            SampleCount++;
            return 0.0;
        }
    }

    private sealed class RevisionTerrainStore : IPlanetTerrainDeformationStore
    {
        private int revision;

        public double SampleElevationDeltaMeters(PlanetVector direction, int seed) => 0.0;

        public int GetRevision(int seed) => revision;

        public void Apply(int seed, IReadOnlyList<PlanetTerrainDeformation> deformations) => revision++;

        public void ApplyDeposition(int seed, IReadOnlyList<PlanetTerrainDeposition> depositions) => revision++;

        public void Clear(int seed) => revision = 0;

        public void ClearAll() => revision = 0;
    }
}