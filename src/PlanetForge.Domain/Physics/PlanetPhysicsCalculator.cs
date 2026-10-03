using PlanetForge.Domain.Planets;

namespace PlanetForge.Domain.Physics;

public static class PlanetPhysicsCalculator
{
    public static PlanetPhysicsSnapshot Calculate(PlanetPhysicalParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ValidatePositive(parameters.RadiusMeters, nameof(parameters.RadiusMeters));
        ValidatePositive(parameters.MassKilograms, nameof(parameters.MassKilograms));
        ValidatePositive(parameters.RotationPeriodSeconds, nameof(parameters.RotationPeriodSeconds));
        ValidatePositive(parameters.OrbitalDistanceMeters, nameof(parameters.OrbitalDistanceMeters));
        ValidatePositive(parameters.StellarLuminosityWatts, nameof(parameters.StellarLuminosityWatts));
        ValidateAlbedo(parameters.BondAlbedo);

        var radiusSquared = parameters.RadiusMeters * parameters.RadiusMeters;
        var surfaceGravity = PhysicalConstants.GravitationalConstant * parameters.MassKilograms / radiusSquared;
        var solarFlux = parameters.StellarLuminosityWatts / (4.0 * Math.PI * parameters.OrbitalDistanceMeters * parameters.OrbitalDistanceMeters);
        var absorbedAverageFlux = solarFlux * (1.0 - parameters.BondAlbedo) / 4.0;
        var equilibriumTemperature = Math.Pow(absorbedAverageFlux / PhysicalConstants.StefanBoltzmannConstant, 0.25);
        var volume = 4.0 / 3.0 * Math.PI * parameters.RadiusMeters * radiusSquared;
        var meanDensity = parameters.MassKilograms / volume;
        var escapeVelocity = Math.Sqrt(2.0 * PhysicalConstants.GravitationalConstant * parameters.MassKilograms / parameters.RadiusMeters);

        return new PlanetPhysicsSnapshot(surfaceGravity, solarFlux, equilibriumTemperature, meanDensity, escapeVelocity);
    }

    private static void ValidatePositive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite and greater than zero.");
        }
    }

    private static void ValidateAlbedo(double albedo)
    {
        if (!double.IsFinite(albedo) || albedo < 0.0 || albedo > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(albedo), albedo, "Bond albedo must be between zero and one.");
        }
    }
}
