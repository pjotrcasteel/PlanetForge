using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Deterministic spherical plate motion about an Euler pole. These are
/// kinematic reference frames, not a deformation/erosion simulation: moved
/// plate centres alone do not reconstruct rifts, mountain uplift or subduction.
/// </summary>
public sealed class PlanetTectonicPlateKinematics
{
    // 60 km per Myr is 6 cm/yr. Scale by the canonical relative speed
    // (0.35..1.0) to obtain 2.1..6 cm/yr as an initial model assumption.
    public const double NominalPlanetRadiusMeters = 6_371_000.0;
    public const double MaximumPlateTravelMetersPerMillionYears = 60_000.0;

    public int PlateCount => ProceduralPlanetElevationSource.PresentPlateCount;

    public PlanetTectonicPlateState GetPlate(int seed, int index)
    {
        var center = ProceduralPlanetElevationSource.GetPresentPlateCenter(seed, index);
        var motion = ProceduralPlanetElevationSource.GetPresentPlateMotion(seed, index);
        var tangent = motion / motion.Length;
        // Cross(center, tangent) is the Euler pole that reproduces the
        // existing canonical instantaneous plate motion at the present epoch.
        var pole = PlanetVector.Normalize(PlanetVector.Cross(center, tangent));
        var velocity = motion.Length * MaximumPlateTravelMetersPerMillionYears;
        var rate = velocity / NominalPlanetRadiusMeters;

        return new PlanetTectonicPlateState(
            new PlanetTectonicPlateId(PlanetWorldIdentity.CreateCurrent(seed), index),
            center, pole, rate, velocity);
    }

    public IReadOnlyList<PlanetTectonicPlateState> GetPlates(int seed)
    {
        return Enumerable.Range(0, PlateCount).Select(index => GetPlate(seed, index)).ToArray();
    }

    /// <summary>
    /// Diagnostic nearest moving plate-centre, not an advected crust polygon
    /// or a dynamically conserved plate ownership map.
    /// </summary>
    public PlanetTectonicPlateId FindNearestPlate(PlanetVector direction, int seed, PlanetGeologicalEpoch epoch)
    {
        var normalized = PlanetVector.Normalize(direction);
        var index = -1;
        var closest = double.NegativeInfinity;
        for (var candidate = 0; candidate < PlateCount; candidate++)
        {
            var plate = GetPlate(seed, candidate);
            var score = PlanetVector.Dot(normalized, plate.CenterAt(epoch));
            if (score > closest)
            {
                closest = score;
                index = candidate;
            }
        }

        return new PlanetTectonicPlateId(PlanetWorldIdentity.CreateCurrent(seed), index);
    }
}

public sealed record PlanetTectonicPlateState(
    PlanetTectonicPlateId Id,
    PlanetVector CenterAtPresent,
    PlanetVector EulerPole,
    double AngularVelocityRadiansPerMillionYears,
    double SpeedMetersPerMillionYears)
{
    public PlanetVector CenterAt(PlanetGeologicalEpoch epoch) => Move(CenterAtPresent, epoch);

    /// <summary>Moves material at present-day direction back to an older epoch.</summary>
    public PlanetVector Move(PlanetVector presentDirection, PlanetGeologicalEpoch epoch) =>
        Rotate(presentDirection, -AngularVelocityRadiansPerMillionYears * (epoch.YearsBeforePresent / 1_000_000.0));

    /// <summary>Inverse operation: a material direction at a historical epoch mapped to today.</summary>
    public PlanetVector MapToPresent(PlanetVector pastDirection, PlanetGeologicalEpoch epoch) =>
        Rotate(pastDirection, AngularVelocityRadiansPerMillionYears * (epoch.YearsBeforePresent / 1_000_000.0));

    private PlanetVector Rotate(PlanetVector direction, double angleRadians)
    {
        var normalized = PlanetVector.Normalize(direction);
        var pole = PlanetVector.Normalize(EulerPole);
        if (!double.IsFinite(AngularVelocityRadiansPerMillionYears) || AngularVelocityRadiansPerMillionYears < 0.0 ||
            !double.IsFinite(SpeedMetersPerMillionYears) || SpeedMetersPerMillionYears < 0.0)
        {
            throw new InvalidOperationException("Plate motion parameters must be finite and non-negative.");
        }

        // Reducing angle before trig also keeps very old geological epochs
        // numerically stable and preserves the sphere's unit radius.
        var angle = Math.IEEERemainder(angleRadians, Math.Tau);
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        return PlanetVector.Normalize(normalized * cos +
            PlanetVector.Cross(pole, normalized) * sin +
            pole * (PlanetVector.Dot(pole, normalized) * (1.0 - cos)));
    }
}
