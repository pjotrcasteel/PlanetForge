namespace PlanetForge.Domain.Surface;

public readonly record struct PlanetSurfaceAddress(int PrecisionBits, uint X, uint Y)
{
    public override string ToString() => $"O{PrecisionBits}:{X}:{Y}";
}