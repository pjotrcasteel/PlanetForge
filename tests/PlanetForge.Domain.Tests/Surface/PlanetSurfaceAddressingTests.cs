using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Tests.Surface;

[TestClass]
public sealed class PlanetSurfaceAddressingTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void Encode_SameDirection_ReturnsSameAddress()
    {
        var direction = PlanetVector.Normalize(new PlanetVector(0.31, -0.42, 0.85));

        var first = PlanetSurfaceAddressing.Encode(direction);
        var second = PlanetSurfaceAddressing.Encode(direction);

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.ToString(), second.ToString());
    }

    [TestMethod]
    public void EncodeDecode_DefaultPrecision_PreservesSubMetreSurfaceLocation()
    {
        var direction = PlanetVector.Normalize(new PlanetVector(-0.342, 0.715, 0.609));

        var address = PlanetSurfaceAddressing.Encode(direction);
        var decoded = PlanetSurfaceAddressing.Decode(address);
        var angularError = Math.Acos(Math.Clamp(PlanetVector.Dot(direction, decoded), -1.0, 1.0));
        var surfaceErrorMeters = angularError * EarthRadiusMeters;

        Assert.IsLessThan(0.1, surfaceErrorMeters);
    }

    [TestMethod]
    public void Encode_LocationsTenMetresApart_ProduceDifferentDefaultAddresses()
    {
        var origin = PlanetVector.UnitZ;
        var moved = PlanetSurfaceNavigator.Move(origin, 10.0, 0.0, EarthRadiusMeters);

        var originAddress = PlanetSurfaceAddressing.Encode(origin);
        var movedAddress = PlanetSurfaceAddressing.Encode(moved);

        Assert.AreNotEqual(originAddress, movedAddress);
    }

    [TestMethod]
    public void Decode_AddressOutsideDeclaredPrecision_Throws()
    {
        var address = new PlanetSurfaceAddress(8, 256, 0);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetSurfaceAddressing.Decode(address));
    }
}