using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Signed material transfer on ONE canonical spherical control-volume face.
/// Positive kilograms move from First to Second; negative kilograms reverse it.
/// No material may be transferred across two cells lacking a shared face.
/// </summary>
public readonly record struct PlanetCrustSphericalFaceFlux(
    PlanetCrustParcelId First,
    PlanetCrustParcelId Second,
    double SignedKilograms);

public readonly record struct PlanetCrustFaceBalance(
    PlanetCrustParcelId MaterialId,
    double StartingKilograms,
    double IncomingKilograms,
    double OutgoingKilograms,
    double EndingKilograms);

public sealed class PlanetCrustFaceFluxSnapshot
{
    internal PlanetCrustFaceFluxSnapshot(
        PlanetWorldIdentity world,
        PlanetGeologicalEpoch epoch,
        PlanetCrustFaceBalance[] balances,
        PlanetCrustSphericalFaceFlux[] transfers)
    {
        World = world;
        Epoch = epoch;
        Balances = Array.AsReadOnly(balances);
        Transfers = Array.AsReadOnly(transfers);
    }

    public PlanetWorldIdentity World { get; }
    public PlanetGeologicalEpoch Epoch { get; }
    public IReadOnlyList<PlanetCrustFaceBalance> Balances { get; }
    public IReadOnlyList<PlanetCrustSphericalFaceFlux> Transfers { get; }
    public double InitialMassKilograms => Balances.Sum(value => value.StartingKilograms);
    public double FinalMassKilograms => Balances.Sum(value => value.EndingKilograms);
}

/// <summary>
/// Conservative flux bookkeeping on CLOSED epoch-specific spherical
/// Voronoi control volumes. Every signed face transfer has one debit and
/// one credit. Flux budgets are checked across ALL faces of a donor before
/// publication; no order-dependent transfers can overdraw reference material.
/// This is a ledger for physically adjacent cells, not a flux/rheology law.
/// </summary>
public sealed class PlanetCrustSphericalFaceFluxLedger
{
    public const double MaximumOutgoingFraction = 0.02;

    public PlanetCrustFaceFluxSnapshot Apply(
        PlanetCrustDeformationSnapshot material,
        PlanetCrustSphericalControlVolumes geometry,
        IReadOnlyList<PlanetCrustSphericalFaceFlux> fluxes)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(fluxes);
        if (material.World != geometry.World || material.Epoch != geometry.Epoch ||
            material.Columns.Count != geometry.Cells.Count)
        {
            throw new ArgumentException("Face geometry and material state must refer to the same generated world.");
        }

        var faces = geometry.Faces.ToDictionary(face => Key(face.First, face.Second));
        var seen = new HashSet<ulong>();
        var incoming = new double[material.Columns.Count];
        var outgoing = new double[material.Columns.Count];

        foreach (var flux in fluxes)
        {
            var key = Key(flux.First, flux.Second);
            if (flux.First.Value == 0 || flux.First.Value >= flux.Second.Value ||
                !faces.ContainsKey(key) || !seen.Add(key) ||
                !double.IsFinite(flux.SignedKilograms))
            {
                throw new ArgumentException("Flux must reference a unique existing canonical face with finite mass.", nameof(fluxes));
            }

            var sourceIndex = checked((int)(flux.SignedKilograms >= 0.0 ? flux.First.Value : flux.Second.Value) - 1);
            var destinationIndex = checked((int)(flux.SignedKilograms >= 0.0 ? flux.Second.Value : flux.First.Value) - 1);
            var kilograms = Math.Abs(flux.SignedKilograms);
            outgoing[sourceIndex] += kilograms;
            incoming[destinationIndex] += kilograms;
        }

        var balances = new PlanetCrustFaceBalance[material.Columns.Count];
        for (var i = 0; i < balances.Length; i++)
        {
            var column = material.Columns[i];
            if (column.State.Material.Id != geometry.Cells[i].MaterialId ||
                column.State.Material.World != geometry.World ||
                column.State.Epoch != material.Epoch ||
                !double.IsFinite(column.TotalMassKilograms) || column.TotalMassKilograms <= 0.0 ||
                !double.IsFinite(incoming[i]) || !double.IsFinite(outgoing[i]) ||
                outgoing[i] > column.TotalMassKilograms * MaximumOutgoingFraction)
            {
                throw new ArgumentException("Flux exceeds donor budget or contradicts the material reference state.", nameof(fluxes));
            }

            balances[i] = new PlanetCrustFaceBalance(column.State.Material.Id,
                column.TotalMassKilograms, incoming[i], outgoing[i],
                column.TotalMassKilograms + incoming[i] - outgoing[i]);
        }

        // Conserved globally because each canonical face occurs exactly once,
        // regardless of read order or the local/LOD region used to inspect it.
        return new PlanetCrustFaceFluxSnapshot(material.World, material.Epoch, balances, fluxes.ToArray());
    }

    private static ulong Key(PlanetCrustParcelId first, PlanetCrustParcelId second) =>
        (first.Value << 32) | second.Value;
}
