using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

public sealed record PlanetWatershed(
    PlanetSurfaceGridCellId Outlet,
    bool OutletIsOcean,
    long LandCellCount,
    long OutletContributingLandCellCount);
