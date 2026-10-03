using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

public sealed record PlanetLake(
    int Id,
    double SurfaceElevationMeters,
    double MaximumDepthMeters,
    IReadOnlyList<PlanetSurfaceGridCellId> Cells);
