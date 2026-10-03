namespace PlanetForge.Domain.Persistence;

public readonly record struct PlanetSaveSchemaVersion
{
    public static PlanetSaveSchemaVersion Current { get; } = new(1);

    public PlanetSaveSchemaVersion(int value)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Save schema version must be greater than zero.");
        }

        Value = value;
    }

    public int Value { get; }
}