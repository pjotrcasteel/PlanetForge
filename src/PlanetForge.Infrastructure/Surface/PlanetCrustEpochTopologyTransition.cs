using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// A moving reference material sample observed in two epoch-specific geographic
/// partitions. Geographic area is not the permanent material's quadrature area.
/// Reference mass, density and composition are unchanged by this remeshing.
/// </summary>
public readonly record struct PlanetCrustEpochCellChange(
    PlanetCrustParcelId MaterialId,
    PlanetTectonicPlateId FromPlate,
    PlanetTectonicPlateId ToPlate,
    double PreviousGeographicAreaSquareMeters,
    double CurrentGeographicAreaSquareMeters,
    double ReferenceMassKilograms,
    double PreviousMassPerGeographicSquareMeter,
    double CurrentMassPerGeographicSquareMeter,
    double MaterialTravelMeters);

public enum PlanetCrustEpochFaceChangeKind
{
    Persisted,
    Appeared,
    Disappeared
}

/// <summary>
/// Changes in a shared face between epochs, keyed by stable material ID pair.
/// A face change does not imply a source/sink of lithospheric mass.
/// </summary>
public readonly record struct PlanetCrustEpochFaceChange(
    PlanetCrustParcelId First,
    PlanetCrustParcelId Second,
    PlanetCrustEpochFaceChangeKind Kind,
    double PreviousLengthMeters,
    double CurrentLengthMeters);

/// <summary>
/// Deterministic comparison of two complete, immutably published geographic
/// Voronoi reconstructions. Geological epochs increase toward the past.
/// Changes are Eulerian remeshing observations, not a finite-volume Lagrangian
/// advection equation: geographic cells can change neighbors while parcels
/// retain reference mass and identity independently of boundary movement.
/// </summary>
public sealed class PlanetCrustEpochTopologyTransition
{
    private PlanetCrustEpochTopologyTransition(
        PlanetWorldIdentity world,
        PlanetGeologicalEpoch fromEpoch,
        PlanetGeologicalEpoch toEpoch,
        PlanetCrustEpochCellChange[] cells,
        PlanetCrustEpochFaceChange[] faces,
        double totalReferenceMassKilograms)
    {
        World = world;
        FromEpoch = fromEpoch;
        ToEpoch = toEpoch;
        CellChanges = Array.AsReadOnly(cells);
        FaceChanges = Array.AsReadOnly(faces);
        TotalReferenceMassKilograms = totalReferenceMassKilograms;
    }

    public PlanetWorldIdentity World { get; }
    public PlanetGeologicalEpoch FromEpoch { get; }
    public PlanetGeologicalEpoch ToEpoch { get; }
    public IReadOnlyList<PlanetCrustEpochCellChange> CellChanges { get; }
    public IReadOnlyList<PlanetCrustEpochFaceChange> FaceChanges { get; }
    public double TotalReferenceMassKilograms { get; }
    public double OldGeographicAreaSquareMeters => CellChanges.Sum(cell => cell.PreviousGeographicAreaSquareMeters);
    public double NewGeographicAreaSquareMeters => CellChanges.Sum(cell => cell.CurrentGeographicAreaSquareMeters);
    public int AppearedFaceCount => FaceChanges.Count(face => face.Kind == PlanetCrustEpochFaceChangeKind.Appeared);
    public int DisappearedFaceCount => FaceChanges.Count(face => face.Kind == PlanetCrustEpochFaceChangeKind.Disappeared);

    public static PlanetCrustEpochTopologyTransition Compare(
        PlanetCrustParcelCatalog catalog,
        PlanetCrustOwnershipHistory history,
        PlanetCrustSphericalControlVolumes from,
        PlanetCrustSphericalControlVolumes to)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (catalog.World != history.World || catalog.World != from.World || catalog.World != to.World ||
            catalog.Parcels.Count != from.Cells.Count || catalog.Parcels.Count != to.Cells.Count)
        {
            throw new ArgumentException("Topology transition requires the same material world in both complete epochs.");
        }

        if (from.Epoch.YearsBeforePresent > to.Epoch.YearsBeforePresent)
        {
            throw new ArgumentException("Backwards geological reconstruction must advance toward older epochs.");
        }

        var cells = new PlanetCrustEpochCellChange[catalog.Parcels.Count];
        for (var i = 0; i < cells.Length; i++)
        {
            var material = catalog.Parcels[i];
            var oldCell = from.Cells[i];
            var newCell = to.Cells[i];
            if (oldCell.MaterialId != material.Id || newCell.MaterialId != material.Id)
            {
                throw new InvalidOperationException("Remeshing has changed the persistent material identity of a cell.");
            }

            var oldState = history.GetState(material.Id, from.Epoch);
            var newState = history.GetState(material.Id, to.Epoch);
            if ((from.GetSiteDirection(material.Id) - oldState.GeographicDirection).Length > 1e-9 ||
                (to.GetSiteDirection(material.Id) - newState.GeographicDirection).Length > 1e-9 ||
                from.GetOwningPlate(material.Id) != oldState.OwningPlate ||
                to.GetOwningPlate(material.Id) != newState.OwningPlate)
            {
                throw new ArgumentException("Epoch geometry must have been reconstructed from the supplied exact ownership history.");
            }
            var angle = Math.Atan2(PlanetVector.Cross(oldState.GeographicDirection, newState.GeographicDirection).Length,
                PlanetVector.Dot(oldState.GeographicDirection, newState.GeographicDirection));
            cells[i] = new PlanetCrustEpochCellChange(
                material.Id, oldState.OwningPlate, newState.OwningPlate,
                oldCell.GeographicAreaSquareMeters, newCell.GeographicAreaSquareMeters,
                material.ReferenceMassKilograms,
                material.ReferenceMassKilograms / oldCell.GeographicAreaSquareMeters,
                material.ReferenceMassKilograms / newCell.GeographicAreaSquareMeters,
                angle * PlanetTectonicPlateKinematics.NominalPlanetRadiusMeters);
        }

        var previous = from.Faces.ToDictionary(face => FaceKey(face.First, face.Second));
        var current = to.Faces.ToDictionary(face => FaceKey(face.First, face.Second));
        var keys = previous.Keys.Concat(current.Keys).Distinct().OrderBy(key => key).ToArray();
        var faces = new PlanetCrustEpochFaceChange[keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            var wasPresent = previous.TryGetValue(key, out var a);
            var nowPresent = current.TryGetValue(key, out var b);
            var first = wasPresent ? a.First : b.First;
            var second = wasPresent ? a.Second : b.Second;
            var kind = wasPresent && nowPresent ? PlanetCrustEpochFaceChangeKind.Persisted :
                wasPresent ? PlanetCrustEpochFaceChangeKind.Disappeared : PlanetCrustEpochFaceChangeKind.Appeared;
            faces[i] = new PlanetCrustEpochFaceChange(first, second, kind,
                wasPresent ? a.LengthMeters : 0.0, nowPresent ? b.LengthMeters : 0.0);
        }

        return new PlanetCrustEpochTopologyTransition(catalog.World, from.Epoch, to.Epoch,
            cells, faces, catalog.TotalReferenceMassKilograms);
    }

    private static ulong FaceKey(PlanetCrustParcelId a, PlanetCrustParcelId b) => (a.Value << 32) | b.Value;
}
