using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetRegionalWatershedTests
{

    [TestMethod]
    [DataRow(8f)]
    [DataRow(10f)]
    [DataRow(12f)]
    public void Build_DepositionThresholds_AreContinuousAndConserveSediment(float runoff)
    {
        const int width = 33;
        const int center = 16 * width + 16;
        var heights = new float[width * width];
        for (var y = 0; y < width; y++)
        {
            for (var x = 0; x < width; x++)
            {
                heights[y * width + x] = 2_000f + (y * 5f) + (x * 0.01f);
            }
        }

        PlanetRegionalWatershed Solve(float water)
        {
            var rainfall = new float[heights.Length];
            rainfall[center] = water;
            var rock = Enumerable.Repeat(3f, heights.Length).ToArray();
            return PlanetRegionalWatershed.Build(width, width, 1_000.0, heights, rainfall, rock);
        }

        var before = Solve(runoff - 0.0001f);
        var after = Solve(runoff + 0.0001f);
        Assert.IsLessThan(0.05f,
            Math.Abs(after.SedimentDepositionMeters[center] - before.SedimentDepositionMeters[center]),
            "A small runoff change must not deposit an abrupt floodplain shelf.");
        foreach (var result in new[] { before, after })
        {
            Assert.AreEqual(result.ErodedVolumeCubicMeters,
                result.DepositedVolumeCubicMeters + result.ExportedVolumeCubicMeters,
                Math.Max(1.0, result.ErodedVolumeCubicMeters * 1e-6));
        }
        if (runoff == 8f)
        {
            Assert.AreEqual(0f, before.SedimentDepositionMeters[center]);
            Assert.IsLessThan(0.001f, after.SedimentDepositionMeters[center]);
        }
    }

    [TestMethod]
    [DataRow(5f)]
    [DataRow(6f)]
    [DataRow(12f)]
    public void Build_RunoffCrossesChannelOnset_IncisionRemainsContinuous(float runoff)
    {
        const int center = 16 * 33 + 16;
        var below = OneWetHillslopeSource(33, 1_000, runoff - 0.001f);
        var above = OneWetHillslopeSource(33, 1_000, runoff + 0.001f);

        Assert.IsLessThan(0.02f, Math.Abs(above.IncisionMeters[center] - below.IncisionMeters[center]),
            "An arbitrarily small runoff change must not create a metre-scale channel-head step.");
        Assert.IsGreaterThanOrEqualTo(below.IncisionMeters[center], above.IncisionMeters[center]);
        if (runoff == 5f)
        {
            Assert.AreEqual(0f, below.IncisionMeters[center]);
            Assert.IsLessThan(0.0001f, above.IncisionMeters[center]);
        }
    }

    [TestMethod]
    public void Build_SameBedrockAndResolution_IsDeterministic()
    {
        var elevations = CreateMountainBasin(32, 24);
        var a = PlanetRegionalWatershed.Build(32, 24, 2000, elevations);
        var b = PlanetRegionalWatershed.Build(32, 24, 2000, elevations);

        CollectionAssert.AreEqual(a.DownstreamIndices, b.DownstreamIndices);
        CollectionAssert.AreEqual(a.SecondaryDownstreamIndices, b.SecondaryDownstreamIndices);
        CollectionAssert.AreEqual(a.SecondaryFlowFractions, b.SecondaryFlowFractions);
        CollectionAssert.AreEqual(a.FilledRoutingElevationMeters, b.FilledRoutingElevationMeters);
        CollectionAssert.AreEqual(a.AccumulatedRunoffCells, b.AccumulatedRunoffCells);
        CollectionAssert.AreEqual(a.IncisionMeters, b.IncisionMeters);
        CollectionAssert.AreEqual(a.SedimentDepositionMeters, b.SedimentDepositionMeters);
        Assert.AreEqual(a.ExportedVolumeCubicMeters, b.ExportedVolumeCubicMeters);
    }

    [TestMethod]
    public void Build_ClosedDepression_RoutesAcrossLowestSpillwayWithoutChangingSource()
    {
        var heights = Enumerable.Repeat(1100f, 24 * 24).ToArray();
        heights[12 * 24 + 12] = 80f;
        for (var x = 0; x < 24; x++)
        {
            heights[12 * 24 + x] = 150 + 20 * x;
        }

        var basin = PlanetRegionalWatershed.Build(24, 24, 500, heights);
        var pit = (11 * 24) + 12;
        Assert.IsGreaterThanOrEqualTo(heights[pit], basin.FilledRoutingElevationMeters[pit]);
        Assert.AreEqual(1100f, heights[pit]);
        Assert.IsTrue(basin.DownstreamIndices[12 * 24 + 12] >= 0);
    }

    [TestMethod]
    public void Build_ObliqueHillslope_DistributesFlowBetweenAdjacentDownhillDirections()
    {
        const int width = 28;
        const int height = 24;
        var bedrock = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bedrock[y * width + x] = 2000f + (width - x) * 35f + (height - y) * 15f;
            }
        }

        var watershed = PlanetRegionalWatershed.Build(width, height, 1500, bedrock);
        var splitCells = 0;
        var outletRunoff = 0.0;
        for (var index = 0; index < bedrock.Length; index++)
        {
            if (watershed.DownstreamIndices[index] < 0)
            {
                outletRunoff += watershed.AccumulatedRunoffCells[index];
            }

            if (watershed.SecondaryDownstreamIndices[index] >= 0)
            {
                splitCells++;
                Assert.IsGreaterThan(0.0f, watershed.SecondaryFlowFractions[index]);
                Assert.IsLessThanOrEqualTo(0.5f, watershed.SecondaryFlowFractions[index]);
            }
        }

        Assert.IsGreaterThan(0, splitCells, "The solver fell back to grid-locked D8 routing everywhere.");
        Assert.AreEqual(width * height, outletRunoff, 0.005);
    }

    [TestMethod]
    public void Build_HighPerimeterRidge_DoesNotTurnTheRidgeIntoAnArtificialOutlet()
    {
        const int width = 24;
        const int height = 24;
        var heights = Enumerable.Repeat(1200f, width * height).ToArray();
        for (var x = 0; x < width; x++)
        {
            heights[x] = (float)(2000 + 600 * Math.Sin(Math.PI * x / (width - 1)));
        }

        var watershed = PlanetRegionalWatershed.Build(width, height, 2000, heights);
        var perimeterPeak = width / 2;

        Assert.IsGreaterThanOrEqualTo(0, watershed.DownstreamIndices[perimeterPeak],
            "A high edge ridge is part of the catchment, not an outlet by definition.");
    }

    [TestMethod]
    public void Build_FlatPlateau_ProducesAcyclicShortestPathsToEdges()
    {
        const int width = 48;
        const int height = 32;
        var heights = Enumerable.Repeat(1000f, width * height).ToArray();
        var basin = PlanetRegionalWatershed.Build(width, height, 6000, heights);
        var center = (height / 2) * width + (width / 2);
        var steps = 0;
        var current = center;

        while (basin.DownstreamIndices[current] >= 0)
        {
            current = basin.DownstreamIndices[current];
            steps++;
            Assert.IsLessThanOrEqualTo(width * height, steps, "A drainage route contains a cycle.");
        }

        Assert.IsLessThanOrEqualTo(Math.Min(width / 2, height / 2), steps);
        Assert.IsTrue(current / width == 0 || current / width == height - 1 ||
            current % width == 0 || current % width == width - 1);
    }

    [TestMethod]
    public void Build_MountainBasin_ConservesRainfallAndSedimentInPhysicalUnits()
    {
        var basin = PlanetRegionalWatershed.Build(48, 32, 6000, CreateMountainBasin(48, 32));
        var runoffAtOutlets = 0.0;
        var channelCount = 0;

        for (var i = 0; i < basin.DownstreamIndices.Length; i++)
        {
            var downstream = basin.DownstreamIndices[i];
            if (downstream < 0)
            {
                runoffAtOutlets += basin.AccumulatedRunoffCells[i];
            }
            else
            {
                Assert.IsGreaterThanOrEqualTo(basin.FilledRoutingElevationMeters[downstream],
                    basin.FilledRoutingElevationMeters[i], $"Cell {i} routes uphill.");
            }

            if (basin.IncisionMeters[i] > 0f)
            {
                channelCount++;
                Assert.IsLessThanOrEqualTo(180.0f, basin.IncisionMeters[i]);
            }

            Assert.IsGreaterThanOrEqualTo(0.0f, basin.SedimentDepositionMeters[i]);
        }

        Assert.IsLessThan(0.002, Math.Abs((48 * 32) - runoffAtOutlets));
        Assert.IsGreaterThan(0, channelCount);
        Assert.IsGreaterThan(0.0, basin.ErodedVolumeCubicMeters);
        Assert.IsLessThan(basin.ErodedVolumeCubicMeters * 1e-6,
            Math.Abs(basin.ErodedVolumeCubicMeters - basin.DepositedVolumeCubicMeters - basin.ExportedVolumeCubicMeters));
    }

    [TestMethod]
    public void Build_SpatialRainfall_ConservesPrecipitationAndSuppressesErosionInDrought()
    {
        const int width = 48;
        const int height = 32;
        var bedrock = CreateMountainBasin(width, height);
        var rainfall = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width / 2; x++)
            {
                rainfall[y * width + x] = 2.0f;
            }
        }

        var wet = PlanetRegionalWatershed.Build(width, height, 3000, bedrock, rainfall);
        var dry = PlanetRegionalWatershed.Build(width, height, 3000, bedrock, new float[width * height]);
        var totalRunoff = wet.DownstreamIndices.Select((receiver, i) =>
            receiver < 0 ? wet.AccumulatedRunoffCells[i] : 0f).Sum(value => (double)value);

        Assert.AreEqual(width * height, totalRunoff, 0.01);
        Assert.AreEqual(0.0, dry.ErodedVolumeCubicMeters);
        Assert.AreEqual(0.0, dry.ExportedVolumeCubicMeters);
    }

    [TestMethod]
    public void Build_SteepConnectedChannel_TransportsSedimentInsteadOfBlanketingHillsides()
    {
        const int width = 32;
        const int height = 32;
        const double spacing = 500.0;
        var bedrock = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bedrock[y * width + x] = 9000f - 175f * y + 0.1f * x;
            }
        }

        var watershed = PlanetRegionalWatershed.Build(width, height, spacing, bedrock);

        Assert.IsGreaterThan(0.0, watershed.ErodedVolumeCubicMeters);
        Assert.IsLessThanOrEqualTo(24.01f, watershed.SedimentDepositionMeters.Max(),
            "One numerical pass must never deposit an unbounded 100 m material blanket.");
        for (var index = 0; index < bedrock.Length; index++)
        {
            if (watershed.SedimentDepositionMeters[index] <= 0f)
            {
                continue;
            }

            var primary = watershed.DownstreamIndices[index];
            Assert.IsGreaterThanOrEqualTo(0, primary);
            Assert.IsGreaterThanOrEqualTo(8f, watershed.AccumulatedRunoffCells[index]);
            var secondary = watershed.SecondaryDownstreamIndices[index];
            var fraction = watershed.SecondaryFlowFractions[index];
            var primarySlope = Math.Max(0.0, watershed.FilledRoutingElevationMeters[index] -
                watershed.FilledRoutingElevationMeters[primary]) /
                (spacing * (index % width != primary % width && index / width != primary / width ? Math.Sqrt(2) : 1));
            var secondarySlope = secondary < 0 ? 0.0 : Math.Max(0.0,
                watershed.FilledRoutingElevationMeters[index] - watershed.FilledRoutingElevationMeters[secondary]) /
                (spacing * (index % width != secondary % width && index / width != secondary / width ? Math.Sqrt(2) : 1));
            var effectiveSlope = primarySlope * (1.0 - fraction) + secondarySlope * fraction;
            Assert.IsLessThanOrEqualTo(0.03501, effectiveSlope,
                "Sediment may settle in flat floodplain reaches, not on steep moving channels.");
        }

        Assert.AreEqual(watershed.ErodedVolumeCubicMeters,
            watershed.DepositedVolumeCubicMeters + watershed.ExportedVolumeCubicMeters,
            Math.Max(1.0, watershed.ErodedVolumeCubicMeters * 1e-5));
    }

    [TestMethod]
    public void Build_SameRunoffPerUnitWidth_ProducesComparableUncappedIncision()
    {
        // Equal flow per metre across 1000 m and 500 m strips: 7 km² / 1 km
        // equals 3.5 km² / 0.5 km. Rainfall counts alone are not comparable.
        var coarse = OneWetHillslopeSource(33, 1_000.0, 7f);
        var fine = OneWetHillslopeSource(65, 500.0, 14f);
        var coarseCenter = 16 * 33 + 16;
        var fineCenter = 32 * 65 + 32;

        Assert.IsGreaterThan(0f, coarse.IncisionMeters[coarseCenter]);
        Assert.AreEqual(coarse.IncisionMeters[coarseCenter], fine.IncisionMeters[fineCenter], 0.001f,
            "Refining an equally supplied flow strip must preserve incision depth below the numerical cap.");
        Assert.IsLessThan(12.0f, coarse.IncisionMeters[coarseCenter]);
        Assert.IsLessThanOrEqualTo(12.0f, coarse.IncisionMeters.Max());
        Assert.IsLessThanOrEqualTo(12.0f, fine.IncisionMeters.Max());
    }

    [TestMethod]
    public void Build_SteepLargeCatchment_CapsNumericalIncisionAndDepositionAndConservesMass()
    {
        const int width = 48;
        var heights = new float[width * width];
        for (var y = 0; y < width; y++)
        {
            for (var x = 0; x < width; x++)
            {
                heights[y * width + x] = 2_500f + 90f * (width - y) + 24f * (width - x);
            }
        }

        var watershed = PlanetRegionalWatershed.Build(width, width, 500.0, heights);
        Assert.IsGreaterThan(0.0, watershed.ErodedVolumeCubicMeters);
        Assert.IsLessThanOrEqualTo(12.001f, watershed.IncisionMeters.Max(),
            "A numerical pass must not gouge 180 m into a single coarse raster cell.");
        Assert.IsLessThanOrEqualTo(6.001f, watershed.SedimentDepositionMeters.Max(),
            "Floodplains must not accumulate 24 m of material in one numerical pass.");
        Assert.AreEqual(watershed.ErodedVolumeCubicMeters,
            watershed.DepositedVolumeCubicMeters + watershed.ExportedVolumeCubicMeters,
            Math.Max(1.0, watershed.ErodedVolumeCubicMeters * 1e-5));
    }

    [TestMethod]
    [DataRow(0.0, 0.0)]
    [DataRow(0.013, 0.0)]
    [DataRow(0.0, 0.0000005)]
    public void Build_RefinedPhysicalHillslope_ConvergesWithoutHittingIncisionCap(double crossSlope, double curvature)
    {
        var volumes = new List<double>();
        foreach (var width in new[] { 65, 129, 257 })
        {
            var heights = PhysicalCatchment(width, crossSlope, curvature);
            var rock = Enumerable.Repeat(0.005f, heights.Length).ToArray();
            var result = PlanetRegionalWatershed.Build(width, width, 128_000.0 / (width - 1), heights, erodibilityCellWeights: rock);
            Assert.IsGreaterThan(0.0, result.ErodedVolumeCubicMeters);
            Assert.IsLessThan(12f, result.IncisionMeters.Max(), "Saturation must not conceal resolution dependence.");
            Assert.AreEqual(result.ErodedVolumeCubicMeters, result.DepositedVolumeCubicMeters + result.ExportedVolumeCubicMeters,
                result.ErodedVolumeCubicMeters * 1e-6);
            volumes.Add(result.ErodedVolumeCubicMeters);
        }

        var coarseChange = Math.Abs(volumes[0] - volumes[1]);
        var fineChange = Math.Abs(volumes[1] - volumes[2]);
        Assert.IsLessThan(coarseChange, fineChange, "Successive refinement must reduce the integration error.");
        Assert.AreEqual(volumes[1], volumes[2], volumes[2] * 0.05,
            "Halving 1 km cells must preserve uncapped bulk erosion within the 5% research benchmark.");
    }

    [TestMethod]
    [DataRow(0.0, 0.0)]
    [DataRow(0.013, 0.0)]
    [DataRow(0.0, 0.0000005)]
    public void Advance_RefinedPhysicalCatchment_PreservesSixStepSedimentExport(double crossSlope, double curvature)
    {
        var exports = new List<double>();
        foreach (var width in new[] { 129, 257 })
        {
            var initial = PlanetRegionalGeologyEvolution.Initialize(1, "physical-refinement", width, width,
                128_000.0 / (width - 1), PhysicalCatchment(width, crossSlope, curvature));
            var evolved = PlanetRegionalGeologyEvolution.Advance(initial, 6);
            Assert.IsGreaterThan(0.0, evolved.CumulativeExportedVolumeCubicMeters);
            Assert.AreEqual(evolved.CumulativeErodedVolumeCubicMeters,
                evolved.CumulativeDepositedVolumeCubicMeters + evolved.CumulativeExportedVolumeCubicMeters,
                evolved.CumulativeErodedVolumeCubicMeters * 1e-6);
            exports.Add(evolved.CumulativeExportedVolumeCubicMeters);
        }

        Assert.AreEqual(exports[0], exports[1], exports[1] * 0.03,
            "Matched 128 km benchmark catchments must agree within 3% after six iterations.");
    }

    private static float[] PhysicalCatchment(int width, double crossSlope, double curvature)
    {
        var spacing = 128_000.0 / (width - 1);
        var heights = new float[width * width];
        for (var y = 0; y < width; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var east = (x - (width - 1) * 0.5) * spacing;
                heights[y * width + x] = (float)(1_000 + y * spacing * 0.02 + crossSlope * x * spacing + curvature * east * east);
            }
        }

        return heights;
    }

    private static PlanetRegionalWatershed OneWetHillslopeSource(int width, double spacingMeters, float rainfall)
    {
        var heights = new float[width * width];
        var precipitation = new float[width * width];
        for (var y = 0; y < width; y++)
        {
            for (var x = 0; x < width; x++)
            {
                heights[y * width + x] = (float)(2_000.0 +
                    (width - 1 - y) * spacingMeters * 0.02 +
                    (width - 1 - x) * spacingMeters * 0.002);
            }
        }

        precipitation[(width / 2) * width + width / 2] = rainfall;
        return PlanetRegionalWatershed.Build(width, width, spacingMeters, heights, precipitation);
    }

    [TestMethod]
    public void Build_InvalidRainfall_RejectsNegativeAndNonFiniteWeights()
    {
        var heights = Enumerable.Repeat(100f, 10 * 10).ToArray();
        var rainfall = Enumerable.Repeat(1f, 10 * 10).ToArray();
        rainfall[10] = -1f;
        Assert.ThrowsExactly<ArgumentException>(() => PlanetRegionalWatershed.Build(10, 10, 1000, heights, rainfall));

        rainfall[10] = float.NaN;
        Assert.ThrowsExactly<ArgumentException>(() => PlanetRegionalWatershed.Build(10, 10, 1000, heights, rainfall));
    }

    [TestMethod]
    public void Build_NonFiniteElevation_RejectsGeologicalInput()
    {
        var heights = Enumerable.Repeat(100f, 10 * 10).ToArray();
        heights[42] = float.NaN;
        Assert.ThrowsExactly<ArgumentException>(() => PlanetRegionalWatershed.Build(10, 10, 1000, heights));
    }

    private static float[] CreateMountainBasin(int width, int height)
    {
        var heights = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var range = 2700.0 * Math.Exp(-Math.Pow((x - width * 0.6) / (width * 0.18), 2.0));
                var gullies = 360.0 * Math.Sin((y + x * 0.65) * 0.49);
                heights[y * width + x] = (float)(range + gullies - 90 - 27 * y);
            }
        }

        return heights;
    }
}
