namespace PlanetForge.Domain.Surface;

public readonly record struct PlanetVector(double X, double Y, double Z)
{
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

    public static PlanetVector operator +(PlanetVector left, PlanetVector right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static PlanetVector operator -(PlanetVector left, PlanetVector right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static PlanetVector operator *(PlanetVector value, double scalar) => new(value.X * scalar, value.Y * scalar, value.Z * scalar);

    public static PlanetVector operator /(PlanetVector value, double scalar) => new(value.X / scalar, value.Y / scalar, value.Z / scalar);
}
