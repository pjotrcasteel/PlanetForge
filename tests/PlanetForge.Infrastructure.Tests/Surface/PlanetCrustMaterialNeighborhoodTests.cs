using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetCrustMaterialNeighborhoodTests
{
    [TestMethod]
    [DataRow(24061984)]
    [DataRow(346147916)]
    [DataRow(579460630)]
    public void Build_SeededCrust_CreatesStableSymmetricReferenceEdges(int seed)
    {
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var graph = PlanetCrustMaterialNeighborhood.Build(catalog);
        var replay = PlanetCrustMaterialNeighborhood.Build(PlanetCrustParcelCatalog.Create(seed));

        Assert.AreEqual(catalog.World, graph.World);
        Assert.AreEqual(6, graph.NearestNeighborCount);
        CollectionAssert.AreEqual(graph.Edges.ToArray(), replay.Edges.ToArray());
        Assert.IsGreaterThan(catalog.Parcels.Count * 2, graph.Edges.Count);

        var unique = new HashSet<(ulong First, ulong Second)>();
        ulong previousFirst = 0;
        ulong previousSecond = 0;
        foreach (var edge in graph.Edges)
        {
            Assert.IsTrue(edge.First.Value < edge.Second.Value);
            Assert.IsTrue(unique.Add((edge.First.Value, edge.Second.Value)), "No material edge may be counted twice.");
            Assert.IsTrue(edge.First.Value > previousFirst ||
                edge.First.Value == previousFirst && edge.Second.Value > previousSecond);
            previousFirst = edge.First.Value;
            previousSecond = edge.Second.Value;
            Assert.IsTrue(double.IsFinite(edge.ReferenceAngularDistanceRadians));
            Assert.IsGreaterThan(0.0, edge.ReferenceAngularDistanceRadians);
            Assert.IsLessThan(0.3, edge.ReferenceAngularDistanceRadians);

            var first = catalog.Parcels[(int)edge.First.Value - 1].PresentDirection;
            var second = catalog.Parcels[(int)edge.Second.Value - 1].PresentDirection;
            Assert.AreEqual(Math.Acos(Math.Clamp(PlanetVector.Dot(first, second), -1.0, 1.0)),
                edge.ReferenceAngularDistanceRadians, 1e-12);
            Assert.IsTrue(graph.GetNeighbors(edge.First).Contains(edge.Second));
            Assert.IsTrue(graph.GetNeighbors(edge.Second).Contains(edge.First));
        }

        foreach (var material in catalog.Parcels)
        {
            var neighbors = graph.GetNeighbors(material.Id);
            Assert.IsGreaterThanOrEqualTo(6, neighbors.Count);
            Assert.AreEqual(neighbors.Count, neighbors.Distinct().Count());
            Assert.IsFalse(neighbors.Contains(material.Id));
        }
    }

    [TestMethod]
    public void Build_IncreasingNeighborBudget_DoesNotRemoveExistingMaterialEdges()
    {
        var catalog = PlanetCrustParcelCatalog.Create(42);
        var small = PlanetCrustMaterialNeighborhood.Build(catalog, 3);
        var large = PlanetCrustMaterialNeighborhood.Build(catalog, 8);
        var largeKeys = large.Edges.Select(edge => (edge.First, edge.Second)).ToHashSet();
        Assert.IsTrue(small.Edges.All(edge => largeKeys.Contains((edge.First, edge.Second))));
        Assert.IsGreaterThan(small.Edges.Count, large.Edges.Count);
    }

    [TestMethod]
    public void Build_InvalidNeighborhoodBudgetAndUnknownMaterial_RejectInvalidInput()
    {
        var catalog = PlanetCrustParcelCatalog.Create(42);
        Assert.ThrowsExactly<ArgumentNullException>(() => PlanetCrustMaterialNeighborhood.Build(null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetCrustMaterialNeighborhood.Build(catalog, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetCrustMaterialNeighborhood.Build(catalog, 17));
        var graph = PlanetCrustMaterialNeighborhood.Build(catalog);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => graph.GetNeighbors(default));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => graph.GetNeighbors(new PlanetCrustParcelId(1025)));
    }
}
