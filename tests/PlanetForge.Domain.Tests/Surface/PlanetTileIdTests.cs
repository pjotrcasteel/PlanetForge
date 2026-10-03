using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Tests.Surface;

[TestClass]
public sealed class PlanetTileIdTests
{
    [TestMethod]
    public void Children_ReturnsFourStableQuadtreeChildren()
    {
        var tile = new PlanetTileId(CubeFace.PositiveZ, 2, 1, 2);

        var children = tile.Children();

        Assert.AreEqual(4, children.Count);
        CollectionAssert.AreEqual(
            new[]
            {
                new PlanetTileId(CubeFace.PositiveZ, 3, 2, 4),
                new PlanetTileId(CubeFace.PositiveZ, 3, 3, 4),
                new PlanetTileId(CubeFace.PositiveZ, 3, 2, 5),
                new PlanetTileId(CubeFace.PositiveZ, 3, 3, 5),
            },
            children.ToArray());
    }

    [TestMethod]
    public void Constructor_CoordinateOutsideLevelGrid_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetTileId(CubeFace.PositiveZ, 2, 4, 0));
    }
}
