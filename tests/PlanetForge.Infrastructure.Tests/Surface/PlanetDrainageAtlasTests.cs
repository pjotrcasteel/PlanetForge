using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetDrainageAtlasTests
{
    [TestMethod]
    public void Build_SameBedrockGrid_ProducesIdenticalDrainageAndSediment()
    {
        var heights = SyntheticHighlands(32, 16);
        var first = PlanetDrainageAtlas.Build(32, 16, heights);
        var second = PlanetDrainageAtlas.Build(32, 16, heights);

        CollectionAssert.AreEqual(first.DownstreamIndices, second.DownstreamIndices);
        CollectionAssert.AreEqual(first.FilledElevationMeters, second.FilledElevationMeters);
        CollectionAssert.AreEqual(first.FlowAccumulation, second.FlowAccumulation);
        CollectionAssert.AreEqual(first.ChannelIncisionMeters, second.ChannelIncisionMeters);
        CollectionAssert.AreEqual(first.SedimentDepositionMeters, second.SedimentDepositionMeters);
        Assert.AreEqual(first.ExportedSedimentVolume, second.ExportedSedimentVolume);
    }

    [TestMethod]
    public void Build_LongitudeSeam_RoutesAcrossWrappedNeighbors()
    {
        var heights = Enumerable.Repeat(1_000f, 8 * 5).ToArray();
        heights[(2 * 8) + 7] = -100.0f;
        heights[2 * 8] = 20.0f;
        var drainage = PlanetDrainageAtlas.Build(8, 5, heights);

        Assert.AreEqual((2 * 8) + 7, drainage.DownstreamIndices[2 * 8]);
    }

    [TestMethod]
    public void Build_NorthPolarCap_RoutesAcrossOppositeMeridian()
    {
        var heights = Enumerable.Repeat(1_000f, 8 * 5).ToArray();
        heights[4] = -100.0f;
        heights[0] = 10.0f;
        var drainage = PlanetDrainageAtlas.Build(8, 5, heights);

        Assert.AreEqual(4, drainage.DownstreamIndices[0]);
    }

    [TestMethod]
    public void Build_FlatBasin_FollowsShortestFloodPathInsteadOfIndexOrder()
    {
        var heights = Enumerable.Repeat(100.0f, 16 * 9).ToArray();
        heights[4 * 16] = -1.0f;
        var drainage = PlanetDrainageAtlas.Build(16, 9, heights);
        var index = (4 * 16) + 6;
        var pathLength = 0;

        while (drainage.DownstreamIndices[index] >= 0)
        {
            index = drainage.DownstreamIndices[index];
            pathLength++;
        }

        Assert.AreEqual(4 * 16, index);
        Assert.IsLessThanOrEqualTo(6, pathLength);
    }

    [TestMethod]
    public void Build_ClosedDepressions_FillsRoutingSurfaceWithoutModifyingBedrock()
    {
        var heights = Enumerable.Repeat(1_000f, 9 * 7).ToArray();
        heights[0] = -50.0f;
        heights[(3 * 9) + 4] = 10.0f;
        var drainage = PlanetDrainageAtlas.Build(9, 7, heights);
        var pit = (3 * 9) + 4;

        Assert.IsGreaterThan(heights[pit], drainage.FilledElevationMeters[pit]);
        Assert.AreEqual(10.0f, heights[pit]);

        for (var index = 0; index < heights.Length; index++)
        {
            var receiver = drainage.DownstreamIndices[index];
            if (receiver < 0)
            {
                continue;
            }

            Assert.IsLessThanOrEqualTo(drainage.FilledElevationMeters[index],
                drainage.FilledElevationMeters[receiver], $"Cell {index} flows uphill on its filled routing surface.");
        }
    }

    [TestMethod]
    public void Build_CanonicalRouting_AllPathsReachOutletsWithoutCycles()
    {
        var heights = SyntheticHighlands(32, 16);
        var drainage = PlanetDrainageAtlas.Build(32, 16, heights);

        for (var i = 0; i < heights.Length; i++)
        {
            var index = i;
            var traversed = 0;
            while (drainage.DownstreamIndices[index] >= 0)
            {
                index = drainage.DownstreamIndices[index];
                traversed++;
                Assert.IsLessThanOrEqualTo(heights.Length, traversed, $"Drainage cycle from cell {i}.");
            }

            Assert.IsLessThanOrEqualTo(0.0f, heights[index], $"Cell {i} did not reach an ocean outlet.");
        }
    }

    [TestMethod]
    public void Build_UniformRainfall_ConservesAreaWeightedRunoff()
    {
        var width = 32;
        var height = 16;
        var drainage = PlanetDrainageAtlas.Build(width, height, SyntheticHighlands(width, height));
        var supplied = 0.0;
        var drained = 0.0;
        for (var y = 0; y < height; y++)
        {
            supplied += width * Math.Cos(Math.PI * (0.5 - ((y + 0.5) / height)));
        }

        for (var index = 0; index < drainage.DownstreamIndices.Length; index++)
        {
            if (drainage.DownstreamIndices[index] < 0)
            {
                drained += drainage.FlowAccumulation[index];
            }
        }

        Assert.IsLessThan(0.05, Math.Abs(supplied - drained));
    }

    [TestMethod]
    public void Build_StreamPowerAndSediment_BoundErosionAndConserveSediment()
    {
        var width = 32;
        var height = 16;
        var drainage = PlanetDrainageAtlas.Build(width, height, SyntheticHighlands(width, height));
        var eroded = 0.0;
        var deposited = 0.0;

        for (var i = 0; i < width * height; i++)
        {
            var latitudeArea = Math.Cos(Math.PI * (0.5 - (((i / width) + 0.5) / height)));
            Assert.IsGreaterThanOrEqualTo(0.0f, drainage.ChannelIncisionMeters[i]);
            Assert.IsLessThanOrEqualTo(240.0f, drainage.ChannelIncisionMeters[i]);
            Assert.IsGreaterThanOrEqualTo(0.0f, drainage.SedimentDepositionMeters[i]);
            Assert.IsLessThanOrEqualTo(240.0f, drainage.SedimentDepositionMeters[i]);
            eroded += drainage.ChannelIncisionMeters[i] * latitudeArea;
            deposited += drainage.SedimentDepositionMeters[i] * latitudeArea;
        }

        Assert.IsGreaterThan(0.0, eroded, "No drainage-controlled erosion occurred.");
        Assert.IsLessThan(0.01, Math.Abs(eroded - deposited - drainage.ExportedSedimentVolume),
            "Sediment must be deposited or exported, never silently created or lost.");
    }

    [TestMethod]
    public void Build_DryPlanet_RoutesToGlobalEndorheicMinimum()
    {
        var heights = Enumerable.Repeat(2_000.0f, 8 * 5).ToArray();
        heights[18] = 50.0f;
        var drainage = PlanetDrainageAtlas.Build(8, 5, heights);

        Assert.AreEqual(-1, drainage.DownstreamIndices[18]);
        Assert.AreEqual(1, drainage.DownstreamIndices.Count(receiver => receiver < 0));
    }

    private static float[] SyntheticHighlands(int width, int height)
    {
        var elevations = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var ridge = 2_400.0 * Math.Exp(-Math.Pow((x - width * 0.45) / (width * 0.23), 2.0));
                var valleys = 370.0 * Math.Sin((y + x * 0.3) * 0.85);
                elevations[(y * width) + x] = x <= 2 ? -300.0f : (float)(ridge + valleys + 180.0);
            }
        }

        return elevations;
    }
}
