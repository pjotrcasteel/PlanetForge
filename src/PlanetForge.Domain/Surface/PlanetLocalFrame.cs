namespace PlanetForge.Domain.Surface;

public readonly record struct PlanetLocalFrame(PlanetVector OriginMeters, PlanetVector East, PlanetVector North, PlanetVector Up)
{
    public static PlanetLocalFrame Create(PlanetVector surfaceDirection, double planetRadiusMeters, double elevationMeters)
    {
        if (!double.IsFinite(planetRadiusMeters) || planetRadiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMeters), planetRadiusMeters, "Planet radius must be finite and greater than zero.");
        }

        if (!double.IsFinite(elevationMeters) || planetRadiusMeters + elevationMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(elevationMeters), elevationMeters, "Elevation must keep the local frame above the planet center.");
        }

        var up = PlanetVector.Normalize(surfaceDirection);
        var reference = Math.Abs(PlanetVector.Dot(up, PlanetVector.UnitY)) < 0.99 ? PlanetVector.UnitY : PlanetVector.UnitX;
        var east = PlanetVector.Normalize(PlanetVector.Cross(reference, up));
        var north = PlanetVector.Normalize(PlanetVector.Cross(up, east));
        var originMeters = up * (planetRadiusMeters + elevationMeters);
        return new PlanetLocalFrame(originMeters, east, north, up);
    }

    public PlanetLocalPosition ToLocal(PlanetVector worldPositionMeters)
    {
        var delta = worldPositionMeters - OriginMeters;
        return new PlanetLocalPosition(
            PlanetVector.Dot(delta, East),
            PlanetVector.Dot(delta, North),
            PlanetVector.Dot(delta, Up));
    }

    public PlanetVector ToWorld(PlanetLocalPosition localPosition) =>
        OriginMeters +
        (East * localPosition.EastMeters) +
        (North * localPosition.NorthMeters) +
        (Up * localPosition.UpMeters);
}
