using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Domain.Persistence;

public sealed record PlanetSaveHeader(PlanetSaveSchemaVersion SchemaVersion, PlanetWorldIdentity World)
{
    public static PlanetSaveHeader CreateCurrent(int seed) => new(PlanetSaveSchemaVersion.Current, PlanetWorldIdentity.CreateCurrent(seed));
}