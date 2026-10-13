namespace PlanetForge.Domain.Origins;

/// <summary>
/// An explicit deep-mantle convective thermal hypothesis. This is a heat-transfer
/// closure, NOT resolved magma velocity, lateral material flow or a new energy store.
/// </summary>
public readonly record struct PlanetOriginMagmaConvectionParameters(
    double DepthMeters, double DensityKilogramsPerCubicMeter,
    double DynamicViscosityPascalSeconds, double ThermalDiffusivitySquareMetersPerSecond)
{
    public static PlanetOriginMagmaConvectionParameters Reference => new(100_000.0, 3_000.0, 0.1, 1e-6);

    public void Validate()
    {
        if (!double.IsFinite(DepthMeters) || DepthMeters <= 0.0 ||
            !double.IsFinite(DensityKilogramsPerCubicMeter) || DensityKilogramsPerCubicMeter <= 0.0 ||
            !double.IsFinite(DynamicViscosityPascalSeconds) || DynamicViscosityPascalSeconds <= 0.0 ||
            !double.IsFinite(ThermalDiffusivitySquareMetersPerSecond) || ThermalDiffusivitySquareMetersPerSecond <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(DepthMeters), "Deep-magma thermal parameters must be finite and positive.");
    }
}

/// <summary>Read-only diagnostics, calculated from the accepted canonical state.</summary>
public readonly record struct PlanetOriginMagmaConvectionDiagnostics(
    double MeanBasalTemperatureKelvin, double MantleTemperatureKelvin, double RayleighNumber,
    double NusseltNumber, double EffectiveBoundaryThicknessMeters, double MeanUpwardHeatFluxWattsPerSquareMeter);

public static class PlanetOriginMagmaConvection
{
    /// <summary>
    /// Ra = rho g alpha (Tm-Tb) D^3 / (eta kappa), using alpha=3e-5/K and g=9.81 m/s².
    /// Parameterized Nu=max(1, 0.089 Ra^(1/3)) above Ra=1708; Nu=1 otherwise.
    /// This regime approximation is uncalibrated for Hadean composition/rheology.
    /// </summary>
    public static PlanetOriginMagmaConvectionDiagnostics Diagnose(
        PlanetOriginRegionalAirlessParameters boundary, PlanetOriginRockColumnField columns, PlanetOriginThermalTimeline mantle)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(mantle);
        boundary.Validate();
        var properties = boundary.MagmaConvection ??
            throw new InvalidOperationException("Magma convection has not been configured.");
        properties.Validate();
        var area = 0.0;
        var temperatureSum = 0.0;
        var bottomHalfWidthSum = 0.0;
        foreach (var column in columns.Columns)
        {
            var bottom = column.Layers[^1];
            area += column.AreaSquareMeters;
            temperatureSum += column.AreaSquareMeters * bottom.Resolve(columns.Material).TemperatureKelvin;
            bottomHalfWidthSum += bottom.VolumeCubicMeters(columns.Material, columns.Properties) * 0.5;
        }
        if (area <= 0.0 || !double.IsFinite(area)) throw new InvalidOperationException("Magma convection requires finite surface area.");
        var basalTemperature = temperatureSum / area;
        var bottomHalfWidth = bottomHalfWidthSum / area;
        var mantleTemperature = mantle.Rock.TemperatureKelvin;
        var unstableDelta = Math.Max(0.0, mantleTemperature - basalTemperature);
        var kinematicViscosity = properties.DynamicViscosityPascalSeconds / properties.DensityKilogramsPerCubicMeter;
        var rayleigh = 9.81 * 3e-5 * unstableDelta * Math.Pow(properties.DepthMeters, 3) /
            (kinematicViscosity * properties.ThermalDiffusivitySquareMetersPerSecond);
        if (!double.IsFinite(rayleigh) || rayleigh < 0.0)
            throw new InvalidOperationException("Magma convection exceeds the declared finite Rayleigh regime.");
        var nusselt = rayleigh > 1708.0 ? Math.Max(1.0, 0.089 * Math.Cbrt(rayleigh)) : 1.0;
        var effectiveThickness = properties.DepthMeters / nusselt;
        if (!double.IsFinite(effectiveThickness) || effectiveThickness <= 0.0)
            throw new InvalidOperationException("Magma convection produced an invalid boundary thickness.");
        // The implicit column solver also includes its physical bottom half-cell resistance.
        var flux = boundary.ConductivityWattsPerMeterKelvin * unstableDelta / (effectiveThickness + bottomHalfWidth);
        return new(basalTemperature, mantleTemperature, rayleigh, nusselt, effectiveThickness, flux);
    }

    /// <summary>Recompute the mantle exchange coefficient from the candidate at every physical substep.</summary>
    public static PlanetOriginRegionalAirlessParameters ResolveBoundary(
        PlanetOriginRegionalAirlessParameters boundary, PlanetOriginRockColumnField columns, PlanetOriginThermalTimeline mantle) =>
        boundary.MagmaConvection is null ? boundary : boundary with
        {
            ConductiveBoundaryThicknessMeters = Diagnose(boundary, columns, mantle).EffectiveBoundaryThicknessMeters
        };
}
