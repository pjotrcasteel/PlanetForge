using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Local relative-motion observation between two persistent material samples.
/// Signed fractional area strain is negative for compressional shortening,
/// positive for extension. Transform shear is reported without fake uplift.
/// </summary>
public readonly record struct PlanetCrustContactInteraction(
    PlanetCrustParcelId First,
    PlanetCrustParcelId Second,
    PlanetPlateBoundaryKind Kind,
    double ContactInfluence,
    double CompressionMetersPerMillionYears,
    double ShearMetersPerMillionYears,
    double FractionalLogAreaStrainPerMaterial);

/// <summary>
/// One bounded hypothetical kinematic step, evaluated at a fixed epoch.
/// A parcel's reference mass and composition are immutable; reciprocal area
/// and column-thickness changes preserve its individual material mass.
/// </summary>
public readonly record struct PlanetCrustColumnStrain(
    PlanetCrustMaterialState State,
    double AreaSquareMeters,
    double ThicknessMeters,
    double MassKilograms,
    double LogAreaStrain,
    double ShearExposureMeters);

public sealed class PlanetCrustInteractionSnapshot
{
    internal PlanetCrustInteractionSnapshot(
        PlanetWorldIdentity world,
        PlanetGeologicalEpoch epoch,
        double durationMillionYears,
        PlanetCrustColumnStrain[] columns,
        PlanetCrustContactInteraction[] contacts,
        double totalReferenceMassKilograms)
    {
        World = world;
        Epoch = epoch;
        DurationMillionYears = durationMillionYears;
        Columns = Array.AsReadOnly(columns);
        Contacts = Array.AsReadOnly(contacts);
        TotalReferenceMassKilograms = totalReferenceMassKilograms;
    }

    public PlanetWorldIdentity World { get; }
    public PlanetGeologicalEpoch Epoch { get; }
    public double DurationMillionYears { get; }
    public IReadOnlyList<PlanetCrustColumnStrain> Columns { get; }
    public IReadOnlyList<PlanetCrustContactInteraction> Contacts { get; }
    public double TotalReferenceMassKilograms { get; }
}

/// <summary>
/// Bounded material-column strain from the EXISTING plate-boundary kinematics
/// and the persistent neighbor graph, without rendering geological features.
///
/// This is a local first-order kinematic experiment, NOT a global conservation
/// of surface coverage, a force-balance solver, or forward geological evolution.
/// It does not mutate history, plate ownership, material identities or terrain.
/// </summary>
public sealed class PlanetCrustContactStrainSolver
{
    // Prevent a research step from collapsing/expanding an entire material
    // reference footprint. These are stability limits, not calibrated rheology.
    public const double MaximumAbsoluteLogAreaStrain = 0.12;
    public const double MaximumStepMillionYears = 1.0;

    private readonly PlanetTectonicBoundaryKinematics boundaries = new();

    public PlanetCrustInteractionSnapshot Evaluate(
        PlanetCrustParcelCatalog catalog,
        PlanetCrustOwnershipHistory history,
        PlanetCrustMaterialNeighborhood neighborhood,
        PlanetGeologicalEpoch epoch,
        double durationMillionYears)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(neighborhood);
        if (catalog.World != history.World || catalog.World != neighborhood.World)
        {
            throw new ArgumentException("Material catalogue, ownership history and neighborhood must share one generated world.");
        }

        if (!double.IsFinite(durationMillionYears) || durationMillionYears < 0.0 ||
            durationMillionYears > MaximumStepMillionYears)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMillionYears));
        }

        var states = catalog.Parcels.Select(material => history.GetState(material.Id, epoch)).ToArray();
        var logArea = new double[states.Length];
        var shearExposure = new double[states.Length];
        var contacts = new List<PlanetCrustContactInteraction>();

        foreach (var edge in neighborhood.Edges)
        {
            var aIndex = checked((int)edge.First.Value - 1);
            var bIndex = checked((int)edge.Second.Value - 1);
            var first = states[aIndex];
            var second = states[bIndex];
            if (first.OwningPlate == second.OwningPlate)
            {
                continue;
            }

            var separationDot = Math.Clamp(PlanetVector.Dot(first.GeographicDirection, second.GeographicDirection), -1.0, 1.0);
            var separationRadians = Math.Acos(separationDot);
            // Frozen reference neighbors cease interacting when their kinematic
            // motion pulls them apart. Re-bonding requires dynamic topology.
            if (separationRadians <= 1e-9 ||
                separationRadians > Math.Max(0.30, edge.ReferenceAngularDistanceRadians * 2.5))
            {
                continue;
            }

            var midpoint = first.GeographicDirection + second.GeographicDirection;
            if (midpoint.Length <= 1e-9)
            {
                continue;
            }

            var sample = boundaries.Sample(midpoint, catalog.World.Seed, epoch);
            if (!sample.IsAtContact || sample.Kind == PlanetPlateBoundaryKind.Neutral ||
                !Matches(sample, first.OwningPlate, second.OwningPlate))
            {
                continue;
            }

            var separationMeters = Math.Max(10_000.0,
                separationRadians * PlanetTectonicPlateKinematics.NominalPlanetRadiusMeters);
            var fraction = -sample.CompressionMetersPerMillionYears * durationMillionYears *
                sample.ContactInfluence / separationMeters;
            // Normalize contacts against the neighborhood degree so a parcel
            // cannot acquire strain merely from a larger graph sampling budget.
            var degree = Math.Max(neighborhood.GetNeighbors(edge.First).Count, neighborhood.GetNeighbors(edge.Second).Count);
            var contribution = Math.Clamp(fraction / degree, -0.04, 0.04);
            logArea[aIndex] += contribution;
            logArea[bIndex] += contribution;
            var shear = Math.Abs(sample.ShearMetersPerMillionYears) * durationMillionYears *
                sample.ContactInfluence / degree;
            shearExposure[aIndex] += shear;
            shearExposure[bIndex] += shear;
            contacts.Add(new PlanetCrustContactInteraction(edge.First, edge.Second, sample.Kind,
                sample.ContactInfluence, sample.CompressionMetersPerMillionYears,
                sample.ShearMetersPerMillionYears, contribution));
        }

        var columns = new PlanetCrustColumnStrain[states.Length];
        for (var i = 0; i < states.Length; i++)
        {
            var material = states[i].Material;
            var strain = Math.Clamp(logArea[i], -MaximumAbsoluteLogAreaStrain, MaximumAbsoluteLogAreaStrain);
            var area = material.ReferenceAreaSquareMeters * Math.Exp(strain);
            var thickness = material.ReferenceMassKilograms /
                (area * material.ReferenceDensityKilogramsPerCubicMeter);
            columns[i] = new PlanetCrustColumnStrain(states[i], area, thickness,
                material.ReferenceMassKilograms, strain, shearExposure[i]);
        }

        return new PlanetCrustInteractionSnapshot(catalog.World, epoch, durationMillionYears,
            columns, contacts.ToArray(), catalog.TotalReferenceMassKilograms);
    }

    private static bool Matches(
        PlanetPlateBoundarySample sample,
        PlanetTectonicPlateId first,
        PlanetTectonicPlateId second) =>
        (sample.FirstPlate == first && sample.SecondPlate == second) ||
        (sample.FirstPlate == second && sample.SecondPlate == first);
}
