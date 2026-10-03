using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface;

public sealed record PlanetSurfaceView(
    PlanetVector CameraDirection,
    double CameraDistanceFromCenter,
    int ViewportHeightPixels,
    double VerticalFieldOfViewRadians);
