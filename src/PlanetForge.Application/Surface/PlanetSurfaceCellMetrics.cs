using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface;

public sealed record PlanetSurfaceCellMetrics(
    PlanetSurfaceGridCellId Cell,
    PlanetVector CenterDirection,
    double ElevationMeters,
    double SlopeRadians,
    PlanetSurfaceGridCellId? DrainageTarget,
    double SteepestDownhillGradient);
