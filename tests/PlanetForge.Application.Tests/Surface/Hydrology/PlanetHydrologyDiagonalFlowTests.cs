using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface.Hydrology;

[TestClass]
public sealed class PlanetHydrologyDiagonalFlowTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void Build_DiagonalSlope_UsesDiagonalDrainageSteps()
    {
        var builder = new PlanetHydrologyModelBuilder(new DiagonalSlopeElevationSource());

        var result = builder.Build(4, 42, EarthRadiusMeters, -20_000.0, CancellationToken.None);

        Assert.IsTrue(result.Cells.Any(cell => IsDiagonalStep(cell, result)), "Expected at least one same-face diagonal drainage step.");
    }

    private static bool IsDiagonalStep(PlanetHydrologyCell cell, PlanetHydrologySnapshot snapshot)
    {
        if (cell.DrainageTarget is null)
        {
            return false;
        }

        var target = cell.DrainageTarget.Value;
        return target.Face == cell.Cell.Face && Math.Abs(target.X - cell.Cell.X) == 1 && Math.Abs(target.Y - cell.Cell.Y) == 1;
    }

    private sealed class DiagonalSlopeElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 3_000.0 + ((direction.X + direction.Y) * 1_000.0);
    }
}
