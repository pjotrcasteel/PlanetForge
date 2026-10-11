namespace PlanetForge.Domain.Surface;

public readonly record struct PlanetVector(double X, double Y, double Z)
{
    public static PlanetVector UnitX { get; } = new(1.0, 0.0, 0.0);

    public static PlanetVector UnitY { get; } = new(0.0, 1.0, 0.0);

    public static PlanetVector UnitZ { get; } = new(0.0, 0.0, 1.0);

    public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

    public static PlanetVector Normalize(PlanetVector value)
    {
        var length = value.Length;
        if (!double.IsFinite(length) || length <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Vector must have a finite non-zero length.");
        }

        return value / length;
    }

    public static double Dot(PlanetVector left, PlanetVector right) => (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    public static PlanetVector Cross(PlanetVector left, PlanetVector right) => new(
        (left.Y * right.Z) - (left.Z * right.Y),
        (left.Z * right.X) - (left.X * right.Z),
        (left.X * right.Y) - (left.Y * right.X));

    public static PlanetVector operator +(PlanetVector left, PlanetVector right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static PlanetVector operator -(PlanetVector left, PlanetVector right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static PlanetVector operator *(PlanetVector value, double scalar) => new(value.X * scalar, value.Y * scalar, value.Z * scalar);

    public static PlanetVector operator /(PlanetVector value, double scalar) => new(value.X / scalar, value.Y / scalar, value.Z / scalar);
}
