using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Explicit mass-exchange mechanism. Spreading moves accessible upper-mantle
/// budget into crust; subduction returns crust mass to that mantle budget.
/// These are research reservoir events, NOT magma, recycled composition,
/// crust material parcel deletion or full mantle mass/energy mechanics.
/// </summary>
public enum PlanetCrustMantleProcess
{
    Spreading,
    Subduction
}

public readonly record struct PlanetCrustMantleExchange(
    PlanetCrustParcelId MaterialId,
    PlanetCrustParcelId NeighborId,
    PlanetCrustMantleProcess Process,
    double MassKilograms);

public readonly record struct PlanetCrustMantleExchangeEvidence(
    PlanetCrustMantleExchange Exchange,
    PlanetGeologicalEpoch Epoch,
    PlanetPlateBoundaryKind BoundaryKind,
    double ContactInfluence,
    double CompressionMetersPerMillionYears);

public readonly record struct PlanetCrustMantleReservoirColumn(
    PlanetCrustMaterialParcel Material,
    PlanetTectonicPlateId OwningPlate,
    double CrustMassKilograms,
    double ExchangeableMantleMassKilograms)
{
    public double TotalMassKilograms => CrustMassKilograms + ExchangeableMantleMassKilograms;
}

public sealed class PlanetCrustMantleReservoirSnapshot
{
    internal PlanetCrustMantleReservoirSnapshot(
        PlanetWorldIdentity world,
        PlanetGeologicalEpoch epoch,
        PlanetCrustMantleReservoirColumn[] columns,
        PlanetCrustMantleExchangeEvidence[] events,
        double initialTotalMassKilograms)
    {
        World = world;
        Epoch = epoch;
        Columns = Array.AsReadOnly(columns);
        Events = Array.AsReadOnly(events);
        InitialTotalMassKilograms = initialTotalMassKilograms;
    }

    public PlanetWorldIdentity World { get; }
    public PlanetGeologicalEpoch Epoch { get; }
    public IReadOnlyList<PlanetCrustMantleReservoirColumn> Columns { get; }
    public IReadOnlyList<PlanetCrustMantleExchangeEvidence> Events { get; }
    public double InitialTotalMassKilograms { get; }
    public double TotalCrustMassKilograms => Columns.Sum(column => column.CrustMassKilograms);
    public double TotalExchangeableMantleMassKilograms => Columns.Sum(column => column.ExchangeableMantleMassKilograms);
    public double TotalMassKilograms => TotalCrustMassKilograms + TotalExchangeableMantleMassKilograms;
}

/// <summary>
/// Immutable forward-time, bounded CRUST ↔ ACCESSIBLE MANTLE mass ledger.
/// This deliberately works alongside the backward reconstructed kinematic
/// ownership history without mistaking its direction for geological time.
///
/// Material (world,id,composition,reference mass) is never deleted or created.
/// The finite mantle budget is a per-parcel exchangeable research reservoir,
/// NOT the mass of the physical planetary mantle. Potential events are based
/// on canonical plate-relative velocity and an ACTUAL present-epoch Voronoi
/// face joining different owner plates. All events are validated atomically
/// against one starting snapshot, with aggregate per-column budgets.
/// </summary>
public sealed class PlanetCrustMantleConservationSolver
{
    public const double InitialMantleBudgetMultiplier = 4.0;
    public const double MaximumTransferredFractionPerStep = 0.01;
    public const long MaximumStepYears = 1_000_000;
    // Effective ridge/trench contact width is an explicit uncalibrated model
    // coefficient; not a claim about real fault width or crust rheology.
    private const double EffectiveInteractionScale = 0.10;
    private readonly PlanetTectonicBoundaryKinematics boundary = new();

