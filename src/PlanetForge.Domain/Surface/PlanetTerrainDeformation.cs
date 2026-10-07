namespace PlanetForge.Domain.Surface;

public sealed record PlanetTerrainDeformation(
    PlanetVector CenterDirection,
    double ChannelAngularRadiusRadians,
    double ValleyAngularRadiusRadians,
    double ChannelIncisionDepthMeters,
    double ValleyIncisionDepthMeters);
