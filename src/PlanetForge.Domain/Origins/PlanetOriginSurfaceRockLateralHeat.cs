using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Origins;

/// <summary>
/// An uncommitted, internal energy redistribution. Transported joules are
/// diagnostic only: each edge debits exactly the joules credited to its peer.
/// </summary>
public sealed record PlanetOriginLateralHeatTrial(
    PlanetOriginSurfaceRockPhaseField Candidate,
    double TransportedJoules,
    double ConservationResidualJoules);

public sealed partial class PlanetOriginSurfaceRockPhaseField
{
    private static readonly PlanetGridDirection[] LateralDirections =
    [
        PlanetGridDirection.North,
        PlanetGridDirection.East,
        PlanetGridDirection.South,
        PlanetGridDirection.West
    ];

    /// <summary>
    /// Proposes finite-volume conductive transport between spherical neighbors.
    /// Each undirected shared edge is processed once, including cube-face seams.
    /// Conductivity is in W/(m K), thickness and planet radius in metres.
    /// This is lateral shell conduction, not unresolved mantle convection.
    /// </summary>
    public PlanetOriginLateralHeatTrial ProposeLateralHeat(
        PlanetOriginDuration duration,
        double conductivityWattsPerMeterKelvin,
        double activeLayerThicknessMeters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var seconds = duration.TotalSeconds;
        if (!double.IsFinite(seconds) || seconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(duration));
        if (!double.IsFinite(conductivityWattsPerMeterKelvin) || conductivityWattsPerMeterKelvin < 0.0)
            throw new ArgumentOutOfRangeException(nameof(conductivityWattsPerMeterKelvin));
        if (!double.IsFinite(activeLayerThicknessMeters) || activeLayerThicknessMeters <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(activeLayerThicknessMeters));
        if (conductivityWattsPerMeterKelvin == 0.0)
            return new PlanetOriginLateralHeatTrial(this, 0.0, 0.0);

        var count = cells.Count;
        var temperatures = new double[count];
        var capacities = new double[count];
        var directions = new PlanetVector[count];
        var deltas = new double[count];
        var conductanceSums = new double[count];

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cell = cells[index];
            temperatures[index] = cell.Resolve(Material).TemperatureKelvin;
            capacities[index] = cell.ActiveRockMassKilograms * Material.SpecificHeatCapacityJoulesPerKilogramKelvin;
            directions[index] = PlanetSurfaceGridGeometry.GetCenterDirection(cell.Id);
            if (!double.IsFinite(capacities[index]) || capacities[index] <= 0.0)
                throw new InvalidOperationException("Lateral transport requires a finite, positive rock heat capacity.");
        }

        var transported = 0.0;
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cell = cells[index];
            foreach (var side in LateralDirections)
            {
                var neighbor = PlanetSurfaceGridTopology.GetNeighbor(cell.Id, side);
                var otherIndex = Layout.GetIndex(neighbor.Cell);
                if (otherIndex <= index) continue;

                var distance = PlanetRadiusMeters * Math.Acos(Math.Clamp(
                    PlanetVector.Dot(directions[index], directions[otherIndex]), -1.0, 1.0));
                var edgeLength = SharedSphericalEdgeLengthMeters(cell.Id, side);
                var conductance = conductivityWattsPerMeterKelvin * activeLayerThicknessMeters * edgeLength / distance;
                var joules = conductance * (temperatures[index] - temperatures[otherIndex]) * seconds;
                if (!double.IsFinite(conductance) || conductance <= 0.0 || !double.IsFinite(joules))
                    throw new InvalidOperationException("Spherical lateral heat transport has a non-finite or invalid edge flux.");

                conductanceSums[index] += conductance;
                conductanceSums[otherIndex] += conductance;
                deltas[index] -= joules;
                deltas[otherIndex] += joules;
                transported += Math.Abs(joules);
            }
        }

        // A conservative explicit heat step must not outrun sensible thermal
        // response, even inside the higher-capacity partially molten range.
        // The adaptive caller must subdivide the interval; never clip fluxes.
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var coupling = conductanceSums[index] * seconds / capacities[index];
            if (!double.IsFinite(coupling) || coupling > 0.5)
                throw new InvalidOperationException("Lateral heat step exceeds its explicit stability bound; subdivide the physical duration.");
        }

        var next = new PlanetOriginSurfaceRockPhaseCell[count];
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var energy = cells[index].StoredEnthalpyJoules + deltas[index];
            if (!double.IsFinite(energy) || energy < 0.0)
                throw new InvalidOperationException("Lateral heat would overdraw a material cell.");
            next[index] = cells[index] with { StoredEnthalpyJoules = energy };
            _ = next[index].Resolve(Material);
        }

        var candidate = new PlanetOriginSurfaceRockPhaseField(Material, Layout, PlanetRadiusMeters, next);
        var residual = TotalStoredEnthalpyJoules - candidate.TotalStoredEnthalpyJoules;
        if (!double.IsFinite(transported) || !double.IsFinite(residual) ||
            Math.Abs(residual) > Math.Max(1e-6, TotalStoredEnthalpyJoules * 1e-12))
            throw new InvalidOperationException("Lateral heat transport did not close its internal energy ledger.");
        return new PlanetOriginLateralHeatTrial(candidate, transported, residual);
    }

    internal double SharedSphericalEdgeLengthMeters(PlanetSurfaceGridCellId cell, PlanetGridDirection side)
    {
        var n = cell.CellsPerAxis;
        var u0 = -1.0 + 2.0 * cell.X / n;
        var v0 = -1.0 + 2.0 * cell.Y / n;
        var u1 = -1.0 + 2.0 * (cell.X + 1) / n;
        var v1 = -1.0 + 2.0 * (cell.Y + 1) / n;
        var endpoints = side switch
        {
            PlanetGridDirection.North => (CubedSphereProjection.ToUnitSphere(cell.Face, u0, v1),
                CubedSphereProjection.ToUnitSphere(cell.Face, u1, v1)),
            PlanetGridDirection.East => (CubedSphereProjection.ToUnitSphere(cell.Face, u1, v0),
                CubedSphereProjection.ToUnitSphere(cell.Face, u1, v1)),
            PlanetGridDirection.South => (CubedSphereProjection.ToUnitSphere(cell.Face, u0, v0),
                CubedSphereProjection.ToUnitSphere(cell.Face, u1, v0)),
            PlanetGridDirection.West => (CubedSphereProjection.ToUnitSphere(cell.Face, u0, v0),
                CubedSphereProjection.ToUnitSphere(cell.Face, u0, v1)),
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Invalid spherical edge.")
        };
        var arcRadians = Math.Atan2(PlanetVector.Cross(endpoints.Item1, endpoints.Item2).Length,
            PlanetVector.Dot(endpoints.Item1, endpoints.Item2));
        return arcRadians * PlanetRadiusMeters;
    }
}
