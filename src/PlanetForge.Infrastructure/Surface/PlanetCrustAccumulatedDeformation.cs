using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// A single persistent material's forward-integrated research column.
/// The signed displacement is measured relative to its initial reference
/// column, NOT a height added to the canonical terrain renderer.
/// </summary>
public readonly record struct PlanetCrustEvolvingColumn(
    PlanetCrustParcelId MaterialId,
    PlanetCrustComposition Composition,
    PlanetTectonicPlateId OwningPlate,
    PlanetVector GeographicDirection,
    double CrustMassKilograms,
    double ExchangeableMantleMassKilograms,
    double AccumulatedLogAreaStrain,
    double AccumulatedShearMeters,
    double CrustThicknessMeters,
    double SurfaceDisplacementMeters,
    double MantleRootChangeMeters,
    double PressureBalanceResidualKilogramsPerSquareMeter);

public sealed class PlanetCrustEvolvingSnapshot
{
    internal PlanetCrustEvolvingSnapshot(
        PlanetWorldIdentity world,
        PlanetGeologicalEpoch epoch,
        PlanetCrustEvolvingColumn[] columns,
        int acceptedEvents,
        double conservedReferenceBudgetKilograms)
    {
        World = world;
        Epoch = epoch;
        Columns = Array.AsReadOnly(columns);
        AcceptedEvents = acceptedEvents;
        ConservedReferenceBudgetKilograms = conservedReferenceBudgetKilograms;
    }

    public PlanetWorldIdentity World { get; }
    public PlanetGeologicalEpoch Epoch { get; }
    public IReadOnlyList<PlanetCrustEvolvingColumn> Columns { get; }
    public int AcceptedEvents { get; }
    public double ConservedReferenceBudgetKilograms { get; }
    public double TotalMaterialBudgetKilograms =>
        Columns.Sum(column => column.CrustMassKilograms + column.ExchangeableMantleMassKilograms);
    public double MaximumAbsoluteSurfaceDisplacementMeters => Columns.Max(column => Math.Abs(column.SurfaceDisplacementMeters));
}

/// <summary>
/// Forward-time accumulation of bounded existing plate contact strain and
/// accepted crust/mantle mass exchanges over a small number of steps.
/// Converts the accumulated reference-footprint strain AND the current crust
/// mass to one Airy-compensated local column. Recalculating from the full
/// current column (rather than summing independent Airy heights) avoids
/// double-counting displacement across epochs.
///
/// This is a RESEARCH timeline, not a continental stress/viscosity model.
/// The original reference footprints remain a quadrature approximation, not
/// deforming conservative spherical solid-material polygons. No height is
/// published to the gameplay planet.
/// </summary>
public sealed class PlanetCrustAccumulatedDeformation
{
    public const int MaximumResearchSteps = 6;
    private const double MaximumAccumulatedAbsoluteLogAreaStrain = 0.30;
    private readonly PlanetCrustMantleConservationSolver reservoirs = new();
    private readonly PlanetCrustContactStrainSolver strain = new();

    public IReadOnlyList<PlanetCrustEvolvingSnapshot> Simulate(
        PlanetCrustParcelCatalog catalog,
        PlanetCrustOwnershipHistory history,
        int steps)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(history);
        if (catalog.World != history.World)
        {
            throw new ArgumentException("Crust catalogue and kinematic history must belong to the same world.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(steps, MaximumResearchSteps);

        var oldest = new PlanetGeologicalEpoch(steps * PlanetCrustMantleConservationSolver.MaximumStepYears);
        var geometry = PlanetCrustSphericalControlVolumes.BuildAtEpoch(catalog, history, oldest);
        var state = reservoirs.CreateInitial(catalog, history, geometry);
        var neighbors = PlanetCrustMaterialNeighborhood.Build(catalog);
        var accumulatedStrain = new double[catalog.Parcels.Count];
        var accumulatedShear = new double[catalog.Parcels.Count];
        var snapshots = new List<PlanetCrustEvolvingSnapshot>(steps + 1)
        {
            Observe(state, accumulatedStrain, accumulatedShear)
        };

        for (var step = steps - 1; step >= 0; step--)
        {
            var epoch = new PlanetGeologicalEpoch(step * PlanetCrustMantleConservationSolver.MaximumStepYears);
            var target = step == 0
                ? PlanetCrustSphericalControlVolumes.Build(catalog)
                : PlanetCrustSphericalControlVolumes.BuildAtEpoch(catalog, history, epoch);
            var proposed = reservoirs.SuggestExchanges(state, catalog, history, target);
            var nextState = reservoirs.Advance(state, catalog, history, target, proposed);
            var local = strain.Evaluate(catalog, history, neighbors, epoch, 1.0);

            for (var index = 0; index < accumulatedStrain.Length; index++)
            {
                accumulatedStrain[index] = Math.Clamp(accumulatedStrain[index] + local.Columns[index].LogAreaStrain,
                    -MaximumAccumulatedAbsoluteLogAreaStrain, MaximumAccumulatedAbsoluteLogAreaStrain);
                accumulatedShear[index] += local.Columns[index].ShearExposureMeters;
            }

            snapshots.Add(Observe(nextState, accumulatedStrain, accumulatedShear));
            state = nextState;
        }

        return snapshots.AsReadOnly();
    }

    /// <summary>
    /// The pressure law consumes both reservoir mass and accumulated
    /// kinematic footprint change. Oceanic crust stays oceanic throughout
    /// these research steps; chemical conversion is not being simulated.
    /// </summary>
    private static PlanetCrustEvolvingSnapshot Observe(
        PlanetCrustMantleReservoirSnapshot reservoirs,
        double[] accumulatedStrain,
        double[] accumulatedShear)
    {
        var columns = new PlanetCrustEvolvingColumn[reservoirs.Columns.Count];
        for (var index = 0; index < columns.Length; index++)
        {
            var local = reservoirs.Columns[index];
            var material = local.Material;
            var area = material.ReferenceAreaSquareMeters * Math.Exp(accumulatedStrain[index]);
            var thickness = local.CrustMassKilograms / (area * material.ReferenceDensityKilogramsPerCubicMeter);
            var state = new PlanetCrustMaterialState(material, reservoirs.Epoch,
                local.OwningPlate, local.GeographicDirection);
            var exchanged = new PlanetCrustExchangedColumn(state, area, local.CrustMassKilograms,
                0, 0, local.CrustMassKilograms, thickness, accumulatedStrain[index]);
            var coverDensity = material.Composition == PlanetCrustComposition.Oceanic
                ? PlanetCrustAiryIsostasySolver.DefaultSeawaterDensityKilogramsPerCubicMeter : 0.0;
            var airy = PlanetCrustAiryIsostasySolver.ComputeColumn(exchanged,
                PlanetCrustAiryIsostasySolver.DefaultMantleDensityKilogramsPerCubicMeter, coverDensity);

            columns[index] = new PlanetCrustEvolvingColumn(material.Id, material.Composition,
                local.OwningPlate, local.GeographicDirection,
                local.CrustMassKilograms, local.ExchangeableMantleMassKilograms,
                accumulatedStrain[index], accumulatedShear[index], thickness,
                airy.SurfaceDisplacementMeters, airy.MantleRootChangeMeters,
                airy.PressureBalanceResidualKilogramsPerSquareMeter);
        }

        return new PlanetCrustEvolvingSnapshot(reservoirs.World, reservoirs.Epoch,
            columns, reservoirs.Events.Count, reservoirs.InitialTotalMassKilograms);
    }
}
