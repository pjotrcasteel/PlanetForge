using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetCrustSphericalControlVolumesTests
{
    [TestMethod]
    [DataRow(24061984)]
    [DataRow(346147916)]
    [DataRow(579460630)]
    public void Build_CanonicalWorld_ClosesEntireSphereWithReciprocalSharedFaces(int seed)
    {
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var geometry = PlanetCrustSphericalControlVolumes.Build(catalog);
        var expected = PlanetCrustSphericalControlVolumes.PlanetSurfaceAreaSquareMeters;
        Assert.AreEqual(catalog.World, geometry.World);
        Assert.AreEqual(catalog.Parcels.Count, geometry.Cells.Count);
        Assert.AreEqual(expected, geometry.GeographicAreaSquareMeters, expected * 1e-8);
        Assert.IsGreaterThan(2000, geometry.Faces.Count);
        Assert.IsLessThan(4000, geometry.Faces.Count);

        var faces = new HashSet<(ulong First, ulong Second)>();
        foreach (var face in geometry.Faces)
        {
            Assert.IsTrue(face.First.Value < face.Second.Value);
            Assert.IsTrue(faces.Add((face.First.Value, face.Second.Value)));
            Assert.IsGreaterThan(0.0, face.LengthMeters);
            Assert.IsTrue(double.IsFinite(face.LengthMeters));
            Assert.IsLessThan(2_000_000.0, face.LengthMeters);
            var first = geometry.GetCell(face.First);
            var second = geometry.GetCell(face.Second);
            Assert.IsTrue(HasEdge(first, face.Start, face.End), "A canonical face must occur on the first cell.");
            Assert.IsTrue(HasEdge(second, face.Start, face.End), "The second cell must share the identical face.");
            var midpoint = PlanetVector.Normalize(face.Start + face.End);
            var firstDot = PlanetVector.Dot(catalog.Parcels[(int)face.First.Value - 1].PresentDirection, midpoint);
            var secondDot = PlanetVector.Dot(catalog.Parcels[(int)face.Second.Value - 1].PresentDirection, midpoint);
            Assert.AreEqual(firstDot, secondDot, 1e-8);
        }

        foreach (var cell in geometry.Cells)
        {
            Assert.IsGreaterThan(0.0, cell.GeographicAreaSquareMeters);
            Assert.IsGreaterThanOrEqualTo(3, cell.BoundaryVertices.Count);
            Assert.AreEqual(cell.MaterialId, geometry.Locate(
                catalog.Parcels[(int)cell.MaterialId.Value - 1].PresentDirection).MaterialId);
        }
    }

    [TestMethod]
    public void Build_RecreatedSameWorld_ReplaysCellAreasAndGeographicFaces()
    {
        var first = PlanetCrustSphericalControlVolumes.Build(PlanetCrustParcelCatalog.Create(24061984));
        var restored = PlanetCrustSphericalControlVolumes.Build(PlanetCrustParcelCatalog.Create(24061984));
        Assert.AreEqual(first.World, restored.World);
        Assert.AreEqual(first.GeographicAreaSquareMeters, restored.GeographicAreaSquareMeters);
        for (var i = 0; i < first.Cells.Count; i++)
        {
            Assert.AreEqual(first.Cells[i].MaterialId, restored.Cells[i].MaterialId);
            Assert.AreEqual(first.Cells[i].GeographicAreaSquareMeters, restored.Cells[i].GeographicAreaSquareMeters);
            CollectionAssert.AreEqual(first.Cells[i].BoundaryVertices.ToArray(),
                restored.Cells[i].BoundaryVertices.ToArray());
        }

        CollectionAssert.AreEqual(first.Faces.ToArray(), restored.Faces.ToArray());
    }

    [TestMethod]
    public void Locate_GlobalDirections_AgreesWithBruteForceNearestMaterialAcrossPolesAndLongitude()
    {
        var catalog = PlanetCrustParcelCatalog.Create(24061984);
        var geometry = PlanetCrustSphericalControlVolumes.Build(catalog);
        var directions = new List<PlanetVector> {
            PlanetVector.UnitX, PlanetVector.UnitY, PlanetVector.UnitZ,
            PlanetVector.UnitX * -1, PlanetVector.UnitY * -1, PlanetVector.UnitZ * -1,
            PlanetVector.Normalize(new PlanetVector(-1, 0.000000000001, 0.000000000001))
        };
        for (var i = 0; i < 400; i++)
        {
            var y = 1.0 - 2.0 * ((i + 0.5) / 400.0);
            var r = Math.Sqrt(1.0 - y * y);
            var angle = i * Math.PI * (3.0 - Math.Sqrt(5.0));
            directions.Add(new PlanetVector(r * Math.Cos(angle), y, r * Math.Sin(angle)));
        }

        foreach (var direction in directions)
        {
            var expected = catalog.Parcels
                .OrderByDescending(material => PlanetVector.Dot(material.PresentDirection, direction))
                .ThenBy(material => material.Id.Value).First().Id;
            Assert.AreEqual(expected, geometry.Locate(direction).MaterialId);
            Assert.AreEqual(expected, geometry.Locate(direction * 4).MaterialId);
        }
    }

    [TestMethod]
    public void Cells_SharedBoundaries_UseIdenticalCanonicalFaceAcrossRegionAndLodViews()
    {
        var catalog = PlanetCrustParcelCatalog.Create(42);
        var geometry = PlanetCrustSphericalControlVolumes.Build(catalog);
        foreach (var face in geometry.Faces.Where((_, index) => index % 41 == 0))
        {
            var midpoint = PlanetVector.Normalize(face.Start + face.End);
            var first = geometry.GetCell(face.First);
            var second = geometry.GetCell(face.Second);
            Assert.IsTrue(HasEdge(first, face.Start, face.End));
            Assert.IsTrue(HasEdge(second, face.Start, face.End));
            var indexed = geometry.Locate(midpoint).MaterialId;
            Assert.IsTrue(indexed == face.First || indexed == face.Second,
                "An exact shared boundary belongs to either of the two adjacent cells.");
        }
    }

    [TestMethod]
    public void Build_SphericalAreas_AreGeographicNotSilentlySubstitutedForOldMaterialQuadrature()
    {
        var catalog = PlanetCrustParcelCatalog.Create(42);
        var geometry = PlanetCrustSphericalControlVolumes.Build(catalog);
        var first = catalog.Parcels[0];
        var area = geometry.GetCell(first.Id).GeographicAreaSquareMeters;
        Assert.AreNotEqual(first.ReferenceAreaSquareMeters, area);
        Assert.AreEqual(4.0 * Math.PI * Math.Pow(PlanetTectonicPlateKinematics.NominalPlanetRadiusMeters, 2),
            geometry.GeographicAreaSquareMeters, geometry.GeographicAreaSquareMeters * 1e-8);
    }

    [TestMethod]
    public void Locate_InvalidPointAndMaterial_RejectsUnsafeLookup()
    {
        var geometry = PlanetCrustSphericalControlVolumes.Build(PlanetCrustParcelCatalog.Create(42));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => geometry.Locate(new PlanetVector(0, 0, 0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => geometry.GetCell(default));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => geometry.GetCell(new PlanetCrustParcelId(1025)));
        Assert.ThrowsExactly<ArgumentNullException>(() => PlanetCrustSphericalControlVolumes.Build(null!));
    }

    private static bool HasEdge(PlanetCrustSphericalCell cell, PlanetVector start, PlanetVector end)
    {
        for (var i = 0; i < cell.BoundaryVertices.Count; i++)
        {
            var a = cell.BoundaryVertices[i];
            var b = cell.BoundaryVertices[(i + 1) % cell.BoundaryVertices.Count];
            if ((a - start).Length < 1e-7 && (b - end).Length < 1e-7 ||
                (a - end).Length < 1e-7 && (b - start).Length < 1e-7)
            {
                return true;
            }
        }

        return false;
    }
}
