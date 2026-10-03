using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface;

public sealed class PlanetLocalSurfacePatchSampler(IPlanetElevationSource elevationSource)
{
    public PlanetLocalSurfacePatch Sample(
        PlanetVector anchorDirection,
        double patchSizeMeters,
        int cellsPerAxis,
        int seed,
        double planetRadiusMeters)
    {
        Validate(patchSizeMeters, cellsPerAxis, planetRadiusMeters);
        var normalizedAnchor = PlanetVector.Normalize(anchorDirection);
        var anchorElevationMeters = elevationSource.SampleElevationMeters(normalizedAnchor, seed);
        var frame = PlanetLocalFrame.Create(normalizedAnchor, planetRadiusMeters, anchorElevationMeters);
        var pointsPerAxis = cellsPerAxis + 1;
        var points = new PlanetLocalSurfacePoint[pointsPerAxis * pointsPerAxis];
        var halfSizeMeters = patchSizeMeters * 0.5;

        for (var y = 0; y < pointsPerAxis; y++)
        {
            var northMeters = ToLocalCoordinate(y, cellsPerAxis, patchSizeMeters, halfSizeMeters);

            for (var x = 0; x < pointsPerAxis; x++)
            {
                var eastMeters = ToLocalCoordinate(x, cellsPerAxis, patchSizeMeters, halfSizeMeters);
                var direction = ProjectToSurfaceDirection(normalizedAnchor, frame, eastMeters, northMeters, planetRadiusMeters);
                var elevationMeters = elevationSource.SampleElevationMeters(direction, seed);
                var worldPositionMeters = direction * (planetRadiusMeters + elevationMeters);
                var localPosition = frame.ToLocal(worldPositionMeters);
                points[(y * pointsPerAxis) + x] = new PlanetLocalSurfacePoint(direction, elevationMeters, localPosition);
            }
        }

        return new PlanetLocalSurfacePatch(frame, patchSizeMeters, cellsPerAxis, points);
    }

    private static PlanetVector ProjectToSurfaceDirection(
        PlanetVector anchorDirection,
        PlanetLocalFrame frame,
        double eastMeters,
        double northMeters,
        double planetRadiusMeters)
    {
        var referenceSpherePoint =
            (anchorDirection * planetRadiusMeters) +
            (frame.East * eastMeters) +
            (frame.North * northMeters);
        return PlanetVector.Normalize(referenceSpherePoint);
    }

    private static double ToLocalCoordinate(int coordinate, int cellsPerAxis, double patchSizeMeters, double halfSizeMeters) =>
        (coordinate / (double)cellsPerAxis * patchSizeMeters) - halfSizeMeters;

    private static void Validate(double patchSizeMeters, int cellsPerAxis, double planetRadiusMeters)
    {
        if (!double.IsFinite(planetRadiusMeters) || planetRadiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMeters), planetRadiusMeters, "Planet radius must be finite and greater than zero.");
        }

        if (!double.IsFinite(patchSizeMeters) || patchSizeMeters <= 0.0 || patchSizeMeters > planetRadiusMeters * 0.2)
        {
            throw new ArgumentOutOfRangeException(nameof(patchSizeMeters), patchSizeMeters, "Local patch size must be positive and no larger than 20% of the planet radius.");
        }

        if (cellsPerAxis < 1 || cellsPerAxis > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(cellsPerAxis), cellsPerAxis, "Cells per axis must be between 1 and 256.");
        }
    }
}
