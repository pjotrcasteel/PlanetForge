using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// An explicitly ordered transfer of existing reference material mass along
/// a previously established persistent neighbor edge. The source parcel is
/// also the immutable provenance of the transferred material and its density.
/// </summary>
public readonly record struct PlanetCrustMaterialExchange(
    PlanetCrustParcelId From,
    PlanetCrustParcelId To,
    double MassKilograms);

/// <summary>
/// A column after an immutable single-step exchange. Exported material is no
/// longer present locally, imported material keeps its SOURCE density.
/// Column thickness measures lithosphere, NOT terrain surface elevation.
/// </summary>
public readonly record struct PlanetCrustExchangedColumn(
    PlanetCrustMaterialState State,
    double AreaSquareMeters,
    double RetainedLocalMassKilograms,
    double ImportedMassKilograms,
    double ExportedMassKilograms,
    double TotalMassKilograms,
    double ThicknessMeters,
    double LogAreaStrain);

public sealed class PlanetCrustDeformationSnapshot
{
    internal PlanetCrustDeformationSnapshot(
        PlanetWorldIdentity world,
        PlanetGeologicalEpoch epoch,
        double durationMillionYears,
        PlanetCrustExchangedColumn[] columns,
        PlanetCrustMaterialExchange[] exchanges,
        double referenceMassKilograms)
    {
        World = world;
        Epoch = epoch;
        DurationMillionYears = durationMillionYears;
        Columns = Array.AsReadOnly(columns);
        Exchanges = Array.AsReadOnly(exchanges);
        ReferenceMassKilograms = referenceMassKilograms;
    }

    public PlanetWorldIdentity World { get; }
    public PlanetGeologicalEpoch Epoch { get; }
    public double DurationMillionYears { get; }
    public IReadOnlyList<PlanetCrustExchangedColumn> Columns { get; }
    public IReadOnlyList<PlanetCrustMaterialExchange> Exchanges { get; }
    public double ReferenceMassKilograms { get; }
    public double TotalFinalMassKilograms => Columns.Sum(column => column.TotalMassKilograms);
}

/// <summary>
/// A first conservative, coupled MATERIAL LEDGER driven by precomputed contact
/// strain (A2.5.5). Negative log-area strain creates excess column thickness.
/// Bounded mass redistributes from more-compressed to less-compressed neighboring
/// material samples. Every exchange is one-way, explicit and counted at both
/// ends; no implicit material generation, deletion or conversion occurs.
///
/// Important: a parcel graph is NOT a watertight finite-volume tessellation;
/// these masses conserve within the sampled ledger, but do not prove
/// conservation of a continuous crust mantle or global area coverage.
/// </summary>
public sealed class PlanetCrustMassExchangeSolver
{
    public const double MaximumOutgoingMassFractionPerStep = 0.01;
    public const double TransportFractionPerUnitLogStrain = 0.10;

    public PlanetCrustDeformationSnapshot Exchange(
        PlanetCrustParcelCatalog catalog,
        PlanetCrustMaterialNeighborhood neighborhood,
        PlanetCrustInteractionSnapshot interaction)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(neighborhood);
        ArgumentNullException.ThrowIfNull(interaction);
        if (catalog.World != neighborhood.World || catalog.World != interaction.World ||
            interaction.Columns.Count != catalog.Parcels.Count)
        {
            throw new ArgumentException("All inputs must describe the same reference material catalogue and generated world.");
        }

        var count = catalog.Parcels.Count;
        var outgoingRequested = new double[count];
        var requests = new List<ExchangeRequest>();
        if (interaction.DurationMillionYears > 0.0)
        {
            foreach (var edge in neighborhood.Edges)
            {
                var first = checked((int)edge.First.Value - 1);
                var second = checked((int)edge.Second.Value - 1);
                if (first < 0 || second < 0 || first >= count || second >= count ||
                    !IsCurrentNeighbor(edge, interaction.Columns[first].State, interaction.Columns[second].State))
                {
                    continue;
                }

                var left = interaction.Columns[first];
                var right = interaction.Columns[second];
                var difference = right.LogAreaStrain - left.LogAreaStrain;
                var donor = difference > 0.0 ? first : second;
                var recipient = difference > 0.0 ? second : first;
                var requested = ComputeRequestedMass(Math.Abs(difference), interaction.Columns[donor].MassKilograms);
                if (requested <= 0.0)
                {
                    continue;
                }

                requests.Add(new ExchangeRequest(donor, recipient, requested));
                outgoingRequested[donor] += requested;
            }
        }

