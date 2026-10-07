using PlanetForge.Domain.Physics;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

public sealed class PlanetCryosphereRunoffModel
{
    private const double DegreeDayMeltMillimetersPerKelvinDay = 4.0;
    private const double DaysPerYear = 365.25;
    private const double MaximumAnnualMeltwaterMillimeters = 3_000.0;
    private const double PolarCoolingKelvin = 22.0;
    private const double EnvironmentalLapseRateKelvinPerMeter = 0.0065;

    public PlanetCryosphereRunoffSnapshot Build(
        PlanetHydrologySnapshot hydrology,
        double seaLevelMeters,
        double surfaceTemperatureKelvin,
        double landIceFraction,
        double snowCoverFraction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hydrology);
        ValidateFinite(seaLevelMeters, nameof(seaLevelMeters));
        ValidateFinite(surfaceTemperatureKelvin, nameof(surfaceTemperatureKelvin));

        var boundedLandIceFraction = Math.Clamp(landIceFraction, 0.0, 1.0);
        var boundedSnowCoverFraction = Math.Clamp(snowCoverFraction, 0.0, 1.0);
        var meltwater = new double[hydrology.Layout.CellCount];
        var landCellCount = 0;
        var totalMeltwater = 0.0;
        var peakMeltwater = 0.0;

        for (var index = 0; index < meltwater.Length; index++)
        {
            CheckCancellation(index, cancellationToken);
            var cell = hydrology.Cells[index];
            if (cell.IsOcean)
            {
                continue;
            }

            landCellCount++;
            var direction = PlanetSurfaceGridGeometry.GetCenterDirection(cell.Cell);
            var latitudeSignal = Math.Abs(direction.Y);
            var elevationAboveSeaLevel = Math.Max(0.0, cell.RawElevationMeters - seaLevelMeters);
            var polarSupport = SmoothStep(0.35, 0.94, latitudeSignal);
            var highlandSupport = SmoothStep(700.0, 3_600.0, elevationAboveSeaLevel);
            var localTemperatureKelvin =
                surfaceTemperatureKelvin -
                (PolarCoolingKelvin * Math.Pow(latitudeSignal, 1.35)) -
                (elevationAboveSeaLevel * EnvironmentalLapseRateKelvinPerMeter);
            var temperatureExcessKelvin = Math.Max(0.0, localTemperatureKelvin - PhysicalConstants.KelvinOffsetCelsius);
            var availableIce = Math.Clamp(
                (boundedLandIceFraction * (0.12 + (0.68 * polarSupport) + (0.42 * highlandSupport))) +
                (boundedSnowCoverFraction * (0.08 + (0.38 * polarSupport) + (0.32 * highlandSupport))),
                0.0,
                1.0);
            var potentialMeltwater = temperatureExcessKelvin * DegreeDayMeltMillimetersPerKelvinDay * DaysPerYear;
            var localMeltwater = Math.Min(potentialMeltwater, MaximumAnnualMeltwaterMillimeters) * availableIce;
            meltwater[index] = localMeltwater;
            totalMeltwater += localMeltwater;
            peakMeltwater = Math.Max(peakMeltwater, localMeltwater);
        }

        var meanMeltwater = landCellCount == 0 ? 0.0 : totalMeltwater / landCellCount;
        return new PlanetCryosphereRunoffSnapshot(meltwater, meanMeltwater, peakMeltwater);
    }

    private static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite.");
        }
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var amount = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
        return amount * amount * (3.0 - (2.0 * amount));
    }

    private static void CheckCancellation(int iteration, CancellationToken cancellationToken)
    {
        if ((iteration & 1023) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
