using PlanetForge.Domain.Planets;

namespace PlanetForge.Domain.Atmosphere;

public static class AtmosphereCalculator
{
    public static AtmosphereSnapshot Calculate(AtmosphereParameters atmosphere, PlanetPhysicalParameters planet, PlanetPhysicsSnapshot physics)
    {
        ArgumentNullException.ThrowIfNull(atmosphere);
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(physics);
        Validate(atmosphere);

        var surfaceArea = 4.0 * Math.PI * planet.RadiusMeters * planet.RadiusMeters;
        var pressure = atmosphere.MassKilograms * physics.SurfaceGravityMetersPerSecondSquared / surfaceArea;
        var earthAtmosphereMasses = atmosphere.MassKilograms / EarthAtmosphereReference.TotalMassKilograms;
        var forcing = atmosphere.MassKilograms <= 0.0
            ? 0.0
            : EarthAtmosphereReference.CarbonDioxideForcingCoefficientWattsPerSquareMeter
                * Math.Log(atmosphere.CarbonDioxidePartsPerMillion / EarthAtmosphereReference.PreindustrialCarbonDioxidePartsPerMillion);
        var carbonDioxideFraction = atmosphere.CarbonDioxidePartsPerMillion / 1_000_000.0;
        var otherGasFraction = Math.Max(0.0, 1.0 - atmosphere.NitrogenFraction - atmosphere.OxygenFraction - carbonDioxideFraction);

        return new AtmosphereSnapshot(pressure, earthAtmosphereMasses, forcing, otherGasFraction);
    }

    private static void Validate(AtmosphereParameters atmosphere)
    {
        if (!double.IsFinite(atmosphere.MassKilograms) || atmosphere.MassKilograms < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(atmosphere.MassKilograms), atmosphere.MassKilograms, "Atmospheric mass must be finite and non-negative.");
        }

        ValidateFraction(atmosphere.NitrogenFraction, nameof(atmosphere.NitrogenFraction));
        ValidateFraction(atmosphere.OxygenFraction, nameof(atmosphere.OxygenFraction));

        if (!double.IsFinite(atmosphere.CarbonDioxidePartsPerMillion) || atmosphere.CarbonDioxidePartsPerMillion <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(atmosphere.CarbonDioxidePartsPerMillion),
                atmosphere.CarbonDioxidePartsPerMillion,
                "Carbon dioxide concentration must be finite and greater than zero.");
        }

        var carbonDioxideFraction = atmosphere.CarbonDioxidePartsPerMillion / 1_000_000.0;
        if (atmosphere.NitrogenFraction + atmosphere.OxygenFraction + carbonDioxideFraction > 1.0)
        {
            throw new ArgumentException("Atmospheric composition fractions cannot exceed one.", nameof(atmosphere));
        }
    }

    private static void ValidateFraction(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Atmospheric fraction must be between zero and one.");
        }
    }
}
