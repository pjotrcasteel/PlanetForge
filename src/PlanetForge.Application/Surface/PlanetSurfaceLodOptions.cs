namespace PlanetForge.Application.Surface;

public sealed record PlanetSurfaceLodOptions(
    int MaxLevel,
    double TargetTileDiameterPixels,
    double HorizonPaddingRadians)
{
    public static PlanetSurfaceLodOptions Default { get; } = new(8, 220.0, 0.02);
}
