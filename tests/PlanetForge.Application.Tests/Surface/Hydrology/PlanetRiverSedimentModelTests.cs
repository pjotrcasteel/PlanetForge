using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface.Hydrology;

[TestClass]
public sealed class PlanetRiverSedimentModelTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void Build_RiverEnteringOcean_ProducesDeltaFan()
    {
        var segment = CreateSegment(18_000.0, 4, 240.0, -180.0);

        var result = new PlanetRiverSedimentModel().Build([segment], 150, EarthRadiusMeters, 0.0, CancellationToken.None);

        Assert.HasCount(3, result);
        Assert.IsTrue(result.All(value => value.CoreDepositionHeightMeters > 0.0));
        Assert.IsTrue(result.All(value => value.ApronAngularRadiusRadians > value.CoreAngularRadiusRadians));
    }

    [TestMethod]
    public void Build_HigherDischarge_ProducesLargerDelta()
    {
        var model = new PlanetRiverSedimentModel();

        var low = model.Build([CreateSegment(3_000.0, 2, 180.0, -120.0)], 150, EarthRadiusMeters, 0.0, CancellationToken.None)[0];
        var high = model.Build([CreateSegment(30_000.0, 4, 180.0, -120.0)], 150, EarthRadiusMeters, 0.0, CancellationToken.None)[0];

        Assert.IsGreaterThan(low.CoreDepositionHeightMeters, high.CoreDepositionHeightMeters);
        Assert.IsGreaterThan(low.ApronAngularRadiusRadians, high.ApronAngularRadiusRadians);
    }

    [TestMethod]
    public void Build_HighDryUplandReach_ProducesNoDeposition()
    {
        var segment = CreateSegment(12_000.0, 3, 4_000.0, 2_500.0);

        var result = new PlanetRiverSedimentModel().Build([segment], 150, EarthRadiusMeters, 0.0, CancellationToken.None);

        Assert.HasCount(0, result);
    }

    [TestMethod]
    public void Build_SameInputs_IsDeterministic()
    {
        var model = new PlanetRiverSedimentModel();
        var segments = new[] { CreateSegment(12_000.0, 3, 300.0, -100.0), CreateSegment(7_500.0, 2, 500.0, 120.0) };

        var first = model.Build(segments, 150, EarthRadiusMeters, 0.0, CancellationToken.None);
        var second = model.Build(segments, 150, EarthRadiusMeters, 0.0, CancellationToken.None);

        CollectionAssert.AreEqual(first.ToArray(), second.ToArray());
    }

    private static PlanetRiverSegment CreateSegment(
        double dischargeCubicMetersPerSecond,
        int streamOrder,
        double fromElevationMeters,
        double toElevationMeters)
        => new(
            new PlanetSurfaceGridCellId(CubeFace.PositiveX, 4, 5, 6),
            new PlanetSurfaceGridCellId(CubeFace.PositiveX, 4, 6, 6),
            PlanetVector.Normalize(new PlanetVector(1.0, 0.10, 0.12)),
            PlanetVector.Normalize(new PlanetVector(1.0, 0.12, 0.20)),
            fromElevationMeters,
            toElevationMeters,
            16,
            500_000_000_000.0,
            dischargeCubicMetersPerSecond,
            streamOrder);
}
