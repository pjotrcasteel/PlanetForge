using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

public sealed record PlanetCryosphereRunoffSnapshot(
    IReadOnlyList<double> LocalAnnualMeltwaterRunoffMillimeters,
    double MeanAnnualMeltwaterRunoffMillimeters,
    double PeakAnnualMeltwaterRunoffMillimeters)
{
    public double GetCell(PlanetSurfaceGridLayout layout, PlanetSurfaceGridCellId cell)
        => LocalAnnualMeltwaterRunoffMillimeters[layout.GetIndex(cell)];
}
