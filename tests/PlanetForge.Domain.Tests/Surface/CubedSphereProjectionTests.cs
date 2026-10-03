using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Tests.Surface;

[TestClass]
public sealed class CubedSphereProjectionTests
{
    [TestMethod]
    [DataRow(CubeFace.PositiveX)]
    [DataRow(CubeFace.NegativeX)]
    [DataRow(CubeFace.PositiveY)]
    [DataRow(CubeFace.NegativeY)]
    [DataRow(CubeFace.PositiveZ)]
    [DataRow(CubeFace.NegativeZ)]
    public void ToUnitSphere_ReturnsUnitLengthDirection(CubeFace face)
    {
        var direction = CubedSphereProjection.ToUnitSphere(face, 0.37, -0.61);

        Assert.AreEqual(1.0, direction.Length, 0.0000000001);
    }

    [TestMethod]
    public void ToUnitSphere_SharedCubeFaceEdge_ReturnsSameDirection()
    {
        var positiveXEdge = CubedSphereProjection.ToUnitSphere(CubeFace.PositiveX, -1.0, 0.35);
        var positiveZEdge = CubedSphereProjection.ToUnitSphere(CubeFace.PositiveZ, 1.0, 0.35);

        AssertVectorEqual(positiveXEdge, positiveZEdge);
    }

    [TestMethod]
    public void ToUnitSphere_InvalidCoordinate_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CubedSphereProjection.ToUnitSphere(CubeFace.PositiveX, 1.01, 0.0));
    }

    private static void AssertVectorEqual(PlanetVector expected, PlanetVector actual)
    {
        Assert.AreEqual(expected.X, actual.X, 0.0000000001);
        Assert.AreEqual(expected.Y, actual.Y, 0.0000000001);
        Assert.AreEqual(expected.Z, actual.Z, 0.0000000001);
    }
}
