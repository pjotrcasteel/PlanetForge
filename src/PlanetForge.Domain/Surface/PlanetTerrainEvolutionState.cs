namespace PlanetForge.Domain.Surface;

public sealed record PlanetTerrainEvolutionState(
    int Revision,
    IReadOnlyList<PlanetTerrainDeformation> Erosions,
    IReadOnlyList<PlanetTerrainDeposition> Depositions)
{
    public static PlanetTerrainEvolutionState Empty { get; } = new(0, [], []);
}
