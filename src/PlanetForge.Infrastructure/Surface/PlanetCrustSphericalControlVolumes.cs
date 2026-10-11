using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Immutable spherical nearest-material geographic cell at one epoch.
/// Its exact geographic area is separate from the older equal-weight material
/// quadrature area, which must not be silently replaced in existing mass ledgers.
/// </summary>
public sealed class PlanetCrustSphericalCell(
    PlanetCrustParcelId materialId,
    double geographicAreaSquareMeters,
    PlanetVector[] boundaryVertices)
{
    public PlanetCrustParcelId MaterialId { get; } = materialId;
    public double GeographicAreaSquareMeters { get; } = geographicAreaSquareMeters;
    public IReadOnlyList<PlanetVector> BoundaryVertices { get; } = Array.AsReadOnly(boundaryVertices);
}

/// <summary>
/// Exactly one canonical shared great-circle face between two adjacent cells.
/// Either cell refers to the SAME face, never two independent tile boundaries.
/// </summary>
public readonly record struct PlanetCrustSphericalFace(
    PlanetCrustParcelId First,
    PlanetCrustParcelId Second,
    PlanetVector Start,
    PlanetVector End,
    double LengthMeters);

/// <summary>
/// Complete deterministic spherical Voronoi partitions around crust material
/// sample directions at one requested epoch. The present-day reference build
/// remains unchanged, while BuildAtEpoch observes persistent material through
/// explicit kinematic ownership history. Voronoi cells are GEOGRAPHIC partitions
/// of moving sites, not advected, area-conserving Lagrangian crust polygons.
///
/// Construction clips each cell by exact great-circle bisectors. Geometry is
/// validated for closure of the sphere, reciprocal faces and positive areas.
/// A balanced 3-D kd-tree locates the containing reference cell without
/// linear scanning all material directions for every geographic query.
/// </summary>
public sealed class PlanetCrustSphericalControlVolumes
{
    private const double RadiusMeters = PlanetTectonicPlateKinematics.NominalPlanetRadiusMeters;
    private static readonly double SphereAreaSquareMeters = 4.0 * Math.PI * RadiusMeters * RadiusMeters;

    private readonly PlanetVector[] sites;
    private readonly PlanetCrustSphericalCell[] cells;
    private readonly IReadOnlyList<PlanetCrustSphericalCell> publicCells;
    private readonly IReadOnlyList<PlanetCrustSphericalFace> faces;
    private readonly KdNode[] tree;
    private readonly int rootIndex;

    private PlanetCrustSphericalControlVolumes(
        PlanetWorldIdentity world,
        PlanetGeologicalEpoch epoch,
        PlanetVector[] sites,
        PlanetCrustSphericalCell[] cells,
        PlanetCrustSphericalFace[] faces)
    {
        World = world;
        Epoch = epoch;
        this.sites = sites;
        this.cells = cells;
        publicCells = Array.AsReadOnly(cells);
        this.faces = Array.AsReadOnly(faces);
        GeographicAreaSquareMeters = cells.Sum(cell => cell.GeographicAreaSquareMeters);
        var nodes = new List<KdNode>(sites.Length);
        rootIndex = BuildIndex(Enumerable.Range(0, sites.Length).ToArray(), 0, nodes);
        tree = nodes.ToArray();
    }

    public PlanetWorldIdentity World { get; }
    public PlanetGeologicalEpoch Epoch { get; }
    public IReadOnlyList<PlanetCrustSphericalCell> Cells => publicCells;
    public IReadOnlyList<PlanetCrustSphericalFace> Faces => faces;
    public double GeographicAreaSquareMeters { get; }
    public static double PlanetSurfaceAreaSquareMeters => SphereAreaSquareMeters;

    public PlanetCrustSphericalCell GetCell(PlanetCrustParcelId id)
    {
        if (id.Value == 0 || id.Value > (ulong)cells.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown reference material cell.");
        }

        return cells[(int)id.Value - 1];
    }

