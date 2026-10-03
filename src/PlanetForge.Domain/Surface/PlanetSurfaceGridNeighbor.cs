namespace PlanetForge.Domain.Surface;

public readonly record struct PlanetSurfaceGridNeighbor(PlanetSurfaceGridCellId Cell, PlanetGridDirection ReturnDirection);
