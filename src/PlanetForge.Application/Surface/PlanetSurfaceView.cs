using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface;

public sealed record PlanetSurfaceView(
    PlanetVector CameraDirection,
    double CameraDistanceFromCenter,
    int ViewportHeightPixels,
    double VerticalFieldOfViewRadians)
{
    public double ViewportAspectRatio { get; init; } = 1.0;
}
