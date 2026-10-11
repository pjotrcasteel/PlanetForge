namespace PlanetForge.Domain.Surface;

/// <summary>
/// Stable local material identity within a generated world. Use with the
/// world identity to form its complete key. Material moves between fixed
/// geographic regions without receiving another ID; it is not a plate ID.
/// </summary>
public readonly record struct PlanetCrustParcelId
{
    public PlanetCrustParcelId(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    public override string ToString() => FormattableString.Invariant($"CRUST:{Value}");
}
