using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Origins;

public sealed partial class PlanetOriginRockColumnField
{
    private PlanetOriginRockColumnTrial ProposeImplicit(PlanetOriginDuration interval, PlanetOriginRegionalAirlessParameters boundary,
        PlanetOriginThermalTimeline interior, CancellationToken cancellationToken)
    {
        if (!Properties.UseRadiatingInterface)
            throw new InvalidOperationException("Implicit depth transport requires the conductive/radiative face.");
        var seconds = interval.TotalSeconds;
        var result = new PlanetOriginRockColumn[Columns.Count];
        var exported = 0.0;
        var imported = 0.0;
        var absorbed = 0.0;
        var emitted = 0.0;
        var mantleIncidence = 0.0;
        for (var i = 0; i < result.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var column = Columns[i];
            var direction = PlanetSurfaceGridGeometry.GetCenterDirection(column.Id);
            var solar = boundary.SolarIrradianceWattsPerSquareMeter * (1.0 - boundary.BondAlbedo) *
                Math.Max(0.0, PlanetVector.Dot(direction, boundary.StellarDirection));
            var solution = PlanetOriginRockColumnImplicitSolver.Solve(column, Material, Properties, boundary,
                interior.Rock.TemperatureKelvin, solar, seconds, cancellationToken);
            result[i] = new(column.Id, column.AreaSquareMeters, column.ReferenceThicknessMeters, solution.Layers, column.ReferenceMassKilograms);
            exported += Math.Max(0.0, solution.InteriorJoules);
            imported += Math.Max(0.0, -solution.InteriorJoules);
            absorbed += solar * column.AreaSquareMeters * seconds;
            emitted += solution.EmittedJoules;
            var bottomWidth = column.Layers[^1].VolumeCubicMeters(Material, Properties) / column.AreaSquareMeters;
            mantleIncidence += boundary.ConductivityWattsPerMeterKelvin * column.AreaSquareMeters * seconds /
                (boundary.ConductiveBoundaryThicknessMeters + bottomWidth * 0.5);
            if (solution.ThicknessMeters / Aggregate.PlanetRadiusMeters > 0.001)
                throw new InvalidOperationException("Implicit columns exceeded the thin-shell geometry envelope.");
        }
        VerifyCoupling(mantleIncidence, interior.MinimumRockHeatCapacityJoulesPerKelvin);
        var aggregate = PlanetOriginSurfaceRockPhaseField.FromOwnedColumns(Aggregate, result);
        var residual = Aggregate.TotalStoredEnthalpyJoules + exported - imported + absorbed - emitted - aggregate.TotalStoredEnthalpyJoules;
        if (!double.IsFinite(residual) || Math.Abs(residual) > Math.Max(1e-6, Aggregate.TotalStoredEnthalpyJoules * 2e-12))
            throw new InvalidOperationException("Implicit depth fluxes did not close the canonical surface account.");
        return new(new(aggregate, Properties, result), exported, imported, absorbed, emitted, residual);
    }
}

internal sealed class PlanetOriginRockColumnConvergenceException(string message) : InvalidOperationException(message);

internal sealed record PlanetOriginImplicitColumnSolution(PlanetOriginRockColumnLayer[] Layers, double InteriorJoules, double EmittedJoules, double ThicknessMeters);

