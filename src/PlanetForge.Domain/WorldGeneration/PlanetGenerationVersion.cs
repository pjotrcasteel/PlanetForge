namespace PlanetForge.Domain.WorldGeneration;

public readonly record struct PlanetGenerationVersion
{
    public static PlanetGenerationVersion Current { get; } = new(10);

    public PlanetGenerationVersion(int value)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Generation version must be greater than zero.");
        }

        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}