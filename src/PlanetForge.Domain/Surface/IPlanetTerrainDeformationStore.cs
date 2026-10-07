namespace PlanetForge.Domain.Surface;

public interface IPlanetTerrainDeformationStore
{
    double SampleElevationDeltaMeters(PlanetVector direction, int seed);

    int GetRevision(int seed);

    void Apply(int seed, IReadOnlyList<PlanetTerrainDeformation> deformations);

    void Clear(int seed);

    void ClearAll();
}