/// <summary>
/// Backward-Euler enthalpy solve on a frozen beginning-of-step physical mesh.
/// Internal face fluxes are shared, radiation uses the reconstructed face and the finite mantle is explicit.
/// Newton residuals and line search reject an unconverged trial; this is first order, not an accuracy guarantee.
/// </summary>
internal static class PlanetOriginRockColumnImplicitSolver
{
    internal static PlanetOriginImplicitColumnSolution Solve(PlanetOriginRockColumn column, PlanetOriginSurfaceRockPhaseMaterial material,
        PlanetOriginRockColumnProperties properties, PlanetOriginRegionalAirlessParameters boundary,
        double mantleKelvin, double solar, double seconds, CancellationToken cancellationToken)
    {
        var n = column.Layers.Count;
        // One immutable specific-enthalpy model, scaled by actual layer mass.
        // Avoid constructing several identical phase models per layer/substep in WASM.
        var model = new PlanetRockEnthalpyModel(material.ToRockProperties(1.0));
        var width = new double[n];
        var mass = new double[n];
        var oldEnergy = new double[n];
        var temperature = new double[n];
        for (var j = 0; j < n; j++)
        {
            var layer = column.Layers[j];
            var phase = model.Resolve(layer.StoredEnthalpyJoules / layer.MassKilograms);
            width[j] = layer.MassKilograms * ((1.0 - phase.MeltFraction) / properties.SolidDensityKilogramsPerCubicMeter +
                phase.MeltFraction / properties.LiquidDensityKilogramsPerCubicMeter) / column.AreaSquareMeters;
            mass[j] = layer.MassKilograms / column.AreaSquareMeters;
            oldEnergy[j] = layer.StoredEnthalpyJoules / column.AreaSquareMeters;
            temperature[j] = phase.TemperatureKelvin;
        }
        // Solve temperature departures from a shared reference. In a stiff liquid
        // layer, subtracting two absolute ~2200 K values loses the tiny gradient
        // that carries a finite heat flux through a very large conductance.
        var referenceKelvin = temperature[^1];
        for (var j = 0; j < n; j++) temperature[j] -= referenceKelvin;
        var conductance = new double[n - 1];
        if (boundary.LiquidHeatTransport is not null)
            conductance = PlanetOriginLiquidHeatTransport.Resolve(column, material, properties, boundary.LiquidHeatTransport).Conductance;
        else
            for (var j = 0; j < conductance.Length; j++)
                conductance[j] = properties.ConductivityWattsPerMeterKelvin / ((width[j] + width[j + 1]) * 0.5);
        var bottom = boundary.ConductivityWattsPerMeterKelvin / (boundary.ConductiveBoundaryThicknessMeters + width[^1] * 0.5);
        var faceConductance = properties.ConductivityWattsPerMeterKelvin / (width[0] * 0.5);
        var radiation = boundary.InfraredEmissivity * PlanetRadiationBalanceModel.StefanBoltzmannWattsPerSquareMeterKelvinFourth;
        var residual = new double[n];
        var diagonal = new double[n];
        var lower = new double[n];
        var upper = new double[n];
        var rhs = new double[n];
        var trial = new double[n];
        var converged = false;
        double Face(double t) => PlanetOriginRockRadiatingBoundary.ResolveTemperature(t + referenceKelvin,
            properties.ConductivityWattsPerMeterKelvin, width[0] * 0.5, solar, boundary.InfraredEmissivity);

        double Evaluate(double[] values, bool jacobian)
        {
            var face = Face(values[0]);
            var norm = 0.0;
            for (var j = 0; j < n; j++)
            {
                var inflow = j == 0 ? solar - radiation * Math.Pow(face, 4) : conductance[j - 1] * (values[j - 1] - values[j]);
                inflow += j == n - 1 ? bottom * (mantleKelvin - referenceKelvin - values[j]) : conductance[j] * (values[j + 1] - values[j]);
                residual[j] = model.CalculateStoredEnergyJoules(values[j] + referenceKelvin) * mass[j] - oldEnergy[j] - seconds * inflow;
                norm = Math.Max(norm, Math.Abs(residual[j]) / (mass[j] * material.SpecificHeatCapacityJoulesPerKilogramKelvin));
                if (!jacobian) continue;
                var cp = material.SpecificHeatCapacityJoulesPerKilogramKelvin;
                if (values[j] + referenceKelvin > material.SolidusTemperatureKelvin && values[j] + referenceKelvin < material.LiquidusTemperatureKelvin)
                    cp += material.LatentHeatOfFusionJoulesPerKilogram / (material.LiquidusTemperatureKelvin - material.SolidusTemperatureKelvin);
                var radSlope = 4.0 * radiation * Math.Pow(face, 3);
                var faceSlope = faceConductance == 0.0 || radSlope == 0.0 ? 0.0 : faceConductance * radSlope / (faceConductance + radSlope);
                diagonal[j] = mass[j] * cp + seconds * ((j == 0 ? faceSlope : conductance[j - 1]) + (j == n - 1 ? bottom : conductance[j]));
                lower[j] = j == 0 ? 0.0 : -seconds * conductance[j - 1];
                upper[j] = j == n - 1 ? 0.0 : -seconds * conductance[j];
                rhs[j] = -residual[j];
            }
            if (!double.IsFinite(norm)) throw new InvalidOperationException("Implicit thermal residual is not finite.");
            return norm;
        }

        for (var iteration = 0; iteration < 80; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var norm = Evaluate(temperature, true);
            if (norm < 1e-7) { converged = true; break; }
            for (var j = 1; j < n; j++)
            {
                var factor = lower[j] / diagonal[j - 1];
                diagonal[j] -= factor * upper[j - 1];
                rhs[j] -= factor * rhs[j - 1];
            }
            rhs[^1] /= diagonal[^1];
            for (var j = n - 2; j >= 0; j--) rhs[j] = (rhs[j] - upper[j] * rhs[j + 1]) / diagonal[j];
            var accepted = false;
            for (var alpha = 1.0; alpha >= 1.0 / 1_048_576.0; alpha *= 0.5)
            {
                for (var j = 0; j < n; j++) trial[j] = temperature[j] + alpha * rhs[j];
                if (trial.Any(value => !double.IsFinite(value) || value + referenceKelvin < 0.0)) continue;
                if (Evaluate(trial, false) >= norm) continue;
                Array.Copy(trial, temperature, n);
                accepted = true;
                break;
            }
            if (!accepted) throw new PlanetOriginRockColumnConvergenceException("Implicit column Newton line search failed; nothing was published.");
        }
        if (!converged) throw new PlanetOriginRockColumnConvergenceException("Implicit column enthalpy solve did not converge; nothing was published.");
        var delta = new double[n];
        for (var j = 0; j < n - 1; j++)
        {
            var heat = seconds * conductance[j] * (temperature[j] - temperature[j + 1]);
            delta[j] -= heat;
            delta[j + 1] += heat;
        }
        var interior = seconds * bottom * (mantleKelvin - referenceKelvin - temperature[^1]);
        var emitted = seconds * radiation * Math.Pow(Face(temperature[0]), 4);
        delta[^1] += interior;
        delta[0] += seconds * solar - emitted;
        var layers = new PlanetOriginRockColumnLayer[n];
        var volume = 0.0;
        for (var j = 0; j < n; j++)
        {
            layers[j] = column.Layers[j].AddEnthalpy(delta[j] * column.AreaSquareMeters);
            var resolved = model.Resolve(layers[j].StoredEnthalpyJoules / layers[j].MassKilograms);
            volume += layers[j].MassKilograms * ((1.0 - resolved.MeltFraction) / properties.SolidDensityKilogramsPerCubicMeter +
                resolved.MeltFraction / properties.LiquidDensityKilogramsPerCubicMeter);
            if (Math.Abs(resolved.TemperatureKelvin - (temperature[j] + referenceKelvin)) > 1e-6)
                throw new InvalidOperationException("Implicit flux update differs from the converged enthalpy solution.");
        }
        return new(layers, interior * column.AreaSquareMeters, emitted * column.AreaSquareMeters, volume / column.AreaSquareMeters);
    }
}
