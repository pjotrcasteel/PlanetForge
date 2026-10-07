using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface.Hydrology;

[TestClass]
public sealed class PlanetRiverGeomorphologyModelTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void Build_ActiveRiver_ProducesContinuousChannelAndValleyStamps()
    {
        var segment = CreateSegment(12_000.0, 3);

        var result = new PlanetRiverGeomorphologyModel().Build([segment], 100, EarthRadiusMeters, CancellationToken.None);

        Assert.IsGreaterThan(2, result.Count);
        Assert.IsTrue(result.All(value => value.ChannelAngularRadiusRadians > 0.0));
        Assert.IsTrue(result.All(value => value.ValleyAngularRadiusRadians > value.ChannelAngularRadiusRadians));
        Assert.IsTrue(result.All(value => value.ChannelIncisionDepthMeters > 0.0));
        Assert.IsTrue(result.All(value => value.ValleyIncisionDepthMeters > 0.0));
    }

    [TestMethod]
    public void Build_HigherDischarge_CarvesDeeperAndWiderValley()
    {
        var model = new PlanetRiverGeomorphologyModel();

        var low = model.Build([CreateSegment(2_500.0, 1)], 100, EarthRadiusMeters, CancellationToken.None)[0];
        var high = model.Build([CreateSegment(25_000.0, 4)], 100, EarthRadiusMeters, CancellationToken.None)[0];

        Assert.IsGreaterThan(low.ChannelIncisionDepthMeters, high.ChannelIncisionDepthMeters);
        Assert.IsGreaterThan(low.ValleyAngularRadiusRadians, high.ValleyAngularRadiusRadians);
    }

    [TestMethod]
    public void Build_MoreElapsedTime_DeepensIncision()
    {
        var model = new PlanetRiverGeomorphologyModel();
        var segment = CreateSegment(12_000.0, 3);

        var early = model.Build([segment], 10, EarthRadiusMeters, CancellationToken.None)[0];
        var mature = model.Build([segment], 100, EarthRadiusMeters, CancellationToken.None)[0];

        Assert.IsGreaterThan(early.ChannelIncisionDepthMeters, mature.ChannelIncisionDepthMeters);
    }

    [TestMethod]
    public void Build_SameInputs_IsDeterministic()
    {
        var model = new PlanetRiverGeomorphologyModel();
        var segments = new[] { CreateSegment(4_000.0, 1), CreateSegment(18_000.0, 3) };

        var first = model.Build(segments, 100, EarthRadiusMeters, CancellationToken.None);
        var second = model.Build(segments, 100, EarthRadiusMeters, CancellationToken.None);

        CollectionAssert.AreEqual(first.ToArray(), second.ToArray());
    }

    [TestMethod]
    public void Build_PreCancelledToken_Throws()
    {
        var model = new PlanetRiverGeomorphologyModel();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            model.Build([CreateSegment(12_000.0, 3)], 100, EarthRadiusMeters, cancellationTokenSource.Token));
    }

    private static PlanetRiverSegment CreateSegment(double dischargeCubicMetersPerSecond, int streamOrder)
        => new(
            new PlanetSurfaceGridCellId(CubeFace.PositiveX, 3, 2, 2),
            new PlanetSurfaceGridCellId(CubeFace.PositiveX, 3, 3, 2),
            PlanetVector.Normalize(new PlanetVector(1.0, 0.10, 0.20)),
            PlanetVector.Normalize(new PlanetVector(1.0, 0.16, 0.28)),
            1_800.0,
            900.0,
            8,
            250_000_000_000.0,
            dischargeCubicMetersPerSecond,
            streamOrder);
}
