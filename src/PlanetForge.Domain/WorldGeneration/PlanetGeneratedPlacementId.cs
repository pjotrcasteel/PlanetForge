namespace PlanetForge.Domain.WorldGeneration;

public readonly record struct PlanetGeneratedPlacementId(string Value)
{
    public override string ToString() => Value;
}