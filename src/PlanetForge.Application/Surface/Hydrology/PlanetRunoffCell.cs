using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

public sealed record PlanetRunoffCell(
    PlanetSurfaceGridCellId Cell,
    double LocalAnnualRunoffMillimeters,
    double DrainageAreaSquareMeters,
    double AccumulatedAnnualRunoffVolumeCubicMeters,
    double MeanDischargeCubicMetersPerSecond);
