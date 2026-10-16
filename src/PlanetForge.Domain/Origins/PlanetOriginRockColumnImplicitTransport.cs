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
        var workspace = new PlanetOriginImplicitColumnWorkspace(Properties.LayerCount, Material);
        for (var i = 0; i < result.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var column = Columns[i];
            var direction = PlanetSurfaceGridGeometry.GetCenterDirection(column.Id);
            var solar = boundary.SolarIrradianceWattsPerSquareMeter * (1.0 - boundary.BondAlbedo) *
                Math.Max(0.0, PlanetVector.Dot(direction, boundary.StellarDirection));
            var solution = PlanetOriginRockColumnImplicitSolver.Solve(column, Material, Properties, boundary,
                interior.Rock.TemperatureKelvin, solar, seconds, workspace, cancellationToken);
            result[i] = new(column.Id, column.AreaSquareMeters, column.ReferenceThicknessMeters, solution.Layers);
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

internal sealed record PlanetOriginImplicitColumnSolution(PlanetOriginRockColumnLayer[] Layers, double InteriorJoules, double EmittedJoules, double ThicknessMeters);

internal sealed class PlanetOriginImplicitColumnWorkspace(int layerCount, PlanetOriginSurfaceRockPhaseMaterial material)
{
    public PlanetRockEnthalpyModel Model { get; } = new(material.ToRockProperties(1.0));
    public double[] Width { get; } = new double[layerCount];
    public double[] Mass { get; } = new double[layerCount];
    public double[] OldEnergy { get; } = new double[layerCount];
    public double[] Temperature { get; } = new double[layerCount];
    public double[] Conductance { get; } = new double[layerCount - 1];
    public double[] Residual { get; } = new double[layerCount];
    public double[] Diagonal { get; } = new double[layerCount];
    public double[] Lower { get; } = new double[layerCount];
    public double[] Upper { get; } = new double[layerCount];
    public double[] RightHandSide { get; } = new double[layerCount];
    public double[] Trial { get; } = new double[layerCount];
    public double[] Delta { get; } = new double[layerCount];
}

/// <summary>
/// Backward-Euler enthalpy solve on a frozen beginning-of-step physical mesh.
/// Internal face fluxes are shared, radiation uses the reconstructed face and the finite mantle is explicit.
/// Newton residuals and line search reject an unconverged trial; this is first order, not an accuracy guarantee.
/// </summary>
internal static class PlanetOriginRockColumnImplicitSolver
{
    internal static PlanetOriginImplicitColumnSolution Solve(PlanetOriginRockColumn column, PlanetOriginSurfaceRockPhaseMaterial material,
        PlanetOriginRockColumnProperties properties, PlanetOriginRegionalAirlessParameters boundary,
        double mantleKelvin, double solar, double seconds, PlanetOriginImplicitColumnWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var n = column.Layers.Count;
        var model = workspace.Model;
        var width = workspace.Width;
        var mass = workspace.Mass;
        var oldEnergy = workspace.OldEnergy;
        var temperature = workspace.Temperature;
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
        var conductance = workspace.Conductance;
        for (var j = 0; j < conductance.Length; j++) conductance[j] = properties.ConductivityWattsPerMeterKelvin / ((width[j] + width[j + 1]) * 0.5);
        var bottom = boundary.ConductivityWattsPerMeterKelvin / (boundary.ConductiveBoundaryThicknessMeters + width[^1] * 0.5);
        var faceConductance = properties.ConductivityWattsPerMeterKelvin / (width[0] * 0.5);
        var radiation = boundary.InfraredEmissivity * PlanetRadiationBalanceModel.StefanBoltzmannWattsPerSquareMeterKelvinFourth;
        var residual = workspace.Residual;
        var diagonal = workspace.Diagonal;
        var lower = workspace.Lower;
        var upper = workspace.Upper;
        var rhs = workspace.RightHandSide;
        var trial = workspace.Trial;
        var converged = false;
        var specificHeat = material.SpecificHeatCapacityJoulesPerKilogramKelvin;
        var latentHeat = material.LatentHeatOfFusionJoulesPerKilogram;
        var meltInterval = material.LiquidusTemperatureKelvin - material.SolidusTemperatureKelvin;
        double Face(double t) => PlanetOriginRockRadiatingBoundary.ResolveTemperature(t,
            properties.ConductivityWattsPerMeterKelvin, width[0] * 0.5, solar, boundary.InfraredEmissivity);

        double Evaluate(double[] values, bool jacobian)
        {
            var face = Face(values[0]);
            var faceSquared = face * face;
            var norm = 0.0;
            for (var j = 0; j < n; j++)
            {
                var inflow = j == 0 ? solar - radiation * faceSquared * faceSquared : conductance[j - 1] * (values[j - 1] - values[j]);
                inflow += j == n - 1 ? bottom * (mantleKelvin - values[j]) : conductance[j] * (values[j + 1] - values[j]);
                var meltFraction = Math.Clamp((values[j] - material.SolidusTemperatureKelvin) / meltInterval, 0.0, 1.0);
                var specificEnthalpy = specificHeat * values[j] + latentHeat * meltFraction;
                residual[j] = specificEnthalpy * mass[j] - oldEnergy[j] - seconds * inflow;
                norm = Math.Max(norm, Math.Abs(residual[j]) / (mass[j] * specificHeat));
                if (!jacobian) continue;
                var cp = specificHeat;
                if (values[j] > material.SolidusTemperatureKelvin && values[j] < material.LiquidusTemperatureKelvin)
                    cp += latentHeat / meltInterval;
                var radSlope = 4.0 * radiation * faceSquared * face;
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
                var valid = true;
                for (var j = 0; j < n; j++)
                {
                    trial[j] = temperature[j] + alpha * rhs[j];
                    if (!double.IsFinite(trial[j]) || trial[j] < 0.0) valid = false;
                }
                if (!valid) continue;
                if (Evaluate(trial, false) >= norm) continue;
                Array.Copy(trial, temperature, n);
                accepted = true;
                break;
            }
            if (!accepted) throw new InvalidOperationException("Implicit column Newton line search failed; nothing was published.");
        }
        if (!converged) throw new InvalidOperationException("Implicit column enthalpy solve did not converge; nothing was published.");
        var delta = workspace.Delta;
        Array.Clear(delta);
        for (var j = 0; j < n - 1; j++)
        {
            var heat = seconds * conductance[j] * (temperature[j] - temperature[j + 1]);
            delta[j] -= heat;
            delta[j + 1] += heat;
        }
        var interior = seconds * bottom * (mantleKelvin - temperature[^1]);
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
            if (Math.Abs(resolved.TemperatureKelvin - temperature[j]) > 1e-6)
                throw new InvalidOperationException("Implicit flux update differs from the converged enthalpy solution.");
        }
        return new(layers, interior * column.AreaSquareMeters, emitted * column.AreaSquareMeters, volume / column.AreaSquareMeters);
    }
}
