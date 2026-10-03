using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface.Hydrology;

[TestClass]
public sealed class PlanetHydrologyFeatureExtractorTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void Extract_BasinWorld_PartitionsAllLandIntoWatersheds()
    {
        var hydrology = BuildBasinWorld();
        var features = new PlanetHydrologyFeatureExtractor().Extract(hydrology, 2, CancellationToken.None);
        var landCellCount = hydrology.Cells.LongCount(cell => !cell.IsOcean);

        Assert.IsTrue(features.Watersheds.Count > 0);
        Assert.AreEqual(landCellCount, features.Watersheds.Sum(watershed => watershed.LandCellCount));
        Assert.IsTrue(features.Watersheds.All(watershed => watershed.OutletContributingLandCellCount >= watershed.LandCellCount));
    }

    [TestMethod]
    public void Extract_ClosedBasin_ProducesLakeAtSpillElevation()
    {
        var hydrology = BuildBasinWorld();
        var features = new PlanetHydrologyFeatureExtractor().Extract(hydrology, 2, CancellationToken.None);

        Assert.IsTrue(features.Lakes.Count > 0);
        Assert.IsTrue(features.Lakes.Any(lake => lake.MaximumDepthMeters >= 900.0));
        Assert.IsTrue(features.Lakes.All(lake => lake.Cells.Count > 0));
    }

    [TestMethod]
    public void Extract_RiverNetwork_FollowsExistingDrainageTargets()
    {
        var hydrology = BuildBasinWorld();
        var features = new PlanetHydrologyFeatureExtractor().Extract(hydrology, 2, CancellationToken.None);

        Assert.IsTrue(features.RiverSegments.Count > 0);
        foreach (var segment in features.RiverSegments)
        {
            var source = hydrology.GetCell(segment.From);
            var target = hydrology.GetCell(segment.To);
            Assert.AreEqual(source.DrainageTarget, segment.To);
            Assert.IsTrue(segment.ContributingLandCellCount >= 2);
            Assert.IsTrue(segment.StrahlerOrder >= 1);
            Assert.IsTrue(target.ContributingLandCellCount >= source.ContributingLandCellCount || target.IsOcean);
        }
    }

    [TestMethod]
    public void Extract_RiverThresholdAboveMaximumContribution_ProducesNoRiverSegments()
    {
        var hydrology = BuildBasinWorld();
        var maximumContribution = hydrology.Cells.Max(cell => cell.ContributingLandCellCount);

        var features = new PlanetHydrologyFeatureExtractor().Extract(hydrology, maximumContribution + 1, CancellationToken.None);

        Assert.AreEqual(0, features.RiverSegments.Count);
    }

    [TestMethod]
    public void Extract_SameHydrology_IsDeterministic()
    {
        var hydrology = BuildBasinWorld();
        var extractor = new PlanetHydrologyFeatureExtractor();

        var first = extractor.Extract(hydrology, 3, CancellationToken.None);
        var second = extractor.Extract(hydrology, 3, CancellationToken.None);

        CollectionAssert.AreEqual(first.Watersheds.Select(watershed => watershed.Outlet).ToArray(), second.Watersheds.Select(watershed => watershed.Outlet).ToArray());
        CollectionAssert.AreEqual(first.Lakes.Select(lake => lake.Id).ToArray(), second.Lakes.Select(lake => lake.Id).ToArray());
        CollectionAssert.AreEqual(first.RiverSegments.Select(segment => segment.From).ToArray(), second.RiverSegments.Select(segment => segment.From).ToArray());
    }

    [TestMethod]
    public void Extract_PreCancelledToken_Throws()
    {
        var hydrology = BuildBasinWorld();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            new PlanetHydrologyFeatureExtractor().Extract(hydrology, 2, cancellationTokenSource.Token));
    }

    private static PlanetHydrologySnapshot BuildBasinWorld() =>
        new PlanetHydrologyModelBuilder(new BasinElevationSource()).Build(4, 42, EarthRadiusMeters, -500.0, CancellationToken.None);

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
