using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface.Hydrology;

[TestClass]
public sealed class PlanetRunoffModelTests
{
    private const double EarthRadiusMeters = 6_371_000.0;
    private const double AnnualRunoffMillimeters = 350.0;

    [TestMethod]
    public void Build_SpatialRunoff_PreservesRequestedMeanAcrossLand()
    {
        var hydrology = BuildDryWorld();

        var runoff = new PlanetRunoffModel().Build(hydrology, EarthRadiusMeters, -20_000.0, AnnualRunoffMillimeters, CancellationToken.None);
        var meanRunoff = runoff.Cells.Where((_, index) => !hydrology.Cells[index].IsOcean).Average(cell => cell.LocalAnnualRunoffMillimeters);

        Assert.AreEqual(AnnualRunoffMillimeters, meanRunoff, 0.000001);
    }

    [TestMethod]
    public void Build_EquatorialLand_ReceivesMoreRunoffThanPolarLandAtEqualElevation()
    {
        var hydrology = new PlanetHydrologyModelBuilder(new FlatElevationSource()).Build(3, 42, EarthRadiusMeters, -1_000.0, CancellationToken.None);

        var runoff = new PlanetRunoffModel().Build(hydrology, EarthRadiusMeters, -1_000.0, AnnualRunoffMillimeters, CancellationToken.None);
        var ordered = hydrology.Cells
            .Select((cell, index) => (cell, index, latitude: Math.Abs(PlanetSurfaceGridGeometry.GetCenterDirection(cell.Cell).Y)))
            .OrderBy(item => item.latitude)
            .ToArray();

        var equatorial = runoff.Cells[ordered[0].index];
        var polar = runoff.Cells[ordered[^1].index];

        Assert.IsGreaterThan(polar.LocalAnnualRunoffMillimeters, equatorial.LocalAnnualRunoffMillimeters);
    }

    [TestMethod]
    public void Build_DrainageDischarge_NeverDecreasesDownstreamAcrossLand()
    {
        var hydrology = BuildDryWorld();

        var runoff = new PlanetRunoffModel().Build(hydrology, EarthRadiusMeters, -20_000.0, AnnualRunoffMillimeters, CancellationToken.None);

        foreach (var cell in hydrology.Cells.Where(cell => cell.DrainageTarget is not null))
        {
            var source = runoff.GetCell(cell.Cell);
            var target = runoff.GetCell(cell.DrainageTarget!.Value);
            Assert.IsGreaterThanOrEqualTo(source.MeanDischargeCubicMetersPerSecond, target.MeanDischargeCubicMetersPerSecond);
        }
    }

    [TestMethod]
    public void Build_DryWorldSink_ReceivesTotalLandRunoffVolume()
    {
        var hydrology = BuildDryWorld();

        var runoff = new PlanetRunoffModel().Build(hydrology, EarthRadiusMeters, -20_000.0, AnnualRunoffMillimeters, CancellationToken.None);
        var sink = hydrology.Cells.Single(cell => cell.DrainageTarget is null);
        var expectedAnnualVolume = AnnualRunoffMillimeters / 1_000.0 * runoff.CellAreaSquareMeters * hydrology.Cells.LongCount(cell => !cell.IsOcean);

        Assert.AreEqual(expectedAnnualVolume, runoff.GetCell(sink.Cell).AccumulatedAnnualRunoffVolumeCubicMeters, expectedAnnualVolume * 1e-10);
    }

    [TestMethod]
    public void Build_MeltwaterForcing_IncreasesLocalAndDownstreamDischarge()
    {
        var hydrology = BuildDryWorld();
        var meltwater = Enumerable.Repeat(120.0, hydrology.Layout.CellCount).ToArray();

        var baseline = new PlanetRunoffModel().Build(hydrology, EarthRadiusMeters, -20_000.0, AnnualRunoffMillimeters, CancellationToken.None);
        var combined = new PlanetRunoffModel().Build(hydrology, EarthRadiusMeters, -20_000.0, AnnualRunoffMillimeters, meltwater, CancellationToken.None);
        var sink = hydrology.Cells.Single(cell => cell.DrainageTarget is null);

        Assert.IsTrue(combined.GetCell(sink.Cell).MeanDischargeCubicMetersPerSecond > baseline.GetCell(sink.Cell).MeanDischargeCubicMetersPerSecond);
        Assert.AreEqual(120.0, combined.Cells.First(cell => cell.LocalAnnualRunoffMillimeters > 0.0).LocalAnnualMeltwaterRunoffMillimeters, 0.000001);
    }

    [TestMethod]
    public void Build_PreCancelledToken_Throws()
    {
        var hydrology = BuildDryWorld();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            new PlanetRunoffModel().Build(hydrology, EarthRadiusMeters, -20_000.0, AnnualRunoffMillimeters, cancellationTokenSource.Token));
    }

    private static PlanetHydrologySnapshot BuildDryWorld()
        => new PlanetHydrologyModelBuilder(new DrySlopeElevationSource()).Build(3, 42, EarthRadiusMeters, -20_000.0, CancellationToken.None);

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 1_000.0;
    }

    private sealed class DrySlopeElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 2_000.0 + (direction.Z * 1_000.0) + (direction.X * 100.0);
    }
}
