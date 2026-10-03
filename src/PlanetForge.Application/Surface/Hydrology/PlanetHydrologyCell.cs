using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

public sealed record PlanetHydrologyCell(
    PlanetSurfaceGridCellId Cell,
    double RawElevationMeters,
    double FilledElevationMeters,
    PlanetSurfaceGridCellId? DrainageTarget,
    long ContributingLandCellCount,
    bool IsOcean,
    double DepressionFillDepthMeters);
