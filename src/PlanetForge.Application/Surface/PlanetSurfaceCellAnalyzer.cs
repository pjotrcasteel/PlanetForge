using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface;

public sealed class PlanetSurfaceCellAnalyzer(IPlanetElevationSource elevationSource)
{
    public PlanetSurfaceCellMetrics Analyze(PlanetSurfaceGridCellId cell, int seed, double planetRadiusMeters)
    {
        ValidatePlanetRadius(planetRadiusMeters);
        var centerDirection = PlanetSurfaceGridGeometry.GetCenterDirection(cell);
        var elevationMeters = elevationSource.SampleElevationMeters(centerDirection, seed);
        var maximumGradientMagnitude = 0.0;
        var steepestDownhillGradient = 0.0;
        PlanetSurfaceGridCellId? drainageTarget = null;

        foreach (var direction in Enum.GetValues<PlanetGridDirection>())
        {
            var neighbor = PlanetSurfaceGridTopology.GetNeighbor(cell, direction).Cell;
            var neighborDirection = PlanetSurfaceGridGeometry.GetCenterDirection(neighbor);
            var neighborElevationMeters = elevationSource.SampleElevationMeters(neighborDirection, seed);
            var angularDistance = Math.Acos(Math.Clamp(PlanetVector.Dot(centerDirection, neighborDirection), -1.0, 1.0));
            var surfaceDistanceMeters = angularDistance * planetRadiusMeters;
            var gradient = (neighborElevationMeters - elevationMeters) / surfaceDistanceMeters;
            maximumGradientMagnitude = Math.Max(maximumGradientMagnitude, Math.Abs(gradient));

            if (gradient < steepestDownhillGradient)
            {
                steepestDownhillGradient = gradient;
                drainageTarget = neighbor;
            }
        }

        return new PlanetSurfaceCellMetrics(
            cell,
            centerDirection,
            elevationMeters,
            Math.Atan(maximumGradientMagnitude),
            drainageTarget,
            steepestDownhillGradient);
    }

    private static void ValidatePlanetRadius(double planetRadiusMeters)
    {
        if (!double.IsFinite(planetRadiusMeters) || planetRadiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMeters), planetRadiusMeters, "Planet radius must be finite and greater than zero.");
        }
    }
}
