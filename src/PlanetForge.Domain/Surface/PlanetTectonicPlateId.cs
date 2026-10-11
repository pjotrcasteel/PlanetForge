using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Domain.Surface;

/// <summary>
/// Persistent identity of one kinematic plate in a generated world. Plate
/// identity is independent of fixed region address and moving crust parcels.
/// </summary>
public readonly record struct PlanetTectonicPlateId
{
    public PlanetTectonicPlateId(PlanetWorldIdentity world, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        World = world;
        Index = index;
    }

    public PlanetWorldIdentity World { get; }
    public int Index { get; }

    public override string ToString() => FormattableString.Invariant(
        $"G{World.GenerationVersion.Value}:S{World.Seed}:PLATE:{Index}");
}
