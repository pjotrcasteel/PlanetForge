using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface.Hydrology;

[TestClass]
public sealed class PlanetOceanConnectivitySurveyTests
{
    private const double PlanetRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void Measure_DryWorld_HasNoOceansOrConnectedRegions()
    {
        var hydrology = new PlanetHydrologyModelBuilder(new ConstantElevationSource(500.0))
            .Build(3, 17, PlanetRadiusMeters, 0.0, TestContext.CancellationToken);

        var result = PlanetOceanConnectivitySurvey.Measure(hydrology, TestContext.CancellationToken);

        Assert.AreEqual(0, result.OceanCells);
        Assert.AreEqual(0, result.OceanComponents);
        Assert.AreEqual(0.0, result.LargestConnectedOceanShare, 0.0);
        Assert.AreEqual(result.TotalCells, result.LandCells);
    }

    [TestMethod]
    public void Measure_EntirelySubmergedWorld_IsOneConnectedGlobalOceanAcrossAllCubeFaces()
    {
        var hydrology = new PlanetHydrologyModelBuilder(new ConstantElevationSource(-500.0))
            .Build(4, 17, PlanetRadiusMeters, 0.0, TestContext.CancellationToken);

        var result = PlanetOceanConnectivitySurvey.Measure(hydrology, TestContext.CancellationToken);

        Assert.AreEqual(result.TotalCells, result.OceanCells);
        Assert.AreEqual(1, result.OceanComponents);
        Assert.AreEqual(result.TotalCells, result.LargestOceanComponentCells);
        Assert.AreEqual(1.0, result.LargestConnectedOceanShare, 0.0);
    }

    [TestMethod]
    public void Measure_OneHemisphereOcean_CrossesCubeFaceSeamsWithoutSplitting()
    {
        var hydrology = new PlanetHydrologyModelBuilder(new HemisphericElevationSource())
            .Build(4, 42, PlanetRadiusMeters, 0.0, TestContext.CancellationToken);

        var result = PlanetOceanConnectivitySurvey.Measure(hydrology, TestContext.CancellationToken);

        Assert.IsTrue(result.OceanCells > 0 && result.LandCells > 0);
        Assert.AreEqual(1, result.OceanComponents, "One continuous hemisphere must remain connected through cubed-sphere face seams.");
        Assert.AreEqual(1.0, result.LargestConnectedOceanShare, 0.0);
    }

    [TestMethod]
    public void Measure_TwoSeparatePolarBasins_AreNotPresentedAsOneOcean()
    {
        var hydrology = new PlanetHydrologyModelBuilder(new SeparatedPolarBasinsElevationSource())
            .Build(4, 42, PlanetRadiusMeters, 0.0, TestContext.CancellationToken);

        var result = PlanetOceanConnectivitySurvey.Measure(hydrology, TestContext.CancellationToken);

        Assert.AreEqual(2, result.OceanComponents);
        Assert.IsTrue(result.OceanCells > 0 && result.LargestOceanComponentCells < result.OceanCells);
        Assert.IsTrue(result.LargestConnectedOceanShare < 0.75);
    }

    [TestMethod]
    public void Measure_PreCancelledSurvey_NeverReturnsIncompleteRegionCounts()
    {
        var hydrology = new PlanetHydrologyModelBuilder(new ConstantElevationSource(-500.0))
            .Build(3, 17, PlanetRadiusMeters, 0.0, TestContext.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            PlanetOceanConnectivitySurvey.Measure(hydrology, cancellation.Token));
    }

    public TestContext TestContext { get; set; } = null!;

    private sealed class ConstantElevationSource(double elevationMeters) : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => elevationMeters;
    }

    private sealed class HemisphericElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => direction.Z > 0.0 ? -1_000.0 : 1_000.0;
    }

    private sealed class SeparatedPolarBasinsElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => Math.Abs(direction.Z) > 0.7 ? -1_000.0 : 1_000.0;
    }
}