    /// <summary>
    /// Geographic position of the actual moving material reference site used
    /// to create this epoch-specific polygon (not its area centroid).
    /// </summary>
    public PlanetVector GetSiteDirection(PlanetCrustParcelId id)
    {
        if (id.Value == 0 || id.Value > (ulong)sites.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        return sites[(int)id.Value - 1];
    }

    public PlanetCrustSphericalCell Locate(PlanetVector geographicDirection)
    {
        var direction = PlanetVector.Normalize(geographicDirection);
        var bestIndex = 0;
        var bestDistance = double.PositiveInfinity;
        SearchIndex(rootIndex, direction, ref bestIndex, ref bestDistance);
        return cells[bestIndex];
    }

    public static PlanetCrustSphericalControlVolumes Build(PlanetCrustParcelCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var sites = catalog.Parcels.Select(material => material.PresentDirection).ToArray();
        return BuildCore(catalog, PlanetGeologicalEpoch.Present, sites);
    }

    /// <summary>
    /// Reconstruct moving geographic control volumes at a geological epoch
    /// from persistent material sample positions, not moving plate centres.
    /// The full globe is re-partitioned atomically and all reciprocal faces
    /// must still close. Coincident sample sites fail rather than losing mass.
    /// </summary>
    public static PlanetCrustSphericalControlVolumes BuildAtEpoch(
        PlanetCrustParcelCatalog catalog,
        PlanetCrustOwnershipHistory history,
        PlanetGeologicalEpoch epoch)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(history);
        if (history.World != catalog.World)
        {
            throw new ArgumentException("Geological history must belong to this generated world.", nameof(history));
        }

        var sites = catalog.Parcels.Select(material =>
            history.GetState(material.Id, epoch).GeographicDirection).ToArray();
        return BuildCore(catalog, epoch, sites);
    }

    private static PlanetCrustSphericalControlVolumes BuildCore(
        PlanetCrustParcelCatalog catalog,
        PlanetGeologicalEpoch epoch,
        PlanetVector[] sites)
    {
        // A one-site-to-one-cell mapping needs distinct locations. The
        // reference material IDs themselves never change on remeshing.
        for (var first = 0; first < sites.Length; first++)
        {
            for (var second = first + 1; second < sites.Length; second++)
            {
                if (PlanetVector.Dot(sites[first], sites[second]) > 1.0 - 1e-13)
                {
                    throw new InvalidOperationException("Two material samples occupy indistinguishable geographic positions.");
                }
            }
        }

        var cells = new PlanetCrustSphericalCell[sites.Length];
        var faces = new SortedDictionary<ulong, FaceCounter>();

        for (var index = 0; index < sites.Length; index++)
        {
            var polygon = ClipCell(index, sites);
            if (polygon.Count < 3)
            {
                throw new InvalidOperationException("A reference crust cell has an empty or invalid spherical polygon.");
            }

            var area = SphericalAreaSteradians(sites[index], polygon) * RadiusMeters * RadiusMeters;
            if (!double.IsFinite(area) || area <= 0.0)
            {
                throw new InvalidOperationException("A crust cell must have finite positive geographic area.");
            }

            cells[index] = new PlanetCrustSphericalCell(catalog.Parcels[index].Id, area, polygon.ToArray());
            RecordSharedFaces(index, sites, polygon, faces);
        }

        var total = cells.Sum(cell => cell.GeographicAreaSquareMeters);
        if (Math.Abs(total - SphereAreaSquareMeters) > SphereAreaSquareMeters * 1e-8)
        {
            throw new InvalidOperationException("Spherical reference cells do not cover the whole globe.");
        }

        if (faces.Values.Any(face => face.Occurrences != 2))
        {
            throw new InvalidOperationException("Every reference cell face must be shared by precisely two adjacent cells.");
        }

        return new PlanetCrustSphericalControlVolumes(catalog.World, epoch, sites,
            cells, faces.Values.Select(face => face.Face).ToArray());
    }

