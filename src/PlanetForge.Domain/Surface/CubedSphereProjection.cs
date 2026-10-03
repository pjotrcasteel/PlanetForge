namespace PlanetForge.Domain.Surface;

public static class CubedSphereProjection
{
    public static PlanetVector ToUnitSphere(CubeFace face, double u, double v)
    {
        ValidateCoordinate(u, nameof(u));
        ValidateCoordinate(v, nameof(v));

        var cube = face switch
        {
            CubeFace.PositiveX => new PlanetVector(1.0, v, -u),
            CubeFace.NegativeX => new PlanetVector(-1.0, v, u),
            CubeFace.PositiveY => new PlanetVector(u, 1.0, -v),
            CubeFace.NegativeY => new PlanetVector(u, -1.0, v),
            CubeFace.PositiveZ => new PlanetVector(u, v, 1.0),
            CubeFace.NegativeZ => new PlanetVector(-u, v, -1.0),
            _ => throw new ArgumentOutOfRangeException(nameof(face), face, "Unknown cube face."),
        };

        return Spherify(cube);
    }

    private static PlanetVector Spherify(PlanetVector cube)
    {
        var x2 = cube.X * cube.X;
        var y2 = cube.Y * cube.Y;
        var z2 = cube.Z * cube.Z;
        var x = cube.X * Math.Sqrt(Math.Max(0.0, 1.0 - (y2 / 2.0) - (z2 / 2.0) + (y2 * z2 / 3.0)));
        var y = cube.Y * Math.Sqrt(Math.Max(0.0, 1.0 - (z2 / 2.0) - (x2 / 2.0) + (z2 * x2 / 3.0)));
        var z = cube.Z * Math.Sqrt(Math.Max(0.0, 1.0 - (x2 / 2.0) - (y2 / 2.0) + (x2 * y2 / 3.0)));
        return PlanetVector.Normalize(new PlanetVector(x, y, z));
    }

    private static void ValidateCoordinate(double coordinate, string parameterName)
    {
        if (!double.IsFinite(coordinate) || coordinate < -1.0 || coordinate > 1.0)
        {
            throw new ArgumentOutOfRangeException(parameterName, coordinate, "Cube-face coordinates must be finite and between -1 and 1.");
        }
    }
}
