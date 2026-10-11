using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Geographic overlap of an old and a new spherical control cell. A transfer
/// moves ONLY a passive Eulerian tracer, never permanent lithospheric material.
/// </summary>
public readonly record struct PlanetCrustGeographicTracerTransfer(
    PlanetCrustParcelId FromCell,
    PlanetCrustParcelId ToCell,
    double OverlapAreaSquareMeters,
    double TransferredTracerKilograms);

public readonly record struct PlanetCrustGeographicTracerCell(
    PlanetCrustParcelId CellId,
    double GeographicAreaSquareMeters,
    double TracerKilograms)
{
    public double TracerKilogramsPerSquareMeter => TracerKilograms / GeographicAreaSquareMeters;
}

public sealed class PlanetCrustGeographicRemapSnapshot
{
    internal PlanetCrustGeographicRemapSnapshot(
        PlanetWorldIdentity world,
        PlanetGeologicalEpoch fromEpoch,
        PlanetGeologicalEpoch toEpoch,
        double originalTracerKilograms,
        PlanetCrustGeographicTracerCell[] cells,
        PlanetCrustGeographicTracerTransfer[] transfers,
        double worstGeographicAreaMismatchFraction)
    {
        World = world;
        FromEpoch = fromEpoch;
        ToEpoch = toEpoch;
        OriginalTracerKilograms = originalTracerKilograms;
        Cells = Array.AsReadOnly(cells);
        Transfers = Array.AsReadOnly(transfers);
        WorstGeographicAreaMismatchFraction = worstGeographicAreaMismatchFraction;
    }

    public PlanetWorldIdentity World { get; }
    public PlanetGeologicalEpoch FromEpoch { get; }
    public PlanetGeologicalEpoch ToEpoch { get; }
    public double OriginalTracerKilograms { get; }
    public IReadOnlyList<PlanetCrustGeographicTracerCell> Cells { get; }
    public IReadOnlyList<PlanetCrustGeographicTracerTransfer> Transfers { get; }
    public double FinalTracerKilograms => Cells.Sum(cell => cell.TracerKilograms);
    public double WorstGeographicAreaMismatchFraction { get; }
}

/// <summary>
/// Regrids a PASSIVE Eulerian geographic tracer between two complete, immutable
/// spherical Voronoi partitions using spherical polygon intersections.
///
/// Each old cell's tracer is assumed uniform over that old geographic area.
/// Its overlapping new cells receive exactly its tracer mass; final donor
/// transfer absorbs floating-point rounding. A conservative regrid is NOT
/// advective transport of solid crust: persistent material identity, composition,
/// reference mass and plate ownership are untouched by this solver.
///
/// Validates the physical overlap coverage of BOTH partitions before remapping.
/// The small per-donor area renormalization closes floating-point bookkeeping,
/// not missing large regions, and does not invent source material.
/// </summary>
public sealed class PlanetCrustEpochConservativeRemapper
{
    public const double MaximumRelativeGeographicAreaError = 1e-4;

    public PlanetCrustGeographicRemapSnapshot Remap(
        PlanetCrustParcelCatalog catalog,
        PlanetCrustOwnershipHistory history,
        PlanetCrustSphericalControlVolumes from,
        PlanetCrustSphericalControlVolumes to,
        IReadOnlyList<double> tracerKilograms)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(tracerKilograms);
        if (tracerKilograms.Count != catalog.Parcels.Count)
        {
            throw new ArgumentException("One nonnegative geographic tracer amount is required per reference material cell.", nameof(tracerKilograms));
        }

        // Includes strict world, epoch-order, stable material-ID, and exact
        // material-history site validation (not merely a matching world seed).
        _ = PlanetCrustEpochTopologyTransition.Compare(catalog, history, from, to);
        var total = 0.0;
        foreach (var value in tracerKilograms)
        {
            if (!double.IsFinite(value) || value < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(tracerKilograms), "Every geographic tracer amount must be finite and nonnegative.");
            }