    private static List<PlanetVector> ClipCell(int index, PlanetVector[] sites)
    {
        var center = sites[index];
        var axis = Math.Abs(center.Y) < 0.9 ? PlanetVector.UnitY : PlanetVector.UnitX;
        var east = PlanetVector.Normalize(PlanetVector.Cross(axis, center));
        var north = PlanetVector.Normalize(PlanetVector.Cross(center, east));

        // A broad enclosing spherical quadrilateral, not a flat tangent mesh.
        // The global coverage check detects any improperly clipped cells.
        PlanetVector Corner(double x, double y) => PlanetVector.Normalize(center + east * (4.0 * x) + north * (4.0 * y));
        var polygon = new List<PlanetVector> { Corner(1, 1), Corner(-1, 1), Corner(-1, -1), Corner(1, -1) };
        var candidates = Enumerable.Range(0, sites.Length)
            .Where(candidate => candidate != index)
            .Select(candidate => (Index: candidate, Dot: PlanetVector.Dot(center, sites[candidate])))
            .OrderByDescending(item => item.Dot)
            .ThenBy(item => item.Index);

        foreach (var candidate in candidates)
        {
            // All remaining sites are further away. When every current cell
            // vertex lies within half their angular separation, no remaining
            // halfspace can cut this convex spherical cell.
            var farthestVertex = polygon.Min(vertex => PlanetVector.Dot(center, vertex));
            var maximumRadius = Math.Acos(Math.Clamp(farthestVertex, -1.0, 1.0));
            var competitorDistance = Math.Acos(Math.Clamp(candidate.Dot, -1.0, 1.0));
            if (competitorDistance > (2.0 * maximumRadius) + 1e-10)
            {
                break;
            }

            polygon = ClipAgainstHalfspace(polygon, center - sites[candidate.Index]);
            if (polygon.Count < 3)
            {
                throw new InvalidOperationException("Spherical halfspace clipping removed a material seed.");
            }
        }

        return polygon;
    }

    private static List<PlanetVector> ClipAgainstHalfspace(List<PlanetVector> polygon, PlanetVector halfspace)
    {
        var result = new List<PlanetVector>(polygon.Count + 2);
        var previous = polygon[^1];
        var previousValue = PlanetVector.Dot(previous, halfspace);
        foreach (var current in polygon)
        {
            var value = PlanetVector.Dot(current, halfspace);
            if ((previousValue >= 0.0) != (value >= 0.0))
            {
                result.Add(IntersectGreatCircle(previous, current, halfspace));
            }

            if (value >= 0.0)
            {
                result.Add(current);
            }

            previous = current;
            previousValue = value;
        }

        // Intersections exactly at a polygon corner must not leave a zero-
        // length edge or produce two contradicting neighbor declarations.
        var unique = new List<PlanetVector>(result.Count);
        foreach (var vertex in result)
        {
            if (unique.Count == 0 || PlanetVector.Dot(unique[^1], vertex) < 1.0 - 1e-13)
            {
                unique.Add(vertex);
            }
        }

        if (unique.Count > 1 && PlanetVector.Dot(unique[0], unique[^1]) > 1.0 - 1e-13)
        {
            unique.RemoveAt(unique.Count - 1);
        }

        return unique;
    }

    private static PlanetVector IntersectGreatCircle(PlanetVector first, PlanetVector second, PlanetVector halfspace)
    {
        // Spherical binary search on the SHORT great-circle arc. Unlike planar
        // linear interpolation, every intermediate point remains on the sphere.
        var firstInside = PlanetVector.Dot(first, halfspace) >= 0.0;
        for (var step = 0; step < 48; step++)
        {
            var midpoint = PlanetVector.Normalize(first + second);
            if ((PlanetVector.Dot(midpoint, halfspace) >= 0.0) == firstInside)
            {
                first = midpoint;
            }
            else
            {
                second = midpoint;
            }
        }

        return PlanetVector.Normalize(first + second);
    }

    private static double SphericalAreaSteradians(PlanetVector center, List<PlanetVector> polygon)
    {
        var area = 0.0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            var triangleNumerator = Math.Abs(PlanetVector.Dot(center, PlanetVector.Cross(a, b)));
            var triangleDenominator = 1.0 + PlanetVector.Dot(center, a) +
                PlanetVector.Dot(a, b) + PlanetVector.Dot(b, center);
            area += 2.0 * Math.Atan2(triangleNumerator, triangleDenominator);
        }

