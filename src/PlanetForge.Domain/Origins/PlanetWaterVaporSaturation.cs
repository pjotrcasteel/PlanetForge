namespace PlanetForge.Domain.Origins;

/// <summary>
/// Saturation pressure of pure H2O along the liquid-vapor coexistence curve.
/// Coefficients are the IAPWS (Wagner-Pruss) reference correlation, valid
/// between the water triple point and critical temperature only.
/// Reference: https://www.iapws.org/relguide/Supp-sat.html
/// </summary>
public static class PlanetWaterVaporSaturation
{
    public const double TriplePointKelvin = 273.16;
    public const double CriticalPointKelvin = 647.096;
    public const double CriticalPressurePascals = 22_064_000.0;

    public static double CalculateSaturationPressurePascals(double temperatureKelvin)
    {
        if (!double.IsFinite(temperatureKelvin) ||
            temperatureKelvin < TriplePointKelvin || temperatureKelvin > CriticalPointKelvin)
        {
            throw new ArgumentOutOfRangeException(nameof(temperatureKelvin),
                "Liquid-vapor saturation is defined only from the water triple point to its critical temperature.");
        }

        var tau = 1.0 - temperatureKelvin / CriticalPointKelvin;
        var polynomial = -7.85951783 * tau
            + 1.84408259 * Math.Pow(tau, 1.5)
            - 11.7866497 * Math.Pow(tau, 3.0)
            + 22.6807411 * Math.Pow(tau, 3.5)
            - 15.9618719 * Math.Pow(tau, 4.0)
            + 1.80122502 * Math.Pow(tau, 7.5);
        return CriticalPressurePascals * Math.Exp(CriticalPointKelvin / temperatureKelvin * polynomial);
    }
}

/// <summary>
/// Diagnostic equilibrium limit in a simplified hydrostatic pure-water-vapor
/// column. This does not account for noncondensable atmospheric gases,
/// vertical temperature profiles, precipitation kinetics or latent heat.
/// Condensation must never be applied to a world state until those physical
/// constraints are available and accounted.
/// </summary>
public readonly record struct PlanetWaterCondensationPotential(
    double SaturationPressurePascals,
    double VaporPressurePascals,
    double SaturatedVaporMassKilograms,
    double PotentialCondensateMassKilograms);

public static class PlanetWaterCondensationModel
{
    public static PlanetWaterCondensationPotential Estimate(
        PlanetWaterInventory water,
        double temperatureKelvin,
        double radiusMeters,
        double surfaceGravityMetersPerSecondSquared)
    {
        ArgumentNullException.ThrowIfNull(water);
        if (!double.IsFinite(radiusMeters) || radiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), "Planet radius must be finite and positive.");
        }

        if (!double.IsFinite(surfaceGravityMetersPerSecondSquared) || surfaceGravityMetersPerSecondSquared <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceGravityMetersPerSecondSquared), "Surface gravity must be finite and positive.");
        }

        var area = 4.0 * Math.PI * radiusMeters * radiusMeters;
        if (!double.IsFinite(area))
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), "Planetary surface area must be finite.");
        }

        var vaporMass = water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor);
        var pressure = vaporMass * surfaceGravityMetersPerSecondSquared / area;
        var saturation = PlanetWaterVaporSaturation.CalculateSaturationPressurePascals(temperatureKelvin);
        var saturatedMass = saturation * area / surfaceGravityMetersPerSecondSquared;
        if (!double.IsFinite(pressure) || !double.IsFinite(saturatedMass))
        {
            throw new ArgumentOutOfRangeException(nameof(water), "Atmospheric hydrostatic state must remain finite.");
        }

        return new PlanetWaterCondensationPotential(saturation, pressure, saturatedMass,
            Math.Max(0.0, vaporMass - saturatedMass));
    }
}
