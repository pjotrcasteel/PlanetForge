using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface.Hydrology;

[TestClass]
public sealed class PlanetOceanPhysicalAreaTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void PhysicalCellAreas_AllSixFaces_SumToFourPiSteradians()
    {
        foreach (var level in new[] { 0, 1, 3, 5 })
        {
            var layout = new PlanetSurfaceGridLayout(level);
            var solidAngle = 0.0;
            for (var index = 0; index < layout.CellCount; index++)
            {
                solidAngle += PlanetSurfaceCellArea.SolidAngleSteradians(layout.GetCell(index));
            }

            Assert.AreEqual(4.0 * Math.PI, solidAngle, 1e-10,
                $"Spherical cell areas must close without gaps or overlaps at level {level}.");
        }
    }

    [TestMethod]
    public void MeasureAreaWeighted_EntirelySubmergedPlanet_ReportsWholeSphericalSurface()
    {
        var source = new ConstantElevationSource(-500.0);
        var hydrology = new PlanetHydrologyModelBuilder(source)
            .Build(4, 17, EarthRadiusMeters, 0.0, CancellationToken.None);

        var result = PlanetOceanConnectivitySurvey.MeasureAreaWeighted(hydrology, EarthRadiusMeters, CancellationToken.None);
        var sphereArea = 4.0 * Math.PI * EarthRadiusMeters * EarthRadiusMeters;

        Assert.AreEqual(sphereArea, result.TotalAreaSquareMeters, sphereArea * 1e-10);
        Assert.AreEqual(sphereArea, result.OceanAreaSquareMeters, sphereArea * 1e-10);
        Assert.AreEqual(sphereArea, result.LargestConnectedOceanAreaSquareMeters, sphereArea * 1e-10);
        Assert.AreEqual(1.0, result.OceanAreaFraction, 1e-12);
        Assert.AreEqual(1.0, result.LargestConnectedOceanAreaShare, 1e-12);
        Assert.AreEqual(1, result.Topology.OceanComponents);
    }

    [TestMethod]
    public void MeasureAreaWeighted_HemisphericSea_IsOneApproximatelyHalfSurfaceAreaOcean()
    {
        var source = new HemisphericElevationSource();
        var hydrology = new PlanetHydrologyModelBuilder(source)
            .Build(5, 17, EarthRadiusMeters, 0.0, CancellationToken.None);

        var result = PlanetOceanConnectivitySurvey.MeasureAreaWeighted(hydrology, EarthRadiusMeters, CancellationToken.None);

        Assert.AreEqual(1, result.Topology.OceanComponents);
        Assert.AreEqual(0.5, result.OceanAreaFraction, 0.02);
        Assert.AreEqual(1.0, result.LargestConnectedOceanAreaShare, 1e-12);
    }

    [TestMethod]
    public void MeasureAreaWeighted_TwoDisjointBasins_AreReportedSeparately()
    {
        var source = new SeparatedPolarBasinsElevationSource();
        var hydrology = new PlanetHydrologyModelBuilder(source)
            .Build(4, 17, EarthRadiusMeters, 0.0, CancellationToken.None);

        var result = PlanetOceanConnectivitySurvey.MeasureAreaWeighted(hydrology, EarthRadiusMeters, CancellationToken.None);

        Assert.AreEqual(2, result.Topology.OceanComponents);
        Assert.IsTrue(result.OceanAreaFraction > 0.0 && result.OceanAreaFraction < 1.0);
        Assert.IsTrue(result.LargestConnectedOceanAreaShare > 0.40 && result.LargestConnectedOceanAreaShare < 0.60);
    }

    [TestMethod]
    public void MeasureAreaWeighted_PreCancelledToken_NeverReturnsAnIncompleteAreaEstimate()
    {
        var hydrology = new PlanetHydrologyModelBuilder(new ConstantElevationSource(-500.0))
            .Build(4, 17, EarthRadiusMeters, 0.0, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            PlanetOceanConnectivitySurvey.MeasureAreaWeighted(hydrology, EarthRadiusMeters, cancellation.Token));
    }

    [TestMethod]
    public void MeasureAreaWeighted_InvalidRadius_RejectsAreaCalculation()
    {
        var hydrology = new PlanetHydrologyModelBuilder(new ConstantElevationSource(-1.0))
            .Build(2, 17, EarthRadiusMeters, 0.0, CancellationToken.None);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOceanConnectivitySurvey.MeasureAreaWeighted(hydrology, 0.0, CancellationToken.None));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOceanConnectivitySurvey.MeasureAreaWeighted(hydrology, double.NaN, CancellationToken.None));
    }

    private sealed class ConstantElevationSource(double elevation) : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => elevation;
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
