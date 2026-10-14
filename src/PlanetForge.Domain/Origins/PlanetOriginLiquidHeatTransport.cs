namespace PlanetForge.Domain.Origins;

/// <summary>Parameterized liquid heat transport; no resolved velocity or additional energy reservoir.</summary>
public readonly record struct PlanetOriginLiquidHeatTransportParameters(
    double DynamicViscosityPascalSeconds, double ThermalExpansionPerKelvin, double GravityMetersPerSecondSquared)
{
    public static PlanetOriginLiquidHeatTransportParameters Reference => new(10.0, 3e-5, 9.81);
    public static PlanetOriginLiquidHeatTransportParameters CrystalSuspensionReference => Reference with { UseCrystalSuspensionRheology = true };

    /// <summary>Opt-in crystal-suspension viscosity hypothesis; the original fully-liquid-only reference remains available.</summary>
    public bool UseCrystalSuspensionRheology { get; init; }

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
/// The original reference requires fully liquid material. The optional suspension branch permits unjammed crystal-bearing fluid.
/// Molecular transport remains at the radiating face and at every ineligible interface.
/// </summary>
public static class PlanetOriginLiquidHeatTransport
{
    public static PlanetOriginLiquidHeatTransportDiagnostics Diagnose(PlanetOriginRockColumn column,
        PlanetOriginSurfaceRockPhaseMaterial material, PlanetOriginRockColumnProperties properties,
        PlanetOriginLiquidHeatTransportParameters parameters) => Resolve(column, material, properties, parameters).Diagnostics;

    /// <summary>Abe-type suspension branch (Monteux 2016, equation 7). Null denotes the jammed/nonfluid domain.</summary>
    public static double? ResolveCrystalSuspensionViscosity(double meltFraction, PlanetOriginRockColumnProperties properties,
        PlanetOriginLiquidHeatTransportParameters parameters)
    {
        properties.Validate();
        parameters.Validate();
        if (!double.IsFinite(meltFraction) || meltFraction < 0.0 || meltFraction > 1.0)
            throw new ArgumentOutOfRangeException(nameof(meltFraction));
        var liquidTerm = meltFraction * properties.SolidDensityKilogramsPerCubicMeter;
        var crystalTerm = (1.0 - meltFraction) * properties.LiquidDensityKilogramsPerCubicMeter;
        var available = liquidTerm - 0.67 * crystalTerm;
        if (available <= 0.0) return null;
        if (parameters.DynamicViscosityPascalSeconds > 1e21)
            throw new InvalidOperationException("The suspension reference's melt viscosity cannot exceed its declared upper viscosity bound.");
        var viscosity = parameters.DynamicViscosityPascalSeconds * Math.Pow((liquidTerm + crystalTerm) / available, 2.5);
        if (double.IsNaN(viscosity) || viscosity <= 0.0) throw new InvalidOperationException("Suspension viscosity must remain positive.");
        return Math.Min(1e21, viscosity);
    }

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
        var viscosities = phases.Select(phase => closure.UseCrystalSuspensionRheology ?
            ResolveCrystalSuspensionViscosity(phase.MeltFraction, properties, closure) :
            phase.MeltFraction == 1.0 ? (double?)closure.DynamicViscosityPascalSeconds : null).ToArray();
        if (closure.UseCrystalSuspensionRheology && properties.SolidDensityKilogramsPerCubicMeter < properties.LiquidDensityKilogramsPerCubicMeter)
            throw new InvalidOperationException("The suspension buoyancy approximation requires solid density no lower than liquid density.");
        var maximumDepth = 0.0;
        var maximumRayleigh = 0.0;
        var maximumMultiplier = 1.0;
        var enhanced = 0;
        for (var start = 0; start < phases.Length;)
        {
            if (viscosities[start] is null) { start++; continue; }
            var end = start;
            var depth = 0.0;
            while (end < phases.Length && viscosities[end] is not null) depth += widths[end++];
            maximumDepth = Math.Max(maximumDepth, depth);
            var centreDepth = widths[start] * 0.5;
            var topCentreDepth = centreDepth;
            var suspensionReferenceKelvin = phases[start].TemperatureKelvin;
            for (var j = start; j < end - 1; j++)
            {
                var distance = (widths[j] + widths[j + 1]) * 0.5;
                var cp = material.SpecificHeatCapacityJoulesPerKilogramKelvin;
                var rho = properties.LiquidDensityKilogramsPerCubicMeter;
                var expansion = closure.ThermalExpansionPerKelvin;
                var adiabaticSlope = expansion / cp;
                if (closure.UseCrystalSuspensionRheology)
                {
                    var upper = ResolveMixture(phases[j], material, properties, closure);
                    var lower = ResolveMixture(phases[j + 1], material, properties, closure);
                    cp = lower.Capacity;
                    rho = lower.Density;
                    expansion = lower.Expansion;
                    adiabaticSlope = 0.5 * (upper.Expansion / upper.Capacity + lower.Expansion / lower.Capacity);
                }
                var exponent = adiabaticSlope * closure.GravityMetersPerSecondSquared * distance;
                var adiabaticDrop = phases[j].TemperatureKelvin * double.ExpM1(exponent);
                centreDepth += distance;
                var gradientExcess = phases[j + 1].TemperatureKelvin - phases[j].TemperatureKelvin - adiabaticDrop;
                suspensionReferenceKelvin *= Math.Exp(exponent);
                var referenceTemperature = closure.UseCrystalSuspensionRheology ? suspensionReferenceKelvin :
                    phases[start].TemperatureKelvin * Math.Exp(closure.ThermalExpansionPerKelvin * closure.GravityMetersPerSecondSquared *
                        (centreDepth - topCentreDepth) / material.SpecificHeatCapacityJoulesPerKilogramKelvin);
                var excess = phases[j + 1].TemperatureKelvin - referenceTemperature;
                if (excess <= 0.0 || gradientExcess <= 0.0) continue;
                var viscosity = Math.Max(viscosities[j]!.Value, viscosities[j + 1]!.Value);
                var k = properties.ConductivityWattsPerMeterKelvin;
                var rayleigh = expansion * closure.GravityMetersPerSecondSquared * cp * rho * rho *
                    excess * depth * depth * depth / (k * viscosity);
                var prandtl = cp * viscosity / k;
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

    private static (double Density, double Capacity, double Expansion) ResolveMixture(PlanetRockThermalState phase,
        PlanetOriginSurfaceRockPhaseMaterial material, PlanetOriginRockColumnProperties properties, PlanetOriginLiquidHeatTransportParameters closure)
    {
        var melt = phase.MeltFraction;
        var density = 1.0 / ((1.0 - melt) / properties.SolidDensityKilogramsPerCubicMeter + melt / properties.LiquidDensityKilogramsPerCubicMeter);
        var capacity = material.SpecificHeatCapacityJoulesPerKilogramKelvin;
        var expansion = closure.ThermalExpansionPerKelvin;
        if (melt > 0.0 && melt < 1.0)
        {
            var interval = material.LiquidusTemperatureKelvin - material.SolidusTemperatureKelvin;
            capacity += material.LatentHeatOfFusionJoulesPerKilogram / interval;
            expansion += (properties.SolidDensityKilogramsPerCubicMeter - properties.LiquidDensityKilogramsPerCubicMeter) / (density * interval);
        }
        return (density, capacity, expansion);
    }

}
