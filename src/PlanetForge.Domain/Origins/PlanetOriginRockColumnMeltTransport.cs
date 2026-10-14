using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Origins;

/// <summary>
/// A physically supplied, integrated face transaction. The process producing this transaction
/// owns momentum, mechanical work and timestep admission; this API never invents a velocity.
/// </summary>
public readonly record struct PlanetOriginMeltFaceTransfer(PlanetSurfaceGridCellId Donor, int DonorLayer,
    PlanetSurfaceGridCellId Recipient, int RecipientLayer, double MassKilograms);

public sealed record PlanetOriginRockColumnMeltTrial(PlanetOriginRockColumnField Candidate, double TransportedMassKilograms,
    double TransportedEnthalpyJoules, double MassResidualKilograms, double EnthalpyResidualJoules);

public sealed partial class PlanetOriginRockColumnField
{
    /// <summary>
    /// Donor-cell upwind transport of owned, fully molten material and its specific enthalpy.
    /// Solid/crystal mass cannot be moved by treating a mixed layer as pure liquid.
    /// All withdrawals use the original donor state; incoming material cannot hide an overdraw.
    /// </summary>
    public PlanetOriginRockColumnMeltTrial ProposeMeltTransfers(IReadOnlyList<PlanetOriginMeltFaceTransfer> transfers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        cancellationToken.ThrowIfCancellationRequested();
        if (transfers.Count == 0) return new(this, 0.0, 0.0, 0.0, 0.0);
        var withdrawn = Columns.Select(column => new double[column.Layers.Count]).ToArray();
        var massDelta = Columns.Select(column => new double[column.Layers.Count]).ToArray();
        var energyDelta = Columns.Select(column => new double[column.Layers.Count]).ToArray();
        var bounds = Columns.Select(column => LayerBoundaries(column)).ToArray();
        var transportedMass = 0.0;
        var transportedEnergy = 0.0;
        foreach (var transfer in transfers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!double.IsFinite(transfer.MassKilograms) || transfer.MassKilograms <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(transfers), "Face material mass must be positive and finite.");
            if (transfer.Donor.Level != Aggregate.Layout.Level || transfer.Recipient.Level != Aggregate.Layout.Level ||
                !ColumnSides.Any(side => PlanetSurfaceGridTopology.GetNeighbor(transfer.Donor, side).Cell == transfer.Recipient))
                throw new ArgumentException("Melt can cross only an actual shared spherical face at this grid level.", nameof(transfers));
            var donorIndex = Aggregate.Layout.GetIndex(transfer.Donor);
            var recipientIndex = Aggregate.Layout.GetIndex(transfer.Recipient);
            if (transfer.DonorLayer < 0 || transfer.DonorLayer >= Properties.LayerCount ||
                transfer.RecipientLayer < 0 || transfer.RecipientLayer >= Properties.LayerCount)
                throw new ArgumentOutOfRangeException(nameof(transfers), "Melt transactions must address owned depth layers.");
            var top = Math.Min(bounds[donorIndex][transfer.DonorLayer], bounds[recipientIndex][transfer.RecipientLayer]);
            var bottom = Math.Max(bounds[donorIndex][transfer.DonorLayer + 1], bounds[recipientIndex][transfer.RecipientLayer + 1]);
            if (top <= bottom) throw new InvalidOperationException("Melt transactions require physically overlapping depth intervals.");
            var recipient = Columns[recipientIndex].Layers[transfer.RecipientLayer];
            if (recipient.Resolve(Material).MeltFraction < 1.0)
                throw new InvalidOperationException("Melt cannot be injected into solid or unresolved mush without an interface/permeability model.");
            var donor = Columns[donorIndex].Layers[transfer.DonorLayer];
            if (donor.Resolve(Material).MeltFraction < 1.0)
                throw new InvalidOperationException("Only fully liquid donor material can use this single-phase melt transaction.");
            withdrawn[donorIndex][transfer.DonorLayer] += transfer.MassKilograms;
            // A separate physical flow solver must subdivide; never limit or create material silently.
            if (!double.IsFinite(withdrawn[donorIndex][transfer.DonorLayer]) || withdrawn[donorIndex][transfer.DonorLayer] > donor.MassKilograms * 0.25)
                throw new InvalidOperationException("A melt step cannot withdraw more than one quarter of an original donor layer; subdivide the physical interval.");
            var enthalpy = transfer.MassKilograms * (donor.StoredEnthalpyJoules / donor.MassKilograms);
            if (!double.IsFinite(enthalpy)) throw new InvalidOperationException("Transported melt enthalpy must remain finite.");
            massDelta[donorIndex][transfer.DonorLayer] -= transfer.MassKilograms;
            massDelta[recipientIndex][transfer.RecipientLayer] += transfer.MassKilograms;
            energyDelta[donorIndex][transfer.DonorLayer] -= enthalpy;
            energyDelta[recipientIndex][transfer.RecipientLayer] += enthalpy;
            transportedMass += transfer.MassKilograms;
            transportedEnergy += enthalpy;
        }
        var result = new PlanetOriginRockColumn[Columns.Count];
        for (var index = 0; index < result.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var column = Columns[index];
            var layers = new PlanetOriginRockColumnLayer[Properties.LayerCount];
            for (var depth = 0; depth < layers.Length; depth++)
            {
                var old = column.Layers[depth];
                var mass = old.MassKilograms + massDelta[index][depth];
                var next = old.AddEnthalpy(energyDelta[index][depth]) with { MassKilograms = mass };
                if (!double.IsFinite(mass) || mass <= 0.0 || !double.IsFinite(next.StoredEnthalpyJoules) || next.StoredEnthalpyJoules < 0.0)
                    throw new InvalidOperationException("Melt transport produced an invalid owned material layer.");
                _ = next.Resolve(Material);
                layers[depth] = next;
            }
            result[index] = new(column.Id, column.AreaSquareMeters, column.ReferenceThicknessMeters, layers, column.ReferenceMassKilograms);
            if (result[index].ThicknessMeters(Material, Properties) / Aggregate.PlanetRadiusMeters > 0.001)
                throw new InvalidOperationException("Melt redistribution exceeded the validated thin-shell geometry envelope.");
        }
        var aggregate = PlanetOriginSurfaceRockPhaseField.FromOwnedColumns(Aggregate, result);
        var massResidual = Aggregate.TotalActiveRockMassKilograms - aggregate.TotalActiveRockMassKilograms;
        var energyResidual = Aggregate.TotalStoredEnthalpyJoules - aggregate.TotalStoredEnthalpyJoules;
        if (!double.IsFinite(transportedMass) || !double.IsFinite(transportedEnergy) ||
            Math.Abs(massResidual) > Aggregate.TotalActiveRockMassKilograms * 2e-12 ||
            Math.Abs(energyResidual) > Math.Max(1e-6, Aggregate.TotalStoredEnthalpyJoules * 2e-12))
            throw new InvalidOperationException("Melt face transactions did not conserve the owned mass and enthalpy.");
        cancellationToken.ThrowIfCancellationRequested();
        return new(new(aggregate, Properties, result), transportedMass, transportedEnergy, massResidual, energyResidual);
    }
}
