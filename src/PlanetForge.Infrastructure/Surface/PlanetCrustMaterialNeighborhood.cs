using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Undirected neighboring material samples in the canonical present-day
/// reference configuration. Not a watertight crust polygon tessellation.
/// </summary>
public readonly record struct PlanetCrustNeighborEdge(
    PlanetCrustParcelId First,
    PlanetCrustParcelId Second,
    double ReferenceAngularDistanceRadians);

/// <summary>
/// Stable, symmetric and bounded contact candidates derived from actual
/// seeded material directions. Edges follow material IDs across epochs;
/// separation can deactivate a contact but does not invent a new neighbor.
/// Reconnection / changing topology requires a subsequent explicit solver.
/// </summary>
public sealed class PlanetCrustMaterialNeighborhood
{
    public const int DefaultNearestNeighbors = 6;

    private readonly IReadOnlyList<PlanetCrustNeighborEdge> edges;
    private readonly IReadOnlyList<PlanetCrustParcelId>[] neighbors;

    private PlanetCrustMaterialNeighborhood(
        PlanetWorldIdentity world,
        int nearestNeighborCount,
        PlanetCrustNeighborEdge[] edges,
        IReadOnlyList<PlanetCrustParcelId>[] neighbors)
    {
        World = world;
        NearestNeighborCount = nearestNeighborCount;
        this.edges = Array.AsReadOnly(edges);
        this.neighbors = neighbors;
    }

    public PlanetWorldIdentity World { get; }
    public int NearestNeighborCount { get; }
    public IReadOnlyList<PlanetCrustNeighborEdge> Edges => edges;

    public IReadOnlyList<PlanetCrustParcelId> GetNeighbors(PlanetCrustParcelId parcelId)
    {
        if (parcelId.Value == 0 || parcelId.Value > (ulong)neighbors.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(parcelId), parcelId, "Unknown reference crust material.");
        }

        return neighbors[(int)parcelId.Value - 1];
    }

    public static PlanetCrustMaterialNeighborhood Build(
        PlanetCrustParcelCatalog catalog,
        int nearestNeighborCount = DefaultNearestNeighbors)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentOutOfRangeException.ThrowIfLessThan(nearestNeighborCount, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nearestNeighborCount, 16);

        var materials = catalog.Parcels;
        if (nearestNeighborCount >= materials.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(nearestNeighborCount));
        }

        var unique = new SortedDictionary<ulong, PlanetCrustNeighborEdge>();
        for (var i = 0; i < materials.Count; i++)
        {
            var closest = new List<(int Index, double Dot)>(nearestNeighborCount);
            for (var j = 0; j < materials.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                var dot = PlanetVector.Dot(materials[i].PresentDirection, materials[j].PresentDirection);
                var insert = 0;
                while (insert < closest.Count && (closest[insert].Dot > dot ||
                    (closest[insert].Dot == dot && closest[insert].Index < j)))
                {
                    insert++;
                }

                if (insert >= nearestNeighborCount)
                {
                    continue;
                }

                closest.Insert(insert, (j, dot));
                if (closest.Count > nearestNeighborCount)
                {
                    closest.RemoveAt(nearestNeighborCount);
                }
            }

            foreach (var neighbor in closest)
            {
                var first = Math.Min(materials[i].Id.Value, materials[neighbor.Index].Id.Value);
                var second = Math.Max(materials[i].Id.Value, materials[neighbor.Index].Id.Value);
                var key = (first << 32) | second;
                var separation = Math.Acos(Math.Clamp(neighbor.Dot, -1.0, 1.0));
                unique.TryAdd(key, new PlanetCrustNeighborEdge(new PlanetCrustParcelId(first),
                    new PlanetCrustParcelId(second), separation));
            }
        }

        var entries = unique.Values.ToArray();
        var list = Enumerable.Range(0, materials.Count).Select(_ => new List<PlanetCrustParcelId>()).ToArray();
        foreach (var edge in entries)
        {
            list[(int)edge.First.Value - 1].Add(edge.Second);
            list[(int)edge.Second.Value - 1].Add(edge.First);
        }

        var adjacency = list.Select(x => (IReadOnlyList<PlanetCrustParcelId>)Array.AsReadOnly(
            x.OrderBy(id => id.Value).ToArray())).ToArray();
        return new PlanetCrustMaterialNeighborhood(catalog.World, nearestNeighborCount, entries, adjacency);
    }
}
