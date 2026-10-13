using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Origins;

/// <summary>
/// Read-only density contraction of funded columns relative to their initial
/// thickness. No inherited bedrock, procedural mountains or height amplification.
/// Spherical inverse-distance reconstruction is continuous across cube seams.
/// </summary>
public sealed class PlanetOriginRockColumnElevationSource : IPlanetElevationSource
{
    private readonly PlanetVector[] centers;
    private readonly double[] elevationMeters;
    private readonly double? uniformElevationMeters;

    public PlanetOriginRockColumnElevationSource(PlanetOriginRockColumnField columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        centers = columns.Columns.Select(c => PlanetSurfaceGridGeometry.GetCenterDirection(c.Id)).ToArray();
        elevationMeters = columns.Columns.Select(c =>
            c.ThicknessMeters(columns.Material, columns.Properties) - c.ReferenceThicknessMeters).ToArray();
        uniformElevationMeters = elevationMeters.All(height => height == elevationMeters[0]) ? elevationMeters[0] : null;
    }

    public double SampleElevationMeters(PlanetVector direction, int seed)
    {
        var unit = PlanetVector.Normalize(direction);
        if (uniformElevationMeters is { } height) return height;
        var weightedHeight = 0.0;
        var totalWeight = 0.0;
        for (var i = 0; i < centers.Length; i++)
        {
            var distance = 1.0 - PlanetVector.Dot(unit, centers[i]);
            if (distance <= 1e-12) return elevationMeters[i];
            var weight = 1.0 / (distance * distance);
            weightedHeight += elevationMeters[i] * weight;
            totalWeight += weight;
        }
        return weightedHeight / totalWeight;
    }
}