    public PlanetCrustMantleReservoirSnapshot CreateInitial(
        PlanetCrustParcelCatalog catalog,
        PlanetCrustOwnershipHistory history,
        PlanetCrustSphericalControlVolumes geometry)
    {
        ValidateWorld(catalog, history, geometry);
        var columns = catalog.Parcels.Select(material => new PlanetCrustMantleReservoirColumn(material,
            history.GetState(material.Id, geometry.Epoch).OwningPlate,
            material.ReferenceMassKilograms,
            material.ReferenceMassKilograms * InitialMantleBudgetMultiplier)).ToArray();
        return new PlanetCrustMantleReservoirSnapshot(catalog.World, geometry.Epoch, columns, [],
            columns.Sum(column => column.TotalMassKilograms));
    }

    /// <summary>
    /// Deterministically propose a small bounded set of physically signed
    /// transfers at the END of a geological step. Candidate material must
    /// already be oceanic: this release models no crust composition changes.
    /// The proposals do not modify any immutable snapshot until accepted.
    /// </summary>
    public IReadOnlyList<PlanetCrustMantleExchange> SuggestExchanges(
        PlanetCrustMantleReservoirSnapshot state,
        PlanetCrustParcelCatalog catalog,
        PlanetCrustOwnershipHistory history,
        PlanetCrustSphericalControlVolumes targetGeometry,
        int maxResults = 32)
    {
        ValidateState(state, catalog, history, targetGeometry, allowEarlierState: true);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxResults, catalog.Parcels.Count);
        ValidateForwardTime(state.Epoch, targetGeometry.Epoch);
        var proposed = new List<PlanetCrustMantleExchange>();
        var used = new HashSet<PlanetCrustParcelId>();
        var durationMillionYears = (state.Epoch.YearsBeforePresent - targetGeometry.Epoch.YearsBeforePresent) / 1_000_000.0;
        if (durationMillionYears == 0.0)
        {
            return proposed.AsReadOnly();
        }

        foreach (var face in targetGeometry.Faces)
        {
            var first = state.Columns[(int)face.First.Value - 1];
            var second = state.Columns[(int)face.Second.Value - 1];
            if (targetGeometry.GetOwningPlate(face.First) == targetGeometry.GetOwningPlate(face.Second))
            {
                continue;
            }

            var contact = Sample(face, targetGeometry, catalog.World.Seed);
            if (!IsValidContact(contact, targetGeometry.GetOwningPlate(face.First), targetGeometry.GetOwningPlate(face.Second)))
            {
                continue;
            }

            var process = ContactProcess(contact);
            if (process is null)
            {
                continue;
            }

            var source = first.Material.Composition == PlanetCrustComposition.Oceanic ? first :
                second.Material.Composition == PlanetCrustComposition.Oceanic ? second : (PlanetCrustMantleReservoirColumn?)null;
            if (source is null || !used.Add(source.Value.Material.Id))
            {
                continue;
            }

            var eligible = source.Value;
            var reservoir = process == PlanetCrustMantleProcess.Spreading
                ? eligible.ExchangeableMantleMassKilograms : eligible.CrustMassKilograms;
            var budget = reservoir * MaximumTransferredFractionPerStep;
            var move = Math.Min(budget, Math.Abs(contact.CompressionMetersPerMillionYears) * durationMillionYears *
                face.LengthMeters * eligible.Material.ReferenceThicknessMeters *
                eligible.Material.ReferenceDensityKilogramsPerCubicMeter *
                contact.ContactInfluence * EffectiveInteractionScale);
            if (!double.IsFinite(move) || move <= 0.0)
            {
                continue;
            }

            proposed.Add(new PlanetCrustMantleExchange(eligible.Material.Id,
                eligible.Material.Id == face.First ? face.Second : face.First, process.Value, move));
            if (proposed.Count == maxResults)
            {
                break;
            }
        }

