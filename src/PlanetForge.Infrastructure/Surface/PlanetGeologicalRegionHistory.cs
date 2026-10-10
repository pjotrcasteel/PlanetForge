using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// One replayable physical geological state at a fixed geographic tile and
/// explicitly labelled epoch. The epoch is metadata; erosion iterations are
/// not claimed to represent elapsed physical years.
/// </summary>
public sealed class PlanetGeologicalRegionHistory
{
    private readonly PlanetRegionalGeologySnapshot original;
    private readonly PlanetRegionalGeologySnapshot evolved;

    private PlanetGeologicalRegionHistory(PlanetGeologicalRegionId region, PlanetGeologicalEpoch epoch,
        double planetRadiusMeters, PlanetRegionalGeologySnapshot original, PlanetRegionalGeologySnapshot evolved)
    {
        Region = region;
        Epoch = epoch;
        PlanetRadiusMeters = planetRadiusMeters;
        this.original = original;
        this.evolved = evolved;
    }

    public PlanetGeologicalRegionId Region { get; }
    public PlanetGeologicalEpoch Epoch { get; }
    public double PlanetRadiusMeters { get; }

    // Snapshot arrays are mutable because they also feed the existing erosion
    // solver. Never expose the archived copy to consumers or callers.
    public PlanetRegionalGeologySnapshot Original => PlanetRegionalGeologyEvolution.Restore(original);
    public PlanetRegionalGeologySnapshot Evolved => PlanetRegionalGeologyEvolution.Restore(evolved);

    public static PlanetGeologicalRegionHistory Create(PlanetGeologicalRegionId region, PlanetGeologicalEpoch epoch,
        double planetRadiusMeters, PlanetRegionalGeologySnapshot original, PlanetRegionalGeologySnapshot evolved)
    {
        if (region.World.GenerationVersion.Value < 1 || !Enum.IsDefined(region.Tile.Face))
        {
            throw new ArgumentException("Invalid generated world or geographic tile identity.", nameof(region));
        }

        if (!double.IsFinite(planetRadiusMeters) || planetRadiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMeters));
        }

        var before = PlanetRegionalGeologyEvolution.Restore(original);
        var after = PlanetRegionalGeologyEvolution.Restore(evolved);
        if (before.RegionKey != region.ToString() || after.RegionKey != before.RegionKey ||
            before.Seed != region.World.Seed || after.Seed != before.Seed ||
            before.Width != after.Width || before.Height != after.Height ||
            before.CellSpacingMeters != after.CellSpacingMeters || before.Iteration != 0 ||
            after.Iteration < before.Iteration)
        {
            throw new ArgumentException("Region snapshots must use the same fixed tile, seed and physical grid, starting at iteration zero.");
        }

        return new PlanetGeologicalRegionHistory(region, epoch, planetRadiusMeters, before, after);
    }

    public PlanetGeologicalRegionHistory Copy() => Create(Region, Epoch, PlanetRadiusMeters, original, evolved);

    public PlanetRegionalGeologyOverlay CreateOverlay() =>
        PlanetRegionalGeologyOverlay.Create(original, evolved, Region.CenterDirection, PlanetRadiusMeters);
}
