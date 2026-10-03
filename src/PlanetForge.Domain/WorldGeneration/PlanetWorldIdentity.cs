namespace PlanetForge.Domain.WorldGeneration;

public readonly record struct PlanetWorldIdentity(int Seed, PlanetGenerationVersion GenerationVersion)
{
    public static PlanetWorldIdentity CreateCurrent(int seed) => new(seed, PlanetGenerationVersion.Current);
}