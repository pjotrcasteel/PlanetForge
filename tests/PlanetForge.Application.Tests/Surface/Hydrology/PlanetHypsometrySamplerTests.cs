using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface.Hydrology;

[TestClass]
public sealed class PlanetHypsometrySamplerTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void Build_ConstantBedrock_RepresentsSphericalAreaAndExactWaterDepth()
    {
        var sampler = new PlanetHypsometrySampler(new ConstantElevationSource());

        var model = sampler.Build(4, 42, EarthRadiusMeters, CancellationToken.None);
        var expectedArea = 4.0 * Math.PI * EarthRadiusMeters * EarthRadiusMeters;
        var waterVolume = expectedArea * 100.0;

        Assert.AreEqual(expectedArea, model.TotalAreaSquareMeters, expectedArea * 1e-10);
        Assert.AreEqual(100.0, model.SolveSeaLevelMeters(waterVolume), 1e-7);
        Assert.AreEqual(1.0, model.GetFloodedAreaFraction(100.0), 1e-10);
    }

    [TestMethod]
    public void Build_SameSeedAndGrid_ProducesReproducibleSeaLevel()
    {
        var sampler = new PlanetHypsometrySampler(new VaryingElevationSource());
        var first = sampler.Build(3, 17, EarthRadiusMeters, CancellationToken.None);
        var second = sampler.Build(3, 17, EarthRadiusMeters, CancellationToken.None);

        Assert.AreEqual(first.TotalAreaSquareMeters, second.TotalAreaSquareMeters, 1e-2);
        Assert.AreEqual(first.SolveSeaLevelMeters(1.0e16), second.SolveSeaLevelMeters(1.0e16), 1e-8);
    }

    [TestMethod]
    public void Build_ChangingTerrainPublication_RejectsMixedEpoch()
    {
        var source = new RevisionedElevationSource();
        var sampler = new PlanetHypsometrySampler(source);

        Assert.ThrowsExactly<InvalidOperationException>(() => sampler.Build(3, 7, EarthRadiusMeters, CancellationToken.None));
    }

    [TestMethod]
    public void Build_PreCancelledToken_DoesNotSampleWorld()
    {
        var source = new CountingElevationSource();
        var sampler = new PlanetHypsometrySampler(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => sampler.Build(3, 7, EarthRadiusMeters, cancellation.Token));
        Assert.AreEqual(0, source.Samples);
    }

    private sealed class ConstantElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }

    private sealed class VaryingElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 2_000.0 * direction.Z + (seed % 100);
    }

    private sealed class RevisionedElevationSource : IPlanetElevationRevisionSource
    {
        public long Revision { get; private set; }

        public double SampleElevationMeters(PlanetVector direction, int seed)
        {
            Revision++;
            return direction.Z * 500.0;
        }
    }

    private sealed class CountingElevationSource : IPlanetElevationSource
    {
        public int Samples { get; private set; }

        public double SampleElevationMeters(PlanetVector direction, int seed)
        {
            Samples++;
            return 0.0;
        }
    }
}
