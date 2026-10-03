using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Tests.Surface;

[TestClass]
public sealed class PlanetSurfaceGridTopologyTests
{
    [TestMethod]
    public void GetNeighbor_AllCellsAndDirections_AreReversible()
    {
        const int level = 3;
        var cellsPerAxis = 1 << level;

        foreach (var face in Enum.GetValues<CubeFace>())
        {
            for (var y = 0; y < cellsPerAxis; y++)
            {
                for (var x = 0; x < cellsPerAxis; x++)
                {
                    var cell = new PlanetSurfaceGridCellId(face, level, x, y);
                    foreach (var direction in Enum.GetValues<PlanetGridDirection>())
                    {
                        var neighbor = PlanetSurfaceGridTopology.GetNeighbor(cell, direction);
                        var returnNeighbor = PlanetSurfaceGridTopology.GetNeighbor(neighbor.Cell, neighbor.ReturnDirection);
                        Assert.AreEqual(cell, returnNeighbor.Cell);
                    }
                }
            }
        }
    }

    [TestMethod]
    public void GetNeighbor_CrossFaceTransition_PreservesLevel()
    {
        var cell = new PlanetSurfaceGridCellId(CubeFace.PositiveZ, 5, 31, 11);

        var neighbor = PlanetSurfaceGridTopology.GetNeighbor(cell, PlanetGridDirection.East);

        Assert.AreEqual(CubeFace.PositiveX, neighbor.Cell.Face);
        Assert.AreEqual(cell.Level, neighbor.Cell.Level);
        Assert.AreEqual(0, neighbor.Cell.X);
        Assert.AreEqual(cell.Y, neighbor.Cell.Y);
    }

    [TestMethod]
    public void GetCenterDirection_AllFaces_ReturnUnitDirections()
    {
        foreach (var face in Enum.GetValues<CubeFace>())
        {
            var direction = PlanetSurfaceGridGeometry.GetCenterDirection(new PlanetSurfaceGridCellId(face, 4, 7, 9));
            Assert.AreEqual(1.0, direction.Length, 0.0000000001);
        }
    }
}
