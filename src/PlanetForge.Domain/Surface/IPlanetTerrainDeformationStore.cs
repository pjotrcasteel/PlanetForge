namespace PlanetForge.Domain.Surface;

public interface IPlanetTerrainDeformationStore
{
    double SampleElevationDeltaMeters(PlanetVector direction, int seed);

    int GetRevision(int seed);

    void Apply(int seed, IReadOnlyList<PlanetTerrainDeformation> deformations);

    void ApplyDeposition(int seed, IReadOnlyList<PlanetTerrainDeposition> depositions);

    PlanetTerrainEvolutionState Export(int seed);

    void Restore(int seed, PlanetTerrainEvolutionState state);

    void Clear(int seed);

    void ClearAll();
}
