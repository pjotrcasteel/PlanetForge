using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Domain.Persistence;

public readonly record struct PlanetEntityId
{
    public PlanetEntityId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Entity ID is required.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public static PlanetEntityId FromGeneratedPlacement(PlanetGeneratedPlacementId placementId) => new($"G-{placementId.Value}");

    public override string ToString() => Value;
}