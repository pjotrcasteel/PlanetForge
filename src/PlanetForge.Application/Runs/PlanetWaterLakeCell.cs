using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Runs;

public sealed record PlanetWaterLakeCell(
    double X,
    double Y,
    double Z,
    double AngularRadiusRadians)
{
    public int LakeId { get; init; } = -1;

    public double SurfaceRadiusRatio { get; init; } = 1.0005;

    public IReadOnlyList<PlanetVector> BoundaryDirections { get; init; } = [];
}
