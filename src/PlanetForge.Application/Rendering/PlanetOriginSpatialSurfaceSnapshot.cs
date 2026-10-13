using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Rendering;

/// <summary>
/// Display-only spatial samples from one canonical, conserved rock epoch.
/// Every value is derived from the existing regional cell enthalpy; these
/// samples are not a second simulation state or a source of physical heat.
/// </summary>
public sealed record PlanetOriginSpatialSurfaceSnapshot(
    long Revision, int Width, int Height, float[] TemperatureKelvin, float[] MeltFraction);

public static class PlanetOriginSpatialSurfaceProjection
{
    private const int AtlasWidth = 256;
    private const int AtlasHeight = 128;

    public static PlanetOriginSpatialSurfaceSnapshot Create(PlanetOriginCanonicalRockCells region)
    {
        ArgumentNullException.ThrowIfNull(region);
        var field = region.Field;
        var centers = field.Cells.Select(c => PlanetSurfaceGridGeometry.GetCenterDirection(c.Id)).ToArray();
        var rock = field.Cells.Select(c => c.Resolve(field.Material)).ToArray();
        var temperatures = new float[AtlasWidth * AtlasHeight];
        var melts = new float[temperatures.Length];

        const int neighborCount = 4;
        Span<int> nearest = stackalloc int[neighborCount];
        Span<double> proximity = stackalloc double[neighborCount];

        for (var y = 0; y < AtlasHeight; y++)
        {
            var latitude = (((y + 0.5) / AtlasHeight) - 0.5) * Math.PI;
            var sinLat = Math.Sin(latitude);
            var cosLat = Math.Cos(latitude);
            for (var x = 0; x < AtlasWidth; x++)
            {
                var longitude = (((x + 0.5) / AtlasWidth) - 0.5) * 2.0 * Math.PI;
                var unit = new PlanetVector(cosLat * Math.Cos(longitude), sinLat, cosLat * Math.Sin(longitude));
                // Reconstruct the *display* field from nearby physical cell centers.
                // Dot products are spherical: no artificial seams at cube faces or longitude wrap.
                // Physical cell enthalpy and its conservation ledger are never changed.

                nearest.Fill(-1);
                proximity.Fill(double.NegativeInfinity);
                for (var cellIndex = 0; cellIndex < centers.Length; cellIndex++)
                {
                    var dot = PlanetVector.Dot(unit, centers[cellIndex]);
                    for (var slot = 0; slot < neighborCount; slot++)
                    {
                        if (dot <= proximity[slot]) continue;
                        for (var shift = neighborCount - 1; shift > slot; shift--)
                        {
                            proximity[shift] = proximity[shift - 1];
                            nearest[shift] = nearest[shift - 1];
                        }
                        proximity[slot] = dot;
                        nearest[slot] = cellIndex;
                        break;
                    }
                }

                var i = y * AtlasWidth + x;
                if (proximity[0] >= 1.0 - 1e-12)
                {
                    temperatures[i] = checked((float)rock[nearest[0]].TemperatureKelvin);
                    melts[i] = checked((float)rock[nearest[0]].MeltFraction);
                    continue;
                }

                var weightSum = 0.0;
                var temperatureSum = 0.0;
                var meltSum = 0.0;
                var minimumTemperature = double.PositiveInfinity;
                var maximumTemperature = double.NegativeInfinity;
                var minimumMelt = 1.0;
                var maximumMelt = 0.0;
                for (var slot = 0; slot < neighborCount; slot++)
                {
                    var neighbor = nearest[slot];
                    if (neighbor < 0) continue;
                    var weight = 1.0 / Math.Pow(Math.Max(1e-12, 1.0 - proximity[slot]), 2.0);
                    var temperature = rock[neighbor].TemperatureKelvin;
                    var melt = rock[neighbor].MeltFraction;
                    weightSum += weight;
                    temperatureSum += weight * temperature;
                    meltSum += weight * melt;
                    minimumTemperature = Math.Min(minimumTemperature, temperature);
                    maximumTemperature = Math.Max(maximumTemperature, temperature);
                    minimumMelt = Math.Min(minimumMelt, melt);
                    maximumMelt = Math.Max(maximumMelt, melt);
                }

                temperatures[i] = checked((float)Math.Clamp(temperatureSum / weightSum, minimumTemperature, maximumTemperature));
                melts[i] = checked((float)Math.Clamp(meltSum / weightSum, minimumMelt, maximumMelt));
            }
        }

        return new PlanetOriginSpatialSurfaceSnapshot(region.Epoch.Revision, AtlasWidth, AtlasHeight, temperatures, melts);
    }
}
