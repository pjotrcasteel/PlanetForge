namespace PlanetForge.Domain.Origins;

/// <summary>
/// A globally mixed hydrostatic column with one representative noncondensable
/// gas and water vapour. Gas molar mass is supplied explicitly: a present-day
/// nitrogen-rich atmosphere must not be assumed for the Hadean.
/// The column is a diagnostic, not a three-dimensional circulation model.
/// </summary>
public readonly record struct PlanetMixedAtmosphereParameters(
    double PlanetRadiusMeters,
    double SurfaceGravityMetersPerSecondSquared,
    double DryGasMassKilograms,
    double DryGasMolarMassKilogramsPerMole)
{
    public void Validate()
    {
        RequirePositive(PlanetRadiusMeters, nameof(PlanetRadiusMeters));
        RequirePositive(SurfaceGravityMetersPerSecondSquared, nameof(SurfaceGravityMetersPerSecondSquared));
        if (!double.IsFinite(DryGasMassKilograms) || DryGasMassKilograms < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(DryGasMassKilograms), "Noncondensable mass must be finite and nonnegative.");
        }

        RequirePositive(DryGasMolarMassKilogramsPerMole, nameof(DryGasMolarMassKilogramsPerMole));
    }

    private static void RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(name, "Atmospheric geometry and molar mass must be finite and positive.");
        }
    }
}

public enum PlanetLiquidVaporSaturationRegime
{
    LiquidVaporIapws = 0,
    BelowLiquidTriplePoint = 1,
    AboveWaterCriticalTemperature = 2
}

/// <summary>
/// Strictly diagnostic partial-pressure and saturation capacity. Null values
/// mean the IAPWS liquid-vapour curve is not applicable; they never mean that
/// condensation or freezing has already happened.
/// </summary>
public readonly record struct PlanetMixedAtmosphereSaturationSnapshot(
    PlanetLiquidVaporSaturationRegime Regime,
    double TotalSurfacePressurePascals,
    double WaterVaporPartialPressurePascals,
    double DryGasPartialPressurePascals,
    double? WaterSaturationPressurePascals,
    double? PotentialCondensateMassKilograms);

/// <summary>
/// Ideal-gas well-mixed, hydrostatic **diagnostic**. Total surface pressure
/// uses the atmosphere's entire column mass times surface gravity per surface
/// area; water partial pressure uses its *mole fraction*, not mass fraction.
/// This is not cloud physics or an automatic rain law: partial-pressure
/// equilibrium is only assessed inside the published IAPWS liquid-vapour
/// temperature range. No temperature lapse, nonideal fugacity correction,
/// escape, precipitation rate, salinity or greenhouse forcing is included.
/// </summary>
public static class PlanetMixedAtmosphereSaturation
{
    public const double WaterVaporMolarMassKilogramsPerMole = 0.01801528;

