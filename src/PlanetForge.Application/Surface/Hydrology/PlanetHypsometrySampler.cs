using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

/// <summary>
/// Captures one deterministic, area-weighted cubed-sphere terrain sample for
/// sea-level calculations. A changing geological publication is rejected, not
/// silently mixed across old and new rock elevations.
/// </summary>
public sealed class PlanetHypsometrySampler(
    IPlanetElevationSource elevationSource,
    IPlanetTerrainDeformationStore? terrainDeformationStore = null)
{
    private const int MaximumSampleGridLevel = 8;

    public PlanetHypsometry Build(int gridLevel, int seed, double radiusMeters, CancellationToken cancellationToken)
    {
        if (gridLevel is < 0 or > MaximumSampleGridLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(gridLevel), "Hypsometry grid level must be between 0 and 8.");
        }

        if (!double.IsFinite(radiusMeters) || radiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), "Planet radius must be finite and positive.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var initialGeologicalRevision = (elevationSource as IPlanetElevationRevisionSource)?.Revision ?? 0;
        var initialDeformationRevision = terrainDeformationStore?.GetRevision(seed) ?? 0;
        var layout = new PlanetSurfaceGridLayout(gridLevel);
        var samples = new PlanetElevationAreaSample[layout.CellCount];
        var areaMultiplier = radiusMeters * radiusMeters;

        for (var index = 0; index < layout.CellCount; index++)
        {
            if ((index & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var cell = layout.GetCell(index);
            var direction = PlanetSurfaceGridGeometry.GetCenterDirection(cell);
            var elevation = elevationSource.SampleElevationMeters(direction, seed);
            var area = PlanetSurfaceCellArea.SolidAngleSteradians(cell) * areaMultiplier;
            samples[index] = new PlanetElevationAreaSample(elevation, area);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (initialGeologicalRevision != ((elevationSource as IPlanetElevationRevisionSource)?.Revision ?? 0)
            || initialDeformationRevision != (terrainDeformationStore?.GetRevision(seed) ?? 0))
        {
            throw new InvalidOperationException("Geological terrain changed while sea-level hypsometry was being sampled.");
        }

        return new PlanetHypsometry(samples);
    }

}
