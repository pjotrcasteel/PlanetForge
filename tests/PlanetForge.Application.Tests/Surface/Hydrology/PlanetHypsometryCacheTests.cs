using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface.Hydrology;

[TestClass]
public sealed class PlanetHypsometryCacheTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void GetOrBuild_UnchangedWorld_ReusesIdenticalHypsometry()
    {
        var source = new CountingElevationSource();
        var cache = CreateCache(source);

        var first = cache.GetOrBuild(2, 42, EarthRadiusMeters, CancellationToken.None);
        var samples = source.SampleCount;
        var second = cache.GetOrBuild(2, 42, EarthRadiusMeters, CancellationToken.None);

        Assert.AreSame(first, second);
        Assert.AreEqual(samples, source.SampleCount);
        Assert.IsGreaterThan(0, samples);
    }

    [TestMethod]
    public void GetOrBuild_GeologicalPublication_RebuildsWaterVolumeCurve()
    {
        var source = new CountingElevationSource();
        var cache = CreateCache(source);
        var initial = cache.GetOrBuild(2, 42, EarthRadiusMeters, CancellationToken.None);

        source.PublishNewGeology();
        var updated = cache.GetOrBuild(2, 42, EarthRadiusMeters, CancellationToken.None);

        Assert.AreNotSame(initial, updated);
        Assert.IsGreaterThan(0.0, updated.SolveSeaLevelMeters(1.0e15) - initial.SolveSeaLevelMeters(1.0e15));
    }

    [TestMethod]
    public void GetOrBuild_ErosionRevision_RebuildsVolumeCurve()
    {
        var source = new CountingElevationSource();
        var deformations = new RevisionedDeformationStore();
        var cache = CreateCache(source, deformations);
        var initial = cache.GetOrBuild(2, 42, EarthRadiusMeters, CancellationToken.None);
        var samples = source.SampleCount;

        deformations.Advance();
        var updated = cache.GetOrBuild(2, 42, EarthRadiusMeters, CancellationToken.None);

        Assert.AreNotSame(initial, updated);
        Assert.IsGreaterThan(samples, source.SampleCount);
    }

    [TestMethod]
    public void GetOrBuild_ChangedSeedRadiusOrResolution_Rebuilds()
    {
        var source = new CountingElevationSource();
        var cache = CreateCache(source);
        var initial = cache.GetOrBuild(1, 42, EarthRadiusMeters, CancellationToken.None);
        var newSeed = cache.GetOrBuild(1, 43, EarthRadiusMeters, CancellationToken.None);
        var newRadius = cache.GetOrBuild(1, 43, EarthRadiusMeters + 1.0, CancellationToken.None);
        var newResolution = cache.GetOrBuild(2, 43, EarthRadiusMeters + 1.0, CancellationToken.None);

        Assert.AreNotSame(initial, newSeed);
        Assert.AreNotSame(newSeed, newRadius);
        Assert.AreNotSame(newRadius, newResolution);
    }

    [TestMethod]
    public void GetOrBuild_TerrainChangedDuringSampling_DoesNotPublishMixedEpoch()
    {
        var source = new CountingElevationSource();
        var cache = CreateCache(source);
        cache.GetOrBuild(1, 42, EarthRadiusMeters, CancellationToken.None);

        source.PublishNewGeology();
        source.PublishDuringNextSample = true;
        Assert.ThrowsExactly<InvalidOperationException>(() => cache.GetOrBuild(1, 42, EarthRadiusMeters, CancellationToken.None));

        var stable = cache.GetOrBuild(1, 42, EarthRadiusMeters, CancellationToken.None);
        var cached = cache.GetOrBuild(1, 42, EarthRadiusMeters, CancellationToken.None);
        Assert.AreSame(stable, cached);
    }

    [TestMethod]
    public void GetOrBuild_CancelledRequest_DoesNotDiscardCachedDistribution()
    {
        var source = new CountingElevationSource();
        var cache = CreateCache(source);
        var first = cache.GetOrBuild(2, 42, EarthRadiusMeters, CancellationToken.None);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => cache.GetOrBuild(2, 42, EarthRadiusMeters, cts.Token));

        var restored = cache.GetOrBuild(2, 42, EarthRadiusMeters, CancellationToken.None);
        Assert.AreSame(first, restored);
    }

    private static PlanetHypsometryCache CreateCache(CountingElevationSource source, IPlanetTerrainDeformationStore? deformationStore = null)
        => new(new PlanetHypsometrySampler(source, deformationStore), source, deformationStore);

    private sealed class CountingElevationSource : IPlanetElevationRevisionSource
    {
        public long Revision { get; private set; }

        public int SampleCount { get; private set; }

        public bool PublishDuringNextSample { get; set; }

        public double SampleElevationMeters(PlanetVector direction, int seed)
        {
            SampleCount++;
            if (PublishDuringNextSample)
            {
                PublishDuringNextSample = false;
                PublishNewGeology();
            }
            return direction.Z * 1_000.0 + (Revision * 100.0) + seed;
        }

        public void PublishNewGeology() => Revision++;
    }

    private sealed class RevisionedDeformationStore : IPlanetTerrainDeformationStore
    {
        private int revision;

        public int GetRevision(int seed) => revision;

        public void Advance() => revision++;

        public double SampleElevationDeltaMeters(PlanetVector direction, int seed) => 0.0;

        public void Apply(int seed, IReadOnlyList<PlanetTerrainDeformation> deformations) => throw new NotSupportedException();

        public void ApplyDeposition(int seed, IReadOnlyList<PlanetTerrainDeposition> depositions) => throw new NotSupportedException();

        public PlanetTerrainEvolutionState Export(int seed) => PlanetTerrainEvolutionState.Empty;

        public void Restore(int seed, PlanetTerrainEvolutionState state) => throw new NotSupportedException();

        public void Clear(int seed) => revision = 0;

        public void ClearAll() => revision = 0;
    }
}
