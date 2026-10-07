using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface.Hydrology;

[TestClass]
public sealed class PlanetHydrologyFeatureExtractorTests
{
    private const double EarthRadiusMeters = 6_371_000.0;
    private const double SeaLevelMeters = -500.0;
    private const double AnnualRunoffMillimeters = 350.0;
    private const double SecondsPerYear = 365.25 * 24.0 * 60.0 * 60.0;

    [TestMethod]
    public void Extract_BasinWorld_PartitionsAllLandIntoWatersheds()
    {
        var hydrology = BuildBasinWorld();
        var runoff = BuildRunoff(hydrology);
        var features = new PlanetHydrologyFeatureExtractor().Extract(hydrology, runoff, MinimumTwoCellDischarge(runoff), CancellationToken.None);
        var landCellCount = hydrology.Cells.LongCount(cell => !cell.IsOcean);

        Assert.IsTrue(features.Watersheds.Count > 0);
        Assert.AreEqual(landCellCount, features.Watersheds.Sum(watershed => watershed.LandCellCount));
        Assert.IsTrue(features.Watersheds.All(watershed => watershed.OutletContributingLandCellCount >= watershed.LandCellCount));
    }

    [TestMethod]
    public void Extract_ClosedBasin_ProducesLakeAtSpillElevation()
    {
        var hydrology = BuildBasinWorld();
        var runoff = BuildRunoff(hydrology);
        var features = new PlanetHydrologyFeatureExtractor().Extract(hydrology, runoff, MinimumTwoCellDischarge(runoff), CancellationToken.None);

        Assert.IsTrue(features.Lakes.Count > 0);
        Assert.IsTrue(features.Lakes.Any(lake => lake.MaximumDepthMeters >= 900.0));
        Assert.IsTrue(features.Lakes.All(lake => lake.Cells.Count > 0));
    }

    [TestMethod]
    public void Extract_RiverNetwork_FollowsExistingDrainageTargetsWithPhysicalDischarge()
    {
        var hydrology = BuildBasinWorld();
        var runoff = BuildRunoff(hydrology);
        var features = new PlanetHydrologyFeatureExtractor().Extract(hydrology, runoff, MinimumTwoCellDischarge(runoff), CancellationToken.None);

        Assert.IsTrue(features.RiverSegments.Count > 0);
        foreach (var segment in features.RiverSegments)
        {
            var source = hydrology.GetCell(segment.From);
            var target = hydrology.GetCell(segment.To);
            Assert.AreEqual(source.DrainageTarget, segment.To);
            Assert.IsTrue(segment.ContributingLandCellCount >= 2);
            Assert.IsTrue(segment.DrainageAreaSquareMeters >= runoff.CellAreaSquareMeters * 2.0);
            Assert.IsTrue(segment.MeanDischargeCubicMetersPerSecond > 0.0);
            Assert.IsTrue(segment.StrahlerOrder >= 1);
            Assert.IsTrue(target.ContributingLandCellCount >= source.ContributingLandCellCount || target.IsOcean);
        }
    }

    [TestMethod]
    public void Extract_DischargeThresholdAboveMaximum_ProducesNoRiverSegments()
    {
        var hydrology = BuildBasinWorld();
        var runoff = BuildRunoff(hydrology);
        var maximumDischarge = runoff.Cells.Max(cell => cell.MeanDischargeCubicMetersPerSecond);

        var features = new PlanetHydrologyFeatureExtractor().Extract(hydrology, runoff, maximumDischarge + 1.0, CancellationToken.None);

        Assert.AreEqual(0, features.RiverSegments.Count);
    }

    [TestMethod]
    public void Extract_SameHydrologyAndRunoff_IsDeterministic()
    {
        var hydrology = BuildBasinWorld();
        var runoff = BuildRunoff(hydrology);
        var extractor = new PlanetHydrologyFeatureExtractor();
        var minimumDischarge = MinimumTwoCellDischarge(runoff);

        var first = extractor.Extract(hydrology, runoff, minimumDischarge, CancellationToken.None);
        var second = extractor.Extract(hydrology, runoff, minimumDischarge, CancellationToken.None);

        CollectionAssert.AreEqual(first.Watersheds.Select(watershed => watershed.Outlet).ToArray(), second.Watersheds.Select(watershed => watershed.Outlet).ToArray());
        CollectionAssert.AreEqual(first.Lakes.Select(lake => lake.Id).ToArray(), second.Lakes.Select(lake => lake.Id).ToArray());
        CollectionAssert.AreEqual(first.RiverSegments.Select(segment => segment.From).ToArray(), second.RiverSegments.Select(segment => segment.From).ToArray());
        CollectionAssert.AreEqual(
            first.RiverSegments.Select(segment => segment.MeanDischargeCubicMetersPerSecond).ToArray(),
            second.RiverSegments.Select(segment => segment.MeanDischargeCubicMetersPerSecond).ToArray());
    }

    [TestMethod]
    public void Extract_PreCancelledToken_Throws()
    {
        var hydrology = BuildBasinWorld();
        var runoff = BuildRunoff(hydrology);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            new PlanetHydrologyFeatureExtractor().Extract(hydrology, runoff, MinimumTwoCellDischarge(runoff), cancellationTokenSource.Token));
    }

    private static PlanetHydrologySnapshot BuildBasinWorld()
        => new PlanetHydrologyModelBuilder(new BasinElevationSource()).Build(4, 42, EarthRadiusMeters, SeaLevelMeters, CancellationToken.None);

    private static PlanetRunoffSnapshot BuildRunoff(PlanetHydrologySnapshot hydrology)
        => new PlanetRunoffModel().Build(hydrology, EarthRadiusMeters, SeaLevelMeters, AnnualRunoffMillimeters, CancellationToken.None);

    private static double MinimumTwoCellDischarge(PlanetRunoffSnapshot runoff)
        => runoff.CellAreaSquareMeters * (AnnualRunoffMillimeters / 1_000.0) / SecondsPerYear * 1.5;

    private sealed class BasinElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed)
        {
            if (direction.Z < -0.45)
            {
                return -1_000.0;
            }

            if (direction.Z > 0.80)
            {
                return 0.0;
            }

            return 1_000.0;
        }
    }
}
