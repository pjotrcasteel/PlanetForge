using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Domain.Surface;

/// <summary>
/// Fixed Eulerian area of a given generated world, independently of geological
/// time or any moving material/plate identity. Identical tile addresses denote
/// the same position even after simulated continental drift.
/// </summary>
public readonly record struct PlanetGeologicalRegionId(PlanetWorldIdentity World, PlanetTileId Tile)
{
    public static PlanetGeologicalRegionId CreateCurrent(int seed, PlanetTileId tile) =>
        new(PlanetWorldIdentity.CreateCurrent(seed), tile);

    public PlanetVector CenterDirection
    {
        get
        {
            // Use the existing cube-sphere projection: tile centres are stable
            // across process restarts, devices and geological epochs.
            var cells = (double)Tile.TilesPerAxis;
            var u = -1.0 + 2.0 * ((Tile.X + 0.5) / cells);
            var v = -1.0 + 2.0 * ((Tile.Y + 0.5) / cells);
            return CubedSphereProjection.ToUnitSphere(Tile.Face, u, v);
        }
    }

    public override string ToString() => FormattableString.Invariant(
        $"G{World.GenerationVersion.Value}:S{World.Seed}:F{(int)Tile.Face}:L{Tile.Level}:X{Tile.X}:Y{Tile.Y}");
}