        // All donor scaling uses the same pre-exchange state; the result cannot
        // depend on edge evaluation order and can never overdraw a parcel.
        var outgoingScale = new double[count];
        for (var i = 0; i < count; i++)
        {
            var budget = interaction.Columns[i].MassKilograms * MaximumOutgoingMassFractionPerStep;
            outgoingScale[i] = outgoingRequested[i] > budget ? budget / outgoingRequested[i] : 1.0;
        }

        var outgoing = new double[count];
        var incoming = new double[count];
        var incomingSolidVolume = new double[count];
        var exchanges = new PlanetCrustMaterialExchange[requests.Count];
        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            var kilograms = request.MassKilograms * outgoingScale[request.FromIndex];
            outgoing[request.FromIndex] += kilograms;
            incoming[request.ToIndex] += kilograms;
            var sourceDensity = catalog.Parcels[request.FromIndex].ReferenceDensityKilogramsPerCubicMeter;
            incomingSolidVolume[request.ToIndex] += kilograms / sourceDensity;
            exchanges[i] = new PlanetCrustMaterialExchange(
                catalog.Parcels[request.FromIndex].Id, catalog.Parcels[request.ToIndex].Id, kilograms);
        }

        var columns = new PlanetCrustExchangedColumn[count];
        for (var i = 0; i < count; i++)
        {
            var original = interaction.Columns[i];
            var material = catalog.Parcels[i];
            if (original.State.Material.Id != material.Id || original.State.Material.World != catalog.World ||
                original.State.Material.ReferenceMassKilograms != material.ReferenceMassKilograms ||
                !double.IsFinite(original.MassKilograms) || original.MassKilograms != material.ReferenceMassKilograms ||
                !double.IsFinite(original.AreaSquareMeters) || original.AreaSquareMeters <= 0.0 ||
                !double.IsFinite(original.LogAreaStrain))
            {
                throw new ArgumentException("The strain snapshot must preserve the exact canonical material ledger.", nameof(interaction));
            }

            var remaining = original.MassKilograms - outgoing[i];
            var currentMass = remaining + incoming[i];
            var solidVolume = remaining / material.ReferenceDensityKilogramsPerCubicMeter + incomingSolidVolume[i];
            columns[i] = new PlanetCrustExchangedColumn(original.State, original.AreaSquareMeters, remaining,
                incoming[i], outgoing[i], currentMass, solidVolume / original.AreaSquareMeters, original.LogAreaStrain);
        }

        return new PlanetCrustDeformationSnapshot(catalog.World, interaction.Epoch, interaction.DurationMillionYears,
            columns, exchanges, catalog.TotalReferenceMassKilograms);
    }

    /// <summary>
    /// Signed strain DIFFERENCE controls a request, while a separate
    /// global per-donor cap limits combined outgoing flux over all neighbors.
    /// This is a stable transport assumption, not a measured rheology law.
    /// </summary>
    public static double ComputeRequestedMass(double logStrainDifference, double sourceMassKilograms)
    {
        if (!double.IsFinite(logStrainDifference) || logStrainDifference < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(logStrainDifference));
        }

        if (!double.IsFinite(sourceMassKilograms) || sourceMassKilograms <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceMassKilograms));
        }

        return sourceMassKilograms * Math.Min(
            MaximumOutgoingMassFractionPerStep,
            logStrainDifference * TransportFractionPerUnitLogStrain);
    }

    private static bool IsCurrentNeighbor(
        PlanetCrustNeighborEdge edge,
        PlanetCrustMaterialState first,
        PlanetCrustMaterialState second)
    {
        var separation = Math.Acos(Math.Clamp(
            PlanetVector.Dot(first.GeographicDirection, second.GeographicDirection), -1.0, 1.0));
        return separation > 1e-9 && separation <= Math.Max(0.30, edge.ReferenceAngularDistanceRadians * 2.5);
    }

    private readonly record struct ExchangeRequest(int FromIndex, int ToIndex, double MassKilograms);
}
