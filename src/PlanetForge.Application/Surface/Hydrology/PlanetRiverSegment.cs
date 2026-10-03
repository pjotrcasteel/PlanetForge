using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

public sealed record PlanetRiverSegment(
    PlanetSurfaceGridCellId From,
    PlanetSurfaceGridCellId To,
    PlanetVector FromDirection,
    PlanetVector ToDirection,
    double FromElevationMeters,
    double ToElevationMeters,
    long ContributingLandCellCount,
    int StrahlerOrder);
