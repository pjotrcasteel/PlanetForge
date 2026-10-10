using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Immutable, conservative 3D spherical spatial hash. A region is indexed in
/// every direction bucket touched by a circumscribed spherical cap. This may
/// return extra candidates, but never excludes a sample within its actual
/// gnomonic rectangle (including the fade boundary). Bucket candidates retain
/// source order to preserve floating-point compositing and overlap semantics.
/// </summary>
internal sealed class PlanetRegionalGeologySpatialIndex
{
    private const int CellsPerAxis = 16;
    private readonly Dictionary<(int Seed, int Bucket), PlanetRegionalGeologyOverlay[]> buckets;

    public PlanetRegionalGeologySpatialIndex(IReadOnlyList<PlanetRegionalGeologyOverlay> regions)
    {
        var mutable = new Dictionary<(int Seed, int Bucket), List<PlanetRegionalGeologyOverlay>>();
        foreach (var region in regions)
        {
            var center = region.CenterDirection;
            var radius = Math.Min(2.0, region.SupportChordRadius + 1e-10);
            var minX = Cell(center.X - radius);
            var maxX = Cell(center.X + radius);
            var minY = Cell(center.Y - radius);
            var maxY = Cell(center.Y + radius);
            var minZ = Cell(center.Z - radius);
            var maxZ = Cell(center.Z + radius);

            for (var z = minZ; z <= maxZ; z++)
            {
                for (var y = minY; y <= maxY; y++)
                {
                    for (var x = minX; x <= maxX; x++)
                    {
                        var key = (region.Seed, Flatten(x, y, z));
                        if (!mutable.TryGetValue(key, out var matches))
                        {
                            matches = [];
                            mutable.Add(key, matches);
                        }

                        matches.Add(region);
                    }
                }
            }
        }

        buckets = mutable.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    public IReadOnlyList<PlanetRegionalGeologyOverlay> Candidates(PlanetVector direction, int seed)
    {
        var normalized = PlanetVector.Normalize(direction);
        var bucket = Flatten(Cell(normalized.X), Cell(normalized.Y), Cell(normalized.Z));
        return buckets.TryGetValue((seed, bucket), out var overlays) ? overlays : [];
    }

    private static int Cell(double value) => Math.Clamp((int)Math.Floor((value + 1.0) * 0.5 * CellsPerAxis), 0, CellsPerAxis - 1);

    private static int Flatten(int x, int y, int z) => (z * CellsPerAxis + y) * CellsPerAxis + x;
}