        return area;
    }

    private static void RecordSharedFaces(
        int index,
        PlanetVector[] sites,
        List<PlanetVector> polygon,
        SortedDictionary<ulong, FaceCounter> faces)
    {
        for (var vertex = 0; vertex < polygon.Count; vertex++)
        {
            var start = polygon[vertex];
            var end = polygon[(vertex + 1) % polygon.Count];
            var midpoint = PlanetVector.Normalize(start + end);
            var ownerDot = PlanetVector.Dot(sites[index], midpoint);
            var neighbor = -1;
            var neighborDot = double.NegativeInfinity;
            for (var other = 0; other < sites.Length; other++)
            {
                if (other == index)
                {
                    continue;
                }

                var score = PlanetVector.Dot(sites[other], midpoint);
                if (score > neighborDot)
                {
                    neighborDot = score;
                    neighbor = other;
                }
            }

            if (neighbor < 0 || Math.Abs(ownerDot - neighborDot) > 1e-8)
            {
                throw new InvalidOperationException("A spherical face is not co-owned by two nearest material cells.");
            }

            var smaller = Math.Min(index, neighbor);
            var larger = Math.Max(index, neighbor);
            var key = ((ulong)smaller << 32) | (uint)larger;
            var length = Math.Atan2(PlanetVector.Cross(start, end).Length,
                PlanetVector.Dot(start, end)) * RadiusMeters;
            if (!faces.TryGetValue(key, out var shared))
            {
                faces.Add(key, new FaceCounter(new PlanetCrustSphericalFace(
                    new PlanetCrustParcelId((ulong)smaller + 1),
                    new PlanetCrustParcelId((ulong)larger + 1), start, end, length)));
            }
            else
            {
                if (shared.Occurrences != 1 || Math.Abs(shared.Face.LengthMeters - length) > 0.1 ||
                    !SameSharedEndpoints(shared.Face.Start, shared.Face.End, start, end))
                {
                    throw new InvalidOperationException(
                        $"Adjacent crust cells disagree: owner={index + 1} neighbor={neighbor + 1} " +
                        $"pair={smaller + 1}-{larger + 1} count={shared.Occurrences} " +
                        $"firstLength={shared.Face.LengthMeters:R} secondLength={length:R} " +
                        $"startGap={(shared.Face.Start - end).Length:R} endGap={(shared.Face.End - start).Length:R}.");
                }

                shared.Occurrences++;
            }
        }
    }

    private static bool SameSharedEndpoints(PlanetVector firstStart, PlanetVector firstEnd,
        PlanetVector secondStart, PlanetVector secondEnd) =>
        (firstStart - secondEnd).Length < 1e-7 && (firstEnd - secondStart).Length < 1e-7;

    private int BuildIndex(int[] indices, int depth, List<KdNode> nodes)
    {
        if (indices.Length == 0)
        {
            return -1;
        }

        var axis = depth % 3;
        Array.Sort(indices, (a, b) =>
        {
            var comparison = Coordinate(sites[a], axis).CompareTo(Coordinate(sites[b], axis));
            return comparison != 0 ? comparison : a.CompareTo(b);
        });
        var middle = indices.Length / 2;
        var index = nodes.Count;
        nodes.Add(default);
        var left = BuildIndex(indices[..middle], depth + 1, nodes);
        var right = BuildIndex(indices[(middle + 1)..], depth + 1, nodes);
        nodes[index] = new KdNode(indices[middle], axis, left, right);
        return index;
    }

    private void SearchIndex(int nodeIndex, PlanetVector target, ref int bestIndex, ref double bestDistance)
    {
        if (nodeIndex < 0)
        {
            return;
        }

        var node = tree[nodeIndex];
        var candidate = sites[node.SiteIndex];
        var distance = (candidate - target).Length;
        if (distance < bestDistance - 1e-14 ||
            Math.Abs(distance - bestDistance) <= 1e-14 && node.SiteIndex < bestIndex)
        {
            bestIndex = node.SiteIndex;
            bestDistance = distance;
        }

        var offset = Coordinate(target, node.Axis) - Coordinate(candidate, node.Axis);
        var near = offset < 0.0 ? node.Left : node.Right;
        var far = offset < 0.0 ? node.Right : node.Left;
        SearchIndex(near, target, ref bestIndex, ref bestDistance);
        if (Math.Abs(offset) <= bestDistance + 1e-14)
        {
            SearchIndex(far, target, ref bestIndex, ref bestDistance);
        }
    }

    private static double Coordinate(PlanetVector direction, int axis) => axis switch
    {
        0 => direction.X,
        1 => direction.Y,
        _ => direction.Z
    };

    private sealed class FaceCounter(PlanetCrustSphericalFace face)
    {
        public PlanetCrustSphericalFace Face { get; } = face;
        public int Occurrences { get; set; } = 1;
    }

    private readonly record struct KdNode(int SiteIndex, int Axis, int Left, int Right);
}
