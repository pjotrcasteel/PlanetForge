namespace PlanetForge.Domain.Origins;

/// <summary>
/// One plane-parallel, non-scattering, local-thermodynamic-equilibrium
/// longwave layer above a black rock surface. OpticalDepth is the supplied
/// vertically integrated absorption coefficient, NOT inferred from steam
/// pressure or an uncalibrated greenhouse multiplier.
/// </summary>
public readonly record struct PlanetOriginGreyRadiativeColumn(
    double PlanetRadiusMeters,
    double SurfaceTemperatureKelvin,
    double AtmosphericLayerTemperatureKelvin,
    double LongwaveOpticalDepth)
{
    public void Validate()
    {
        if (!double.IsFinite(PlanetRadiusMeters) || PlanetRadiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(PlanetRadiusMeters),
                "The spherical radiating surface needs a finite positive radius.");
        }

        if (!double.IsFinite(SurfaceTemperatureKelvin) || SurfaceTemperatureKelvin < 0.0 ||
            !double.IsFinite(AtmosphericLayerTemperatureKelvin) || AtmosphericLayerTemperatureKelvin < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(SurfaceTemperatureKelvin),
                "Blackbody radiation requires finite nonnegative absolute temperatures.");
        }

        if (!double.IsFinite(LongwaveOpticalDepth) || LongwaveOpticalDepth < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(LongwaveOpticalDepth),
                "A non-scattering absorption optical depth must be finite and nonnegative.");
        }
    }
}

/// <summary>
/// Explicit TOA radiative balance and the *separate* surface/atmosphere
/// boundary fluxes, in watts. Surface and atmospheric net cooling can be
/// negative; their sum must equal the positive thermal energy lost to space.
/// This DTO does not itself debit any energy reservoir.
/// </summary>
public sealed record PlanetOriginGreyRadiativeFlux(
    double HemisphericSurfaceTransmittance,
    double SurfaceEmittedPowerWatts,
    double SurfaceTransmittedToSpaceWatts,
    double AtmosphereAbsorbedSurfacePowerWatts,
    double AtmosphereEmittedUpWatts,
    double AtmosphereEmittedDownWatts,
    double SurfaceNetCoolingWatts,
    double AtmosphereNetCoolingWatts,
    double TotalOutgoingToSpaceWatts)
{
    public double PlanetConservationResidualWatts =>
        SurfaceNetCoolingWatts + AtmosphereNetCoolingWatts - TotalOutgoingToSpaceWatts;
}

/// <summary>
/// Exact formal-solution structure for a single isothermal grey absorbing
/// atmospheric layer: I(top, mu) = B(surface) exp(-tau/mu) +
/// B(layer) [1 - exp(-tau/mu)]. Uses 16-point Gaussian hemisphere
/// quadrature for the angular integral. No scattering, clouds, convection,
/// vertical lapse, wavelength bands, steam continuum or pressure-dependent
/// opacity is modeled. This is a bounded numerical radiation KERNEL, not a
/// calibrated Hadean steam-atmosphere model or a timestep rate proposer.
/// </summary>
public static class PlanetOriginGreyRadiativeTransfer
{
    // Half-range 16-point Gauss-Legendre abscissae/weights over mu in [0, 1].
    // For a transparent layer, 2 * SUM(weight * mu) = 1 exactly.
    private static readonly double[] Cosines =
    [
        0.00529953250417503, 0.02771248846338370, 0.06718439880608412, 0.12229779582249850,
        0.19106187779867811, 0.27099161117138630, 0.35919822461037054, 0.45249374508118130,
        0.54750625491881880, 0.64080177538962950, 0.72900838882861360, 0.80893812220132190,
        0.87770220417750160, 0.93281560119391600, 0.97228751153661630, 0.99470046749582500
    ];

    private static readonly double[] Weights =
    [
        0.01357622970587709, 0.03112676196932373, 0.04757925584124630, 0.06231448562776704,
        0.07479799440828835, 0.08457825969750132, 0.09130170752246182, 0.09472530522753432,
        0.09472530522753432, 0.09130170752246182, 0.08457825969750132, 0.07479799440828835,
        0.06231448562776704, 0.04757925584124630, 0.03112676196932373, 0.01357622970587709
    ];

    public static PlanetOriginGreyRadiativeFlux Calculate(PlanetOriginGreyRadiativeColumn column)
    {
        column.Validate();
        var transmission = CalculateHemisphericTransmittance(column.LongwaveOpticalDepth);
        var sphereArea = 4.0 * Math.PI * column.PlanetRadiusMeters * column.PlanetRadiusMeters;
        var sigma = PlanetRadiationBalanceModel.StefanBoltzmannWattsPerSquareMeterKelvinFourth;
        var surfaceEmission = sphereArea * sigma * FourthPower(column.SurfaceTemperatureKelvin);
        var layerOneHemisphere = sphereArea * sigma * FourthPower(column.AtmosphericLayerTemperatureKelvin) *
            (1.0 - transmission);
        var transmittedSurface = surfaceEmission * transmission;
        var absorbedSurface = surfaceEmission * (1.0 - transmission);
        var surfaceNetCooling = surfaceEmission - layerOneHemisphere;
        var atmosphereNetCooling = (2.0 * layerOneHemisphere) - absorbedSurface;
        var outgoing = transmittedSurface + layerOneHemisphere;

        if (!double.IsFinite(surfaceEmission) || !double.IsFinite(layerOneHemisphere) ||
            !double.IsFinite(transmittedSurface) || !double.IsFinite(absorbedSurface) ||
            !double.IsFinite(surfaceNetCooling) || !double.IsFinite(atmosphereNetCooling) ||
            !double.IsFinite(outgoing))
        {
            throw new ArgumentOutOfRangeException(nameof(column),
                "Thermal radiation flux must fit a finite energy ledger.");
        }

        return new PlanetOriginGreyRadiativeFlux(
            transmission,
            surfaceEmission,
            transmittedSurface,
            absorbedSurface,
            layerOneHemisphere,
            layerOneHemisphere,
            surfaceNetCooling,
            atmosphereNetCooling,
            outgoing);
    }

    private static double CalculateHemisphericTransmittance(double opticalDepth)
    {
        if (opticalDepth == 0.0) return 1.0;
        var weightedTransmission = 0.0;
        for (var i = 0; i < Cosines.Length; i++)
        {
            weightedTransmission += Weights[i] * Cosines[i] * Math.Exp(-opticalDepth / Cosines[i]);
        }

        return Math.Clamp(2.0 * weightedTransmission, 0.0, 1.0);
    }

    private static double FourthPower(double value)
    {
        var squared = value * value;
        return squared * squared;
    }
}
