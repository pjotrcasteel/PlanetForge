namespace PlanetForge.Domain.Surface;

public static class PlanetSurfaceAddressing
{
    public const int DefaultPrecisionBits = 30;
    private const int MinimumPrecisionBits = 8;
    private const int MaximumPrecisionBits = 30;

    public static PlanetSurfaceAddress Encode(PlanetVector direction, int precisionBits = DefaultPrecisionBits)
    {
        ValidatePrecision(precisionBits);
        var normalized = PlanetVector.Normalize(direction);
        var inverseL1Norm = 1.0 / (Math.Abs(normalized.X) + Math.Abs(normalized.Y) + Math.Abs(normalized.Z));
        var x = normalized.X * inverseL1Norm;
        var y = normalized.Y * inverseL1Norm;
        var z = normalized.Z * inverseL1Norm;

        if (z < 0.0)
        {
            var originalX = x;
            var originalY = y;
            x = (1.0 - Math.Abs(originalY)) * SignNotZero(originalX);
            y = (1.0 - Math.Abs(originalX)) * SignNotZero(originalY);
        }

        var maximum = (1u << precisionBits) - 1u;
        return new PlanetSurfaceAddress(precisionBits, Quantize(x, maximum), Quantize(y, maximum));
    }

    public static PlanetVector Decode(PlanetSurfaceAddress address)
    {
        ValidatePrecision(address.PrecisionBits);
        var maximum = (1u << address.PrecisionBits) - 1u;
        if (address.X > maximum || address.Y > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(address), address, "Surface address coordinates exceed their declared precision.");
        }

        var x = ((address.X / (double)maximum) * 2.0) - 1.0;
        var y = ((address.Y / (double)maximum) * 2.0) - 1.0;
        var z = 1.0 - Math.Abs(x) - Math.Abs(y);

        if (z < 0.0)
        {
            var originalX = x;
            var originalY = y;
            x = (1.0 - Math.Abs(originalY)) * SignNotZero(originalX);
            y = (1.0 - Math.Abs(originalX)) * SignNotZero(originalY);
        }

        return PlanetVector.Normalize(new PlanetVector(x, y, z));
    }

    private static uint Quantize(double value, uint maximum)
    {
        var normalized = Math.Clamp((value + 1.0) * 0.5, 0.0, 1.0);
        return (uint)Math.Round(normalized * maximum, MidpointRounding.AwayFromZero);
    }

    private static double SignNotZero(double value) => value < 0.0 ? -1.0 : 1.0;

    private static void ValidatePrecision(int precisionBits)
    {
        if (precisionBits < MinimumPrecisionBits || precisionBits > MaximumPrecisionBits)
        {
            throw new ArgumentOutOfRangeException(nameof(precisionBits), precisionBits, $"Surface address precision must be between {MinimumPrecisionBits} and {MaximumPrecisionBits} bits.");
        }
    }
}