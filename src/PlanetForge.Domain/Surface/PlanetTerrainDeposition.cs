namespace PlanetForge.Domain.Surface;

public sealed record PlanetTerrainDeposition(
    PlanetVector CenterDirection,
    double CoreAngularRadiusRadians,
    double ApronAngularRadiusRadians,
    double CoreDepositionHeightMeters,
    double ApronDepositionHeightMeters);
