using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Tests.Surface;

[TestClass]
public sealed class PlanetSurfaceNavigatorTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void Move_ZeroDistance_PreservesDirection()
    {
        var direction = PlanetVector.Normalize(new PlanetVector(0.2, 0.4, 0.8));

        var moved = PlanetSurfaceNavigator.Move(direction, 0.0, 0.0, EarthRadiusMeters);

        Assert.AreEqual(direction.X, moved.X, 0.000000000001);
        Assert.AreEqual(direction.Y, moved.Y, 0.000000000001);
        Assert.AreEqual(direction.Z, moved.Z, 0.000000000001);
    }

    [TestMethod]
    public void Move_OneKilometreEast_TravelsOneKilometreAlongSurface()
    {
        var origin = PlanetVector.UnitZ;

        var moved = PlanetSurfaceNavigator.Move(origin, 1_000.0, 0.0, EarthRadiusMeters);
        var angularDistance = Math.Acos(Math.Clamp(PlanetVector.Dot(origin, moved), -1.0, 1.0));
        var distanceMeters = angularDistance * EarthRadiusMeters;

        Assert.AreEqual(1_000.0, distanceMeters, 0.001);
        Assert.AreEqual(1.0, moved.Length, 0.000000000001);
    }

    [TestMethod]
    public void Move_QuarterCircumferenceNorth_ReachesNorthPole()
    {
        var distanceMeters = Math.PI * EarthRadiusMeters * 0.5;

        var moved = PlanetSurfaceNavigator.Move(PlanetVector.UnitZ, 0.0, distanceMeters, EarthRadiusMeters);

        Assert.AreEqual(0.0, moved.X, 0.000000000001);
        Assert.AreEqual(1.0, moved.Y, 0.000000000001);
        Assert.AreEqual(0.0, moved.Z, 0.000000000001);
    }

    [TestMethod]
    public void Move_NearPole_RemainsFiniteAndNormalized()
    {
        var origin = PlanetVector.Normalize(new PlanetVector(0.000001, 1.0, 0.000001));

        var moved = PlanetSurfaceNavigator.Move(origin, 250.0, 125.0, EarthRadiusMeters);

        Assert.IsTrue(double.IsFinite(moved.X));
        Assert.IsTrue(double.IsFinite(moved.Y));
        Assert.IsTrue(double.IsFinite(moved.Z));
        Assert.AreEqual(1.0, moved.Length, 0.000000000001);
    }
}