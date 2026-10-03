using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Tests.Surface;

[TestClass]
public sealed class PlanetSurfaceGridLayoutTests
{
    [TestMethod]
    public void CellIndexRoundTrip_AllCellsAtLevelThree_IsStable()
    {
        var layout = new PlanetSurfaceGridLayout(3);

        for (var index = 0; index < layout.CellCount; index++)
        {
            var cell = layout.GetCell(index);
            Assert.AreEqual(index, layout.GetIndex(cell));
        }
    }

    [TestMethod]
    public void GetIndex_CellFromDifferentLevel_Throws()
    {
        var layout = new PlanetSurfaceGridLayout(3);
        var cell = new PlanetSurfaceGridCellId(CubeFace.PositiveZ, 2, 0, 0);

        Assert.ThrowsExactly<ArgumentException>(() => layout.GetIndex(cell));
    }
}
