using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Deterministic publication order for previously saved regional geology.
/// Physical coarser layers precede finer nested layers, with peer ordering
/// independent of persistence load order or camera traversal.
/// </summary>
public static class PlanetGeologicalRegionLayerBuilder
{
    public static PlanetRegionalGeologyOverlay[][] Build(PlanetGeologicalEpoch epoch,
        IReadOnlyList<PlanetGeologicalRegionHistory> histories)
    {
        ArgumentNullException.ThrowIfNull(histories);
        if (histories.Count == 0)
        {
            return [];
        }

        if (histories.Any(history => history is null))
        {
            throw new ArgumentException("Geological history collection cannot contain null.", nameof(histories));
        }

        var world = histories[0].Region.World;
        if (world.GenerationVersion != PlanetGenerationVersion.Current ||
            histories.Any(history => history.Epoch != epoch || history.Region.World != world))
        {
            throw new ArgumentException("Published histories must share an epoch and a current canonical planet identity.", nameof(histories));
        }

        if (histories.Select(history => history.Region).Distinct().Count() != histories.Count)
        {
            throw new ArgumentException("A geographic region may be published only once at any epoch.", nameof(histories));
        }

        // The atlas requires identical physical grid dimensions among peers.
        // Sorting peers by geographic address guarantees load-order independence.
        return histories
            .GroupBy(history => (history.Original.Width, history.Original.Height, history.Original.CellSpacingMeters))
            .OrderByDescending(group => group.Key.CellSpacingMeters)
            .ThenBy(group => group.Key.Width)
            .ThenBy(group => group.Key.Height)
            .Select(group => group.OrderBy(history => history.Region.ToString(), StringComparer.Ordinal)
                .Select(history => history.CreateOverlay()).ToArray())
            .ToArray();
    }
}
