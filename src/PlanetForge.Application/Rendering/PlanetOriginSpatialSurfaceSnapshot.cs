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

        for (var y = 0; y < AtlasHeight; y++)
        {
            var latitude = (((y + 0.5) / AtlasHeight) - 0.5) * Math.PI;
            var sinLat = Math.Sin(latitude);
            var cosLat = Math.Cos(latitude);
            for (var x = 0; x < AtlasWidth; x++)
            {
                var longitude = (((x + 0.5) / AtlasWidth) - 0.5) * 2.0 * Math.PI;
                var unit = new PlanetVector(cosLat * Math.Cos(longitude), sinLat, cosLat * Math.Sin(longitude));
                var bestIndex = 0;
                var bestDot = double.NegativeInfinity;
                for (var cellIndex = 0; cellIndex < centers.Length; cellIndex++)
                {
                    var dot = PlanetVector.Dot(unit, centers[cellIndex]);
                    if (dot <= bestDot) continue;
                    bestDot = dot;
                    bestIndex = cellIndex;
                }

                var i = y * AtlasWidth + x;
                temperatures[i] = checked((float)rock[bestIndex].TemperatureKelvin);
                melts[i] = checked((float)rock[bestIndex].MeltFraction);
            }
        }

        return new PlanetOriginSpatialSurfaceSnapshot(region.Epoch.Revision, AtlasWidth, AtlasHeight, temperatures, melts);
    }
}
