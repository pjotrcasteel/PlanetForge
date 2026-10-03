namespace PlanetForge.Domain.Surface;

public static class PlanetSurfaceNavigator
{
    public static PlanetVector Move(PlanetVector anchorDirection, double eastMeters, double northMeters, double planetRadiusMeters)
    {
        if (!double.IsFinite(eastMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(eastMeters), eastMeters, "East movement must be finite.");
        }

        if (!double.IsFinite(northMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(northMeters), northMeters, "North movement must be finite.");
        }

        if (!double.IsFinite(planetRadiusMeters) || planetRadiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMeters), planetRadiusMeters, "Planet radius must be finite and greater than zero.");
        }

        var up = PlanetVector.Normalize(anchorDirection);
        var distanceMeters = Math.Sqrt((eastMeters * eastMeters) + (northMeters * northMeters));
        if (distanceMeters == 0.0)
        {
            return up;
        }

        var maximumUnambiguousDistanceMeters = Math.PI * planetRadiusMeters;
        if (distanceMeters > maximumUnambiguousDistanceMeters)
        {
            throw new ArgumentOutOfRangeException(nameof(eastMeters), distanceMeters, "A single surface move cannot exceed half the planet circumference.");
        }

        var frame = PlanetLocalFrame.Create(up, planetRadiusMeters, 0.0);
        var tangent = (frame.East * eastMeters) + (frame.North * northMeters);
        var tangentDirection = PlanetVector.Normalize(tangent);
        var angularDistance = distanceMeters / planetRadiusMeters;
        var movedDirection = (up * Math.Cos(angularDistance)) + (tangentDirection * Math.Sin(angularDistance));
        return PlanetVector.Normalize(movedDirection);
    }
}