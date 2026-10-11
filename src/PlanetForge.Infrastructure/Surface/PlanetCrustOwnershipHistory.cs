using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Explicit, world-scoped research proposal for a material parcel changing its
/// kinematic plate frame while reconstructing progressively older epochs.
/// The direction of this timeline is increasing years-before-present; it is NOT
/// a forward-in-time plate collision solver.
/// </summary>
public readonly record struct PlanetCrustPlateTransfer(
    PlanetWorldIdentity World,
    PlanetCrustParcelId ParcelId,
    PlanetGeologicalEpoch Epoch,
    PlanetTectonicPlateId FromPlate,
    PlanetTectonicPlateId ToPlate);

/// <summary>
/// Verified provenance of a transfer against the existing moving-centre
/// boundary proxy. Classification is kinematic, not a physical stress tensor.
/// </summary>
public readonly record struct PlanetCrustPlateTransferEvidence(
    PlanetCrustPlateTransfer Transfer,
    PlanetPlateBoundaryKind BoundaryKind,
    double ContactInfluence,
    double CompressionMetersPerMillionYears,
    double ShearMetersPerMillionYears);

/// <summary>
/// Immutable observation of one material parcel, including its plate frame
/// at a requested epoch. Reference composition/area/mass never change.
/// </summary>
public readonly record struct PlanetCrustMaterialState(
    PlanetCrustMaterialParcel Material,
    PlanetGeologicalEpoch Epoch,
    PlanetTectonicPlateId OwningPlate,
    PlanetVector GeographicDirection);

/// <summary>
/// Immutable, replayable ownership transitions for the A2.5 material sample
/// catalogue. It explicitly records any plate-frame change rather than
/// silently relabelling material from the nearest moving centre every frame.
/// Each transition preserves the exact spherical position at its epoch and
/// carries an independently validated boundary-kinematics observation.
/// </summary>
public sealed class PlanetCrustOwnershipHistory
{
    private readonly PlanetCrustParcelCatalog catalog;
    private readonly PlanetTectonicPlateState[] plates;
    private readonly Dictionary<ulong, Checkpoint[]> checkpoints;
    private readonly IReadOnlyList<PlanetCrustPlateTransferEvidence> evidence;

    private PlanetCrustOwnershipHistory(
        PlanetCrustParcelCatalog catalog,
        PlanetTectonicPlateState[] plates,
        Dictionary<ulong, Checkpoint[]> checkpoints,
        PlanetCrustPlateTransferEvidence[] evidence)
    {
        this.catalog = catalog;
        this.plates = plates;
        this.checkpoints = checkpoints;
        this.evidence = Array.AsReadOnly(evidence);
    }

    public PlanetWorldIdentity World => catalog.World;
    public double TotalReferenceMassKilograms => catalog.TotalReferenceMassKilograms;
    public IReadOnlyList<PlanetCrustPlateTransferEvidence> Transfers => evidence;

    public static PlanetCrustOwnershipHistory Create(PlanetCrustParcelCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var plates = new PlanetTectonicPlateKinematics().GetPlates(catalog.World.Seed).ToArray();
        if (plates.Any(plate => plate.Id.World != catalog.World))
        {
            throw new ArgumentException("The material catalogue must match the canonical plate generation.", nameof(catalog));
        }

        return new PlanetCrustOwnershipHistory(catalog, plates, new Dictionary<ulong, Checkpoint[]>(), []);
    }

    /// <summary>
    /// Apply one explicit and locally verified plate transfer; never modify
    /// the previous history or other parcels. A moving-centre proxy boundary
    /// must contain the material at the transfer epoch. This is intentionally
    /// not an automatic plate assignment or geological force law.
    /// </summary>
    public PlanetCrustOwnershipHistory WithTransfer(PlanetCrustPlateTransfer transfer)
    {
        ValidateTransfer(transfer);
        var id = transfer.ParcelId.Value;
        var previous = checkpoints.GetValueOrDefault(id) ?? [];
        if (previous.Length > 0 && previous[^1].Epoch.YearsBeforePresent >= transfer.Epoch.YearsBeforePresent)
        {
            throw new ArgumentException("Parcel transfers must advance strictly toward older epochs.", nameof(transfer));
        }

        var material = catalog.Parcels[(int)id - 1];
        var state = GetState(material.Id, transfer.Epoch);
        if (state.OwningPlate != transfer.FromPlate)
        {
            throw new InvalidOperationException("The transfer source plate does not own this parcel at the requested epoch.");
        }

        var contact = new PlanetTectonicBoundaryKinematics().Sample(state.GeographicDirection, World.Seed, transfer.Epoch);
        if (!contact.IsAtContact || contact.Kind == PlanetPlateBoundaryKind.Neutral ||
            !Matches(contact, transfer.FromPlate, transfer.ToPlate))
        {
            throw new InvalidOperationException("The proposed transfer is not supported by the canonical boundary proxy.");
        }

        var next = new Dictionary<ulong, Checkpoint[]>(checkpoints)
        {
            [id] = [.. previous, new Checkpoint(transfer.Epoch, transfer.ToPlate, state.GeographicDirection)]
        };
        var records = new PlanetCrustPlateTransferEvidence[evidence.Count + 1];
        for (var i = 0; i < evidence.Count; i++)
        {
            records[i] = evidence[i];
        }

        records[^1] = new PlanetCrustPlateTransferEvidence(transfer, contact.Kind, contact.ContactInfluence,
            contact.CompressionMetersPerMillionYears, contact.ShearMetersPerMillionYears);
        return new PlanetCrustOwnershipHistory(catalog, plates, next, records);
    }