        return proposed.AsReadOnly();
    }

    public PlanetCrustMantleReservoirSnapshot Advance(
        PlanetCrustMantleReservoirSnapshot state,
        PlanetCrustParcelCatalog catalog,
        PlanetCrustOwnershipHistory history,
        PlanetCrustSphericalControlVolumes targetGeometry,
        IReadOnlyList<PlanetCrustMantleExchange> exchanges)
    {
        ArgumentNullException.ThrowIfNull(exchanges);
        ValidateState(state, catalog, history, targetGeometry, allowEarlierState: true);
        ValidateForwardTime(state.Epoch, targetGeometry.Epoch);

        // Validate the current material snapshot's plate ownership separately
        // from the target: remeshing can change owners at explicit checkpoints.
        var outgoingGeometry = PlanetCrustSphericalControlVolumes.BuildAtEpoch(catalog, history, state.Epoch);
        if (state.Columns.Where((column, i) => column.OwningPlate != outgoingGeometry.GetOwningPlate(column.Material.Id)).Any())
        {
            throw new ArgumentException("Starting material ownership disagrees with the geological source epoch.", nameof(state));
        }

        var faces = targetGeometry.Faces.ToDictionary(face => FaceKey(face.First, face.Second));
        var usedPairs = new HashSet<ulong>();
        var deltaCrust = new double[state.Columns.Count];
        var evidence = new PlanetCrustMantleExchangeEvidence[exchanges.Count];

        for (var i = 0; i < exchanges.Count; i++)
        {
            var request = exchanges[i];
            var key = FaceKey(request.MaterialId, request.NeighborId);
            if (request.MaterialId == request.NeighborId || !faces.TryGetValue(key, out var face) ||
                !usedPairs.Add(key) || !double.IsFinite(request.MassKilograms) || request.MassKilograms <= 0.0 ||
                request.Process is not PlanetCrustMantleProcess.Spreading and not PlanetCrustMantleProcess.Subduction)
            {
                throw new ArgumentException("An event must refer to a unique existing face, known process and finite positive material mass.", nameof(exchanges));
            }

            var index = (int)request.MaterialId.Value - 1;
            var source = state.Columns[index];
            if (source.Material.Composition != PlanetCrustComposition.Oceanic)
            {
                throw new ArgumentException("Only existing oceanic material supports this research spreading/subduction exchange.", nameof(exchanges));
            }

            var firstPlate = targetGeometry.GetOwningPlate(face.First);
            var secondPlate = targetGeometry.GetOwningPlate(face.Second);
            if (firstPlate == secondPlate)
            {
                throw new ArgumentException("Reservoir exchange requires different plate owners across the canonical face.", nameof(exchanges));
            }

            var contact = Sample(face, targetGeometry, catalog.World.Seed);
            if (!IsValidContact(contact, firstPlate, secondPlate) || ContactProcess(contact) != request.Process)
            {
                throw new ArgumentException("The event conflicts with the actual signed plate-relative motion at this face.", nameof(exchanges));
            }

            var sign = request.Process == PlanetCrustMantleProcess.Spreading ? 1.0 : -1.0;
            deltaCrust[index] += sign * request.MassKilograms;
            evidence[i] = new PlanetCrustMantleExchangeEvidence(request, targetGeometry.Epoch,
                contact.Kind, contact.ContactInfluence, contact.CompressionMetersPerMillionYears);
        }

        // All events are evaluated against the same beginning snapshot.
        // Bounds protect BOTH reservoirs and avoid event-order dependence.
        var result = new PlanetCrustMantleReservoirColumn[state.Columns.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var before = state.Columns[i];
            var delta = deltaCrust[i];
            var budgetSource = delta >= 0 ? before.ExchangeableMantleMassKilograms : before.CrustMassKilograms;
            if (!double.IsFinite(delta) ||
                Math.Abs(delta) > budgetSource * MaximumTransferredFractionPerStep + budgetSource * 1e-12)
            {
                throw new ArgumentException("Combined events exceed the finite source reservoir's per-step budget.", nameof(exchanges));
            }

            var crust = before.CrustMassKilograms + delta;
            var mantle = before.ExchangeableMantleMassKilograms - delta;
            if (!double.IsFinite(crust) || !double.IsFinite(mantle) || crust <= 0 || mantle < 0)
            {
                throw new ArgumentException("The proposed events would deplete a material reservoir.", nameof(exchanges));
            }

            result[i] = new PlanetCrustMantleReservoirColumn(before.Material,
                targetGeometry.GetOwningPlate(before.Material.Id), crust, mantle);
        }

        var allEvents = state.Events.Concat(evidence).ToArray();
        return new PlanetCrustMantleReservoirSnapshot(state.World, targetGeometry.Epoch, result,
            allEvents, state.InitialTotalMassKilograms);
    }

    private PlanetPlateBoundarySample Sample(PlanetCrustSphericalFace face,
        PlanetCrustSphericalControlVolumes geometry, int seed)
    {
        var midpoint = PlanetVector.Normalize(face.Start + face.End);
        return boundary.Sample(midpoint, seed, geometry.Epoch);
    }

    private static PlanetCrustMantleProcess? ContactProcess(PlanetPlateBoundarySample contact) => contact.Kind switch
    {
        PlanetPlateBoundaryKind.Convergent or PlanetPlateBoundaryKind.ObliqueConvergent =>
            PlanetCrustMantleProcess.Subduction,
        PlanetPlateBoundaryKind.Divergent or PlanetPlateBoundaryKind.ObliqueDivergent =>
            PlanetCrustMantleProcess.Spreading,
        _ => null
    };

    private static bool IsValidContact(PlanetPlateBoundarySample contact,
        PlanetTectonicPlateId first, PlanetTectonicPlateId second) =>
        contact.IsAtContact && contact.Kind is not PlanetPlateBoundaryKind.Neutral and not PlanetPlateBoundaryKind.OutsideContact &&
        ((contact.FirstPlate == first && contact.SecondPlate == second) ||
         (contact.FirstPlate == second && contact.SecondPlate == first));

    private static void ValidateForwardTime(PlanetGeologicalEpoch from, PlanetGeologicalEpoch to)
    {
        var years = from.YearsBeforePresent - to.YearsBeforePresent;
        if (years < 0 || years > MaximumStepYears)
        {
            throw new ArgumentOutOfRangeException(nameof(to),
                "Forward geological steps must move from older toward younger epochs, at most 1 million years.");
        }
    }

    private static void ValidateWorld(PlanetCrustParcelCatalog catalog,
        PlanetCrustOwnershipHistory history, PlanetCrustSphericalControlVolumes geometry)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(geometry);
        if (catalog.World != history.World || catalog.World != geometry.World ||
            catalog.Parcels.Count != geometry.Cells.Count)
        {
            throw new ArgumentException("Crust, mantle and geometry must belong to one generated planet.");
        }

        for (var i = 0; i < catalog.Parcels.Count; i++)
        {
            var material = catalog.Parcels[i];
            var state = history.GetState(material.Id, geometry.Epoch);
            if (geometry.Cells[i].MaterialId != material.Id ||
                geometry.GetOwningPlate(material.Id) != state.OwningPlate ||
                (geometry.GetSiteDirection(material.Id) - state.GeographicDirection).Length > 1e-9)
            {
                throw new ArgumentException("Geology cannot use geometry from another ownership history.");
            }
        }
    }

    private static void ValidateState(PlanetCrustMantleReservoirSnapshot state,
        PlanetCrustParcelCatalog catalog,
        PlanetCrustOwnershipHistory history,
        PlanetCrustSphericalControlVolumes geometry, bool allowEarlierState)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateWorld(catalog, history, geometry);
        if (state.World != catalog.World || state.Columns.Count != catalog.Parcels.Count)
        {
            throw new ArgumentException("Reservoir state must match the world's persistent crust materials.");
        }

        for (var i = 0; i < state.Columns.Count; i++)
        {
            var column = state.Columns[i];
            if (column.Material != catalog.Parcels[i] ||
                !double.IsFinite(column.CrustMassKilograms) || column.CrustMassKilograms <= 0.0 ||
                !double.IsFinite(column.ExchangeableMantleMassKilograms) || column.ExchangeableMantleMassKilograms < 0.0)
            {
                throw new ArgumentException("Reservoir snapshot has inconsistent material identity or invalid mass.");
            }
        }
    }

    private static ulong FaceKey(PlanetCrustParcelId first, PlanetCrustParcelId second) =>
        (Math.Min(first.Value, second.Value) << 32) | Math.Max(first.Value, second.Value);
}
