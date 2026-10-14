namespace PlanetForge.Domain.Origins;

/// <summary>Parameterized liquid heat transport; no resolved velocity or additional energy reservoir.</summary>
public readonly record struct PlanetOriginLiquidHeatTransportParameters(
    double DynamicViscosityPascalSeconds, double ThermalExpansionPerKelvin, double GravityMetersPerSecondSquared)
{
    public static PlanetOriginLiquidHeatTransportParameters Reference => new(10.0, 3e-5, 9.81);

    public void Validate()
    {
        if (!double.IsFinite(DynamicViscosityPascalSeconds) || DynamicViscosityPascalSeconds <= 0.0 ||
            !double.IsFinite(ThermalExpansionPerKelvin) || ThermalExpansionPerKelvin <= 0.0 ||
            !double.IsFinite(GravityMetersPerSecondSquared) || GravityMetersPerSecondSquared <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(DynamicViscosityPascalSeconds));
    }
}

public readonly record struct PlanetOriginLiquidHeatTransportDiagnostics(
    double ConnectedLiquidDepthMeters, double MaximumRayleighNumber, double MaximumConductivityMultiplier, int EnhancedInterfaceCount);

/// <summary>
/// Thin-column turbulent effective-conductivity hypothesis following Monteux et al. (2016), equations 2–5.
/// Only connected fully liquid, superadiabatically unstable material is eligible. Mixed/solid layers break connectivity.
/// Molecular transport remains at the radiating face and at every ineligible interface.
/// </summary>
public static class PlanetOriginLiquidHeatTransport
{
    public static PlanetOriginLiquidHeatTransportDiagnostics Diagnose(PlanetOriginRockColumn column,
        PlanetOriginSurfaceRockPhaseMaterial material, PlanetOriginRockColumnProperties properties,
        PlanetOriginLiquidHeatTransportParameters parameters) => Resolve(column, material, properties, parameters).Diagnostics;

    internal static (double[] Conductance, PlanetOriginLiquidHeatTransportDiagnostics Diagnostics) Resolve(
        PlanetOriginRockColumn column, PlanetOriginSurfaceRockPhaseMaterial material, PlanetOriginRockColumnProperties properties,
        PlanetOriginLiquidHeatTransportParameters? parameters)
    {
        var phases = column.Layers.Select(layer => layer.Resolve(material)).ToArray();
        var widths = column.Layers.Select(layer => layer.VolumeCubicMeters(material, properties) / column.AreaSquareMeters).ToArray();
        var conductance = new double[widths.Length - 1];
        for (var j = 0; j < conductance.Length; j++)
            conductance[j] = properties.ConductivityWattsPerMeterKelvin / ((widths[j] + widths[j + 1]) * 0.5);
        if (parameters is not { } closure) return (conductance, new(0.0, 0.0, 1.0, 0));
        closure.Validate();
        properties.Validate();
        if (!properties.UseImplicitDepthTransport || !properties.UseRadiatingInterface || properties.ConductivityWattsPerMeterKelvin <= 0.0)
            throw new InvalidOperationException("Liquid heat transport requires implicit depth transport with positive molecular conductivity.");
        var maximumDepth = 0.0;
        var maximumRayleigh = 0.0;
        var maximumMultiplier = 1.0;
        var enhanced = 0;
        for (var start = 0; start < phases.Length;)
        {
            if (phases[start].MeltFraction != 1.0) { start++; continue; }
            var end = start;
            var depth = 0.0;
            while (end < phases.Length && phases[end].MeltFraction == 1.0) depth += widths[end++];
            maximumDepth = Math.Max(maximumDepth, depth);
            var centreDepth = widths[start] * 0.5;
            var topCentreDepth = centreDepth;
            for (var j = start; j < end - 1; j++)
            {
                var distance = (widths[j] + widths[j + 1]) * 0.5;
                var adiabaticDrop = phases[j].TemperatureKelvin * double.ExpM1(
                    closure.ThermalExpansionPerKelvin * closure.GravityMetersPerSecondSquared * distance /
                    material.SpecificHeatCapacityJoulesPerKilogramKelvin);
                centreDepth += distance;
                var gradientExcess = phases[j + 1].TemperatureKelvin - phases[j].TemperatureKelvin - adiabaticDrop;
                var referenceTemperature = phases[start].TemperatureKelvin * Math.Exp(
                    closure.ThermalExpansionPerKelvin * closure.GravityMetersPerSecondSquared * (centreDepth - topCentreDepth) /
                    material.SpecificHeatCapacityJoulesPerKilogramKelvin);
                var excess = phases[j + 1].TemperatureKelvin - referenceTemperature;
                if (excess <= 0.0 || gradientExcess <= 0.0) continue;
                var rho = properties.LiquidDensityKilogramsPerCubicMeter;
                var k = properties.ConductivityWattsPerMeterKelvin;
                var cp = material.SpecificHeatCapacityJoulesPerKilogramKelvin;
                var rayleigh = closure.ThermalExpansionPerKelvin * closure.GravityMetersPerSecondSquared * cp * rho * rho *
                    excess * depth * depth * depth / (k * closure.DynamicViscosityPascalSeconds);
                var prandtl = cp * closure.DynamicViscosityPascalSeconds / k;
                var convectiveMultiplier = rayleigh <= 1708.0 ? 0.0 : rayleigh < 1e19 ? 0.089 * Math.Cbrt(rayleigh) :
                    0.22 * Math.Pow(rayleigh, 2.0 / 7.0) * Math.Pow(prandtl, -1.0 / 7.0);
                // Apply the closure only to the superadiabatic part of the gradient.
                // Frozen beginning-of-step coefficients retain a shared positive face conductance.
                var unstableFraction = gradientExcess /
                    (phases[j + 1].TemperatureKelvin - phases[j].TemperatureKelvin);
                var multiplier = 1.0 + convectiveMultiplier * unstableFraction;
                if (!double.IsFinite(rayleigh) || !double.IsFinite(multiplier) || multiplier < 1.0)
                    throw new InvalidOperationException("Liquid heat transport exceeds its finite parameter regime.");
                conductance[j] *= multiplier;
                maximumRayleigh = Math.Max(maximumRayleigh, rayleigh);
                maximumMultiplier = Math.Max(maximumMultiplier, multiplier);
                if (multiplier > 1.0) enhanced++;
            }
            start = end;
        }
        return (conductance, new(maximumDepth, maximumRayleigh, maximumMultiplier, enhanced));
    }
}