    public PlanetCrustMaterialState GetState(PlanetCrustParcelId id, PlanetGeologicalEpoch epoch)
    {
        if (id.Value == 0 || id.Value > (ulong)catalog.Parcels.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "Material is not present in this generated world.");
        }

        var material = catalog.Parcels[(int)id.Value - 1];
        var direction = material.PresentDirection;
        var origin = PlanetGeologicalEpoch.Present;
        var owningPlate = material.InitialPlate;
        if (checkpoints.TryGetValue(id.Value, out var timeline))
        {
            foreach (var item in timeline)
            {
                if (item.Epoch.YearsBeforePresent > epoch.YearsBeforePresent)
                {
                    break;
                }

                direction = item.Direction;
                origin = item.Epoch;
                owningPlate = item.Plate;
            }
        }

        if (origin != epoch)
        {
            var plate = plates[owningPlate.Index];
            direction = plate.Move(plate.MapToPresent(direction, origin), epoch);
        }

        return new PlanetCrustMaterialState(material, epoch, owningPlate, direction);
    }

    /// <summary>
    /// Read a fixed geographic observation window across moving material.
    /// Bounded by the 1,024-parcel research catalogue, not used by GPU LOD.
    /// </summary>
    public IReadOnlyList<PlanetCrustMaterialState> FindNear(
        PlanetVector geographicDirection,
        PlanetGeologicalEpoch epoch,
        double radiusRadians)
    {
        if (!double.IsFinite(radiusRadians) || radiusRadians < 0.0 || radiusRadians > Math.PI)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusRadians));
        }

        var direction = PlanetVector.Normalize(geographicDirection);
        var dotThreshold = Math.Cos(radiusRadians);
        var result = new List<PlanetCrustMaterialState>();
        foreach (var material in catalog.Parcels)
        {
            var state = GetState(material.Id, epoch);
            if (PlanetVector.Dot(state.GeographicDirection, direction) >= dotThreshold)
            {
                result.Add(state);
            }
        }

        return result.AsReadOnly();
    }

    /// <summary>
    /// Suggest bounded, deterministic near-contact plate-frame transfers.
    /// A suggestion is only evidence: the caller must explicitly accept it
    /// through WithTransfer. No automatic subduction, material creation,
    /// fragmentation or force-driven geological simulation is claimed.
    /// </summary>
    public IReadOnlyList<PlanetCrustPlateTransferEvidence> SuggestTransfers(PlanetGeologicalEpoch epoch, int maxResults = 32)
    {
        if (epoch.YearsBeforePresent == 0)
        {
            throw new ArgumentException("Present-day reference ownership cannot be modified.", nameof(epoch));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxResults, catalog.Parcels.Count);

        var boundary = new PlanetTectonicBoundaryKinematics();
        var result = new List<PlanetCrustPlateTransferEvidence>();
        foreach (var material in catalog.Parcels)
        {
            if (checkpoints.TryGetValue(material.Id.Value, out var prior) &&
                prior[^1].Epoch.YearsBeforePresent >= epoch.YearsBeforePresent)
            {
                continue;
            }

            var state = GetState(material.Id, epoch);
            var contact = boundary.Sample(state.GeographicDirection, World.Seed, epoch);
            if (!contact.IsAtContact || contact.Kind == PlanetPlateBoundaryKind.Neutral)
            {
                continue;
            }

            var target = contact.FirstPlate == state.OwningPlate ? contact.SecondPlate :
                contact.SecondPlate == state.OwningPlate ? contact.FirstPlate : (PlanetTectonicPlateId?)null;
            if (target is null)
            {
                continue;
            }

            var transfer = new PlanetCrustPlateTransfer(World, material.Id, epoch, state.OwningPlate, target.Value);
            result.Add(new PlanetCrustPlateTransferEvidence(transfer, contact.Kind, contact.ContactInfluence,
                contact.CompressionMetersPerMillionYears, contact.ShearMetersPerMillionYears));
            if (result.Count == maxResults)
            {
                break;
            }
        }

        return result.AsReadOnly();
    }

    private void ValidateTransfer(PlanetCrustPlateTransfer transfer)
    {
        if (transfer.World != World || transfer.FromPlate.World != World || transfer.ToPlate.World != World)
        {
            throw new ArgumentException("Crust transfer and both plates must belong to the same generated world.", nameof(transfer));
        }

        if (transfer.ParcelId.Value == 0 || transfer.ParcelId.Value > (ulong)catalog.Parcels.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(transfer), "Unknown material parcel.");
        }

        if (transfer.Epoch.YearsBeforePresent == 0)
        {
            throw new ArgumentException("Present-day reference ownership is immutable; transitions need an older epoch.", nameof(transfer));
        }

        if (transfer.FromPlate == transfer.ToPlate || transfer.FromPlate.Index < 0 || transfer.ToPlate.Index < 0 ||
            transfer.FromPlate.Index >= plates.Length || transfer.ToPlate.Index >= plates.Length)
        {
            throw new ArgumentException("A transfer must reference two different canonical plates.", nameof(transfer));
        }
    }

    private static bool Matches(
        PlanetPlateBoundarySample contact,
        PlanetTectonicPlateId fromPlate,
        PlanetTectonicPlateId toPlate) =>
        (contact.FirstPlate == fromPlate && contact.SecondPlate == toPlate) ||
        (contact.FirstPlate == toPlate && contact.SecondPlate == fromPlate);

    private readonly record struct Checkpoint(
        PlanetGeologicalEpoch Epoch,
        PlanetTectonicPlateId Plate,
        PlanetVector Direction);
}
