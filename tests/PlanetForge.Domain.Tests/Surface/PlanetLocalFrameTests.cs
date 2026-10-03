using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Tests.Surface;

[TestClass]
public sealed class PlanetLocalFrameTests
{
    [TestMethod]
    public void Create_Equator_ProducesOrthonormalFrame()
    {
        var frame = PlanetLocalFrame.Create(PlanetVector.UnitZ, 6_371_000.0, 120.0);

        AssertOrthonormal(frame);
        Assert.AreEqual(6_371_120.0, frame.OriginMeters.Length, 0.000001);
    }

    [TestMethod]
    public void Create_NearNorthPole_ProducesOrthonormalFrame()
    {
        var direction = PlanetVector.Normalize(new PlanetVector(0.000001, 1.0, 0.000001));

        var frame = PlanetLocalFrame.Create(direction, 6_371_000.0, 0.0);

        AssertOrthonormal(frame);
    }

    [TestMethod]
    public void LocalWorldRoundTrip_PreservesMetreScalePosition()
    {
        var frame = PlanetLocalFrame.Create(PlanetVector.Normalize(new PlanetVector(0.3, 0.6, 0.7)), 6_371_000.0, 850.0);
        var local = new PlanetLocalPosition(123.456, -987.654, 42.125);

        var world = frame.ToWorld(local);
        var roundTrip = frame.ToLocal(world);

        Assert.AreEqual(local.EastMeters, roundTrip.EastMeters, 0.00000001);
        Assert.AreEqual(local.NorthMeters, roundTrip.NorthMeters, 0.00000001);
        Assert.AreEqual(local.UpMeters, roundTrip.UpMeters, 0.00000001);
    }

    private static void AssertOrthonormal(PlanetLocalFrame frame)
    {
        Assert.AreEqual(1.0, frame.East.Length, 0.0000000001);
        Assert.AreEqual(1.0, frame.North.Length, 0.0000000001);
        Assert.AreEqual(1.0, frame.Up.Length, 0.0000000001);
        Assert.AreEqual(0.0, PlanetVector.Dot(frame.East, frame.North), 0.0000000001);
        Assert.AreEqual(0.0, PlanetVector.Dot(frame.East, frame.Up), 0.0000000001);
        Assert.AreEqual(0.0, PlanetVector.Dot(frame.North, frame.Up), 0.0000000001);
    }
}
