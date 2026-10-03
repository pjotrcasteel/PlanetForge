using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface.Hydrology;

[TestClass]
public sealed class PlanetHydrologyModelBuilderTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Build_ClosedInlandBasin_FillsToSpillElevation()
    {
        var builder = new PlanetHydrologyModelBuilder(new BasinElevationSource());

        var result = builder.Build(4, 42, EarthRadiusMeters, -500.0, TestContext.CancellationToken);

        Assert.IsTrue(result.Cells.Any(cell => cell.DepressionFillDepthMeters >= 900.0));
    }

    [TestMethod]
    public void Build_WithOcean_AllLandDrainagePathsReachOceanWithoutLoops()
    {
        var builder = new PlanetHydrologyModelBuilder(new BasinElevationSource());
        var result = builder.Build(4, 42, EarthRadiusMeters, -500.0, TestContext.CancellationToken);

        foreach (var cell in result.Cells.Where(cell => !cell.IsOcean))
        {
            AssertPathReachesOcean(result, cell);
        }
    }

    [TestMethod]
    public void Build_WithOcean_OutletAccumulationEqualsLandCellCount()
    {
        var builder = new PlanetHydrologyModelBuilder(new BasinElevationSource());
        var result = builder.Build(4, 42, EarthRadiusMeters, -500.0, TestContext.CancellationToken);
        var landCellCount = result.Cells.LongCount(cell => !cell.IsOcean);
        var oceanAccumulation = result.Cells.Where(cell => cell.IsOcean).Sum(cell => cell.ContributingLandCellCount);

        Assert.AreEqual(landCellCount, oceanAccumulation);
    }

    [TestMethod]
    public void Build_DryWorld_UsesSingleNaturalSink()
    {
        var builder = new PlanetHydrologyModelBuilder(new DrySlopeElevationSource());
        var result = builder.Build(3, 42, EarthRadiusMeters, -20_000.0, TestContext.CancellationToken);
        var sinks = result.Cells.Where(cell => cell.DrainageTarget is null).ToArray();

        Assert.AreEqual(1, sinks.Length);
        Assert.AreEqual(result.Layout.CellCount, sinks[0].ContributingLandCellCount);
    }

    [TestMethod]
    public void Build_PreCancelledToken_Throws()
    {
        var builder = new PlanetHydrologyModelBuilder(new BasinElevationSource());
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => builder.Build(4, 42, EarthRadiusMeters, -500.0, cancellationTokenSource.Token));
    }

    private static void AssertPathReachesOcean(PlanetHydrologySnapshot snapshot, PlanetHydrologyCell start)
    {
        var visited = new HashSet<PlanetSurfaceGridCellId>();
        var current = start;

        for (var step = 0; step < snapshot.Layout.CellCount; step++)
        {
            Assert.IsTrue(visited.Add(current.Cell), $"Drainage loop detected at {current.Cell}.");
            if (current.IsOcean)
            {
                return;
            }

            Assert.IsNotNull(current.DrainageTarget, $"Land cell {current.Cell} terminated before reaching ocean.");
            current = snapshot.GetCell(current.DrainageTarget.Value);
        }

        Assert.Fail("Drainage path exceeded the total cell count.");
    }

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

    private sealed class DrySlopeElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 2_000.0 + (direction.Z * 1_000.0) + (direction.X * 100.0);
    }
}
