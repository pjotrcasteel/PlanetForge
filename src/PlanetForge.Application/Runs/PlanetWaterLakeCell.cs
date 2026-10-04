using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Runs;

public sealed record PlanetWaterLakeCell(
    int LakeId,
    double SurfaceRadiusRatio,
    IReadOnlyList<PlanetVector> BoundaryDirections);