    public static PlanetMixedAtmosphereSaturationSnapshot Evaluate(
        PlanetWaterInventory water,
        PlanetMixedAtmosphereParameters parameters,
        double effectiveLiquidVaporTemperatureKelvin)
    {
        ArgumentNullException.ThrowIfNull(water);
        parameters.Validate();
        if (!double.IsFinite(effectiveLiquidVaporTemperatureKelvin) || effectiveLiquidVaporTemperatureKelvin <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveLiquidVaporTemperatureKelvin),
                "The specified effective saturation temperature must be finite and positive Kelvin.");
        }

        var area = 4.0 * Math.PI * parameters.PlanetRadiusMeters * parameters.PlanetRadiusMeters;
        if (!double.IsFinite(area) || area <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "Global surface area must remain finite and positive.");
        }

        var vaporMass = water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor);
        var totalPressure = SurfacePressure(vaporMass, area, parameters);
        var vaporPressure = WaterPartialPressure(vaporMass, totalPressure, parameters);
        var dryPressure = totalPressure - vaporPressure;
        if (!double.IsFinite(dryPressure) || dryPressure < -Math.Abs(totalPressure) * 1e-14)
        {
            throw new InvalidOperationException("The mixed atmospheric dry-gas pressure must be finite and nonnegative.");
        }

        var regime = effectiveLiquidVaporTemperatureKelvin switch
        {
            < PlanetWaterVaporSaturation.TriplePointKelvin => PlanetLiquidVaporSaturationRegime.BelowLiquidTriplePoint,
            > PlanetWaterVaporSaturation.CriticalPointKelvin => PlanetLiquidVaporSaturationRegime.AboveWaterCriticalTemperature,
            _ => PlanetLiquidVaporSaturationRegime.LiquidVaporIapws
        };

        if (regime != PlanetLiquidVaporSaturationRegime.LiquidVaporIapws)
        {
            return new PlanetMixedAtmosphereSaturationSnapshot(
                regime, totalPressure, vaporPressure, Math.Max(0.0, dryPressure), null, null);
        }

        var saturation = PlanetWaterVaporSaturation.CalculateSaturationPressurePascals(effectiveLiquidVaporTemperatureKelvin);
        var condensate = EstimatePotentialCondensate(vaporMass, vaporPressure, saturation, area, parameters);
        return new PlanetMixedAtmosphereSaturationSnapshot(
            regime, totalPressure, vaporPressure, Math.Max(0.0, dryPressure), saturation, condensate);
    }

    private static double EstimatePotentialCondensate(
        double initialVaporMass, double initialPartialPressure, double saturationPressure,
        double area, PlanetMixedAtmosphereParameters parameters)
    {
        if (initialPartialPressure <= saturationPressure || initialVaporMass == 0.0)
        {
            return 0.0;
        }

        // Condensing vapour changes both column weight and mean molecular
        // composition. Solve for the remaining vapour mass instead of simply
        // applying the pure-steam mass-to-pressure formula in a mixed gas.
        var lowerMass = 0.0;
        var upperMass = initialVaporMass;
        for (var iteration = 0; iteration < 96; iteration++)
        {
            var midMass = lowerMass + (upperMass - lowerMass) * 0.5;
            if (midMass == lowerMass || midMass == upperMass) break;

            var pressure = WaterPartialPressure(midMass, SurfacePressure(midMass, area, parameters), parameters);
            if (pressure > saturationPressure) upperMass = midMass;
            else lowerMass = midMass;
        }

        return Math.Clamp(initialVaporMass - upperMass, 0.0, initialVaporMass);
    }

    private static double SurfacePressure(double vaporMass, double area, PlanetMixedAtmosphereParameters parameters)
    {
        var pressure = (vaporMass + parameters.DryGasMassKilograms) *
            (parameters.SurfaceGravityMetersPerSecondSquared / area);
        if (!double.IsFinite(pressure) || pressure < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(vaporMass), "Column surface pressure must remain finite and nonnegative.");
        }

        return pressure;
    }

    private static double WaterPartialPressure(
        double vaporMass, double totalPressure, PlanetMixedAtmosphereParameters parameters)
    {
        if (vaporMass == 0.0) return 0.0;
        var vaporMoles = vaporMass / WaterVaporMolarMassKilogramsPerMole;
        var dryMoles = parameters.DryGasMassKilograms / parameters.DryGasMolarMassKilogramsPerMole;
        var sum = vaporMoles + dryMoles;
        if (!double.IsFinite(sum) || sum <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(vaporMass), "Atmospheric molar inventory must remain finite.");
        }

        var partial = totalPressure * (vaporMoles / sum);
        if (!double.IsFinite(partial) || partial < 0.0 || partial > totalPressure * (1.0 + 1e-14))
        {
            throw new ArgumentOutOfRangeException(nameof(vaporMass), "Vapour partial pressure must stay inside total pressure.");
        }

        return partial;
    }
}