            total += value;
        }

        if (!double.IsFinite(total))
        {
            throw new ArgumentOutOfRangeException(nameof(tracerKilograms), "Total geographic tracer mass must be finite.");
        }

        var count = catalog.Parcels.Count;
        // Identical reconstructed partitions must be an EXACT identity map,
        // rather than accumulating numerical clipping noise at shared faces.
        if (Enumerable.Range(0, count).All(i =>
            from.GetSiteDirection(catalog.Parcels[i].Id) == to.GetSiteDirection(catalog.Parcels[i].Id)))
        {
            var identityCells = catalog.Parcels.Select((material, i) =>
                new PlanetCrustGeographicTracerCell(material.Id, to.Cells[i].GeographicAreaSquareMeters,
                    tracerKilograms[i])).ToArray();
            var identityTransfers = catalog.Parcels.Select((material, i) =>
                new PlanetCrustGeographicTracerTransfer(material.Id, material.Id,
                    to.Cells[i].GeographicAreaSquareMeters, tracerKilograms[i])).ToArray();
            return new PlanetCrustGeographicRemapSnapshot(catalog.World, from.Epoch, to.Epoch,
                total, identityCells, identityTransfers, 0.0);
        }

        var sourceSites = Enumerable.Range(0, count).Select(i => from.GetSiteDirection(catalog.Parcels[i].Id)).ToArray();
        var targetSites = Enumerable.Range(0, count).Select(i => to.GetSiteDirection(catalog.Parcels[i].Id)).ToArray();
        var sourceRadii = Enumerable.Range(0, count).Select(i => BoundingRadius(sourceSites[i], from.Cells[i])).ToArray();
        var targetRadii = Enumerable.Range(0, count).Select(i => BoundingRadius(targetSites[i], to.Cells[i])).ToArray();
        var targetPlanes = Enumerable.Range(0, count).Select(i => GetHalfspaces(to.Cells[i], targetSites[i])).ToArray();
        var remapped = new double[count];
        var targetCoveredArea = new double[count];
        var transfers = new List<PlanetCrustGeographicTracerTransfer>(count * 4);
        var worstError = 0.0;

        for (var oldIndex = 0; oldIndex < count; oldIndex++)
        {
            var oldCell = from.Cells[oldIndex];
            var overlaps = new List<(int TargetIndex, double Area)>();
            var totalOverlap = 0.0;
            for (var newIndex = 0; newIndex < count; newIndex++)
            {
                var maximumAngle = Math.Min(Math.PI, sourceRadii[oldIndex] + targetRadii[newIndex] + 1e-7);
                if (PlanetVector.Dot(sourceSites[oldIndex], targetSites[newIndex]) < Math.Cos(maximumAngle))
                {
                    continue;
                }

                var polygon = oldCell.BoundaryVertices.ToList();
                foreach (var plane in targetPlanes[newIndex])
                {
                    polygon = Clip(polygon, plane);
                    if (polygon.Count < 3)
                    {
                        break;
                    }
                }

                if (polygon.Count < 3)
                {
                    continue;
                }

                var overlap = SphericalPolygonArea(polygon);
                if (overlap <= oldCell.GeographicAreaSquareMeters * 1e-13)
                {
                    continue;
                }

                overlaps.Add((newIndex, overlap));
                totalOverlap += overlap;
                targetCoveredArea[newIndex] += overlap;
            }

            var error = Math.Abs(totalOverlap - oldCell.GeographicAreaSquareMeters) / oldCell.GeographicAreaSquareMeters;
            worstError = Math.Max(error, worstError);
            if (overlaps.Count == 0 || error > MaximumRelativeGeographicAreaError)
            {
                throw new InvalidOperationException(
                    $"Geographic old-cell overlap fails closure at material {oldCell.MaterialId.Value}: relative error {error:R}.");
            }

            var distributed = 0.0;
            for (var j = 0; j < overlaps.Count; j++)
            {
                var (newIndex, overlap) = overlaps[j];
                var transfer = j == overlaps.Count - 1 ? tracerKilograms[oldIndex] - distributed :
                    tracerKilograms[oldIndex] * (overlap / totalOverlap);
                distributed += transfer;
                remapped[newIndex] += transfer;
                transfers.Add(new PlanetCrustGeographicTracerTransfer(oldCell.MaterialId,
                    to.Cells[newIndex].MaterialId, overlap, transfer));
            }
        }

        var result = new PlanetCrustGeographicTracerCell[count];
        for (var index = 0; index < count; index++)
        {
            var cell = to.Cells[index];
            var error = Math.Abs(targetCoveredArea[index] - cell.GeographicAreaSquareMeters) / cell.GeographicAreaSquareMeters;
            worstError = Math.Max(worstError, error);
            if (error > MaximumRelativeGeographicAreaError)
            {
                throw new InvalidOperationException(
                    $"Geographic new-cell overlap fails closure at material {cell.MaterialId.Value}: relative error {error:R}.");
            }

            result[index] = new PlanetCrustGeographicTracerCell(cell.MaterialId, cell.GeographicAreaSquareMeters, remapped[index]);
        }

        return new PlanetCrustGeographicRemapSnapshot(catalog.World, from.Epoch, to.Epoch,
            total, result, transfers.ToArray(), worstError);
    }

    private static double BoundingRadius(PlanetVector center, PlanetCrustSphericalCell cell) =>
        cell.BoundaryVertices.Max(vertex => Math.Acos(Math.Clamp(PlanetVector.Dot(center, vertex), -1.0, 1.0)));

    private static PlanetVector[] GetHalfspaces(PlanetCrustSphericalCell cell, PlanetVector site)
    {
        var planes = new PlanetVector[cell.BoundaryVertices.Count];
        for (var index = 0; index < planes.Length; index++)
        {
            var a = cell.BoundaryVertices[index];
            var b = cell.BoundaryVertices[(index + 1) % planes.Length];
            var plane = PlanetVector.Normalize(PlanetVector.Cross(a, b));
            planes[index] = PlanetVector.Dot(plane, site) >= 0.0 ? plane : plane * -1.0;
        }

        return planes;
    }

    private static List<PlanetVector> Clip(List<PlanetVector> polygon, PlanetVector normal)
    {
        if (polygon.Count == 0)
        {
            return polygon;
        }

        var clipped = new List<PlanetVector>(polygon.Count + 2);
        var last = polygon[^1];
        var lastDot = PlanetVector.Dot(last, normal);
        foreach (var current in polygon)
        {
            var dot = PlanetVector.Dot(current, normal);
            if ((lastDot >= 0) != (dot >= 0))
            {
                clipped.Add(Intersect(last, current, normal));
            }

            if (dot >= 0)
            {
                clipped.Add(current);
            }

            last = current;
            lastDot = dot;
        }

        var unique = new List<PlanetVector>(clipped.Count);
        foreach (var vertex in clipped)
        {
            if (unique.Count == 0 || (unique[^1] - vertex).Length > 1e-9)
            {
                unique.Add(vertex);
            }
        }

        if (unique.Count > 1 && (unique[0] - unique[^1]).Length < 1e-9)
        {
            unique.RemoveAt(unique.Count - 1);
        }

        return unique;
    }

    private static PlanetVector Intersect(PlanetVector start, PlanetVector end, PlanetVector plane)
    {
        var crossing = PlanetVector.Cross(PlanetVector.Cross(start, end), plane);
        if (crossing.Length > 1e-12)
        {
            var intersection = PlanetVector.Normalize(crossing);
            if (PlanetVector.Dot(intersection, start + end) < 0)
            {
                intersection = intersection * -1;
            }

            if (PlanetVector.Dot(intersection, start) >= PlanetVector.Dot(start, end) - 1e-12 &&
                PlanetVector.Dot(intersection, end) >= PlanetVector.Dot(start, end) - 1e-12)
            {
                return intersection;
            }
        }

        var firstInside = PlanetVector.Dot(start, plane) >= 0.0;
        for (var step = 0; step < 64; step++)
        {
            var middle = PlanetVector.Normalize(start + end);
            if ((PlanetVector.Dot(middle, plane) >= 0) == firstInside)
            {
                start = middle;
            }
            else
            {
                end = middle;
            }
        }

        return PlanetVector.Normalize(start + end);
    }

    private static double SphericalPolygonArea(List<PlanetVector> polygon)
    {
        var center = PlanetVector.Normalize(polygon.Aggregate(new PlanetVector(0, 0, 0), (a, b) => a + b));
        var steradians = 0.0;
        for (var index = 0; index < polygon.Count; index++)
        {
            var first = polygon[index];
            var second = polygon[(index + 1) % polygon.Count];
            var numerator = Math.Abs(PlanetVector.Dot(center, PlanetVector.Cross(first, second)));
            var denominator = 1.0 + PlanetVector.Dot(center, first) +
                PlanetVector.Dot(first, second) + PlanetVector.Dot(second, center);
            steradians += 2.0 * Math.Atan2(numerator, denominator);
        }

        var radius = PlanetTectonicPlateKinematics.NominalPlanetRadiusMeters;
        return steradians * radius * radius;
    }
}
