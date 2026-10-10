namespace PlanetForge.Domain.Surface;

/// <summary>
/// Future A2.5 material identity. A parcel moves with crust and can cross
/// fixed geological regions without its identity changing. A parcel is not a
/// continent, and is not necessarily the same as a tectonic plate.
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
