using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Type of instantaneous relative motion at a diagnostic plate contact.
/// These are kinematic classifications, not evidence of uplift, rifting or subduction.
/// </summary>
public enum PlanetPlateBoundaryKind
{
    OutsideContact,
    Neutral,
    Convergent,
    Divergent,
    Transform,
    ObliqueConvergent,
    ObliqueDivergent
}

/// <summary>
/// Signed normal compression (positive = convergence), signed lateral slip and
/// relative speed at one geographic position, in metres per million years.
/// Contact influence is a bounded diagnostic envelope, not a force or a height delta.
/// </summary>
public readonly record struct PlanetPlateBoundarySample(
    PlanetTectonicPlateId FirstPlate,
    PlanetTectonicPlateId SecondPlate,
    PlanetPlateBoundaryKind Kind,
    double AngularDistanceRadians,
    double ContactInfluence,
    double CompressionMetersPerMillionYears,
    double ShearMetersPerMillionYears,
    double RelativeSpeedMetersPerMillionYears)
{
    public bool IsAtContact => ContactInfluence > 0.0;
}

/// <summary>
/// Evaluates the two nearest moving canonical plate centres on a sphere.
/// The centre Voronoi contact is a diagnostic proxy: true crust polygons,
/// permanent material parcels and physical stress/force solvers come later.
/// No renderer, canonical elevation or world save is modified by this class.
/// </summary>
public sealed class PlanetTectonicBoundaryKinematics
{
    // Research-only contact envelope: 0–0.035 radians (~223 km on Earth radius).
    // These assumed widths must not be treated as measured plate-boundary geology.
    public const double ContactOuterWidthRadians = 0.035;
    public const double ContactInnerWidthRadians = 0.010;
    private const double MinimumSignificantMotionMetersPerMillionYears = 500.0;

    private readonly PlanetTectonicPlateKinematics plates = new();

    /// <summary>
    /// Samples the two nearest centres at one epoch without allocating a global plate grid.
    /// This method is for inspection and verification, not per-vertex terrain evaluation.
    /// </summary>
    public PlanetPlateBoundarySample Sample(PlanetVector geographicDirection, int seed, PlanetGeologicalEpoch epoch)
    {
        var position = PlanetVector.Normalize(geographicDirection);
        PlanetTectonicPlateState? nearest = null;
        PlanetTectonicPlateState? second = null;
        var bestDot = double.NegativeInfinity;
        var nextDot = double.NegativeInfinity;

        foreach (var plate in plates.GetPlates(seed))
        {
            var score = PlanetVector.Dot(position, plate.CenterAt(epoch));
            if (score > bestDot)
            {
                (second, nextDot) = (nearest, bestDot);
                (nearest, bestDot) = (plate, score);
            }
            else if (score > nextDot)
            {
                (second, nextDot) = (plate, score);
            }
        }

        return Evaluate(position, nearest!, second!, epoch);
    }

    /// <summary>
    /// Evaluate an explicit pair for physics tests and future persistent contact topology.
    /// Callers must supply two different plates from the same generated world.
    /// </summary>
    public static PlanetPlateBoundarySample Evaluate(
        PlanetVector geographicDirection,
        PlanetTectonicPlateState first,
        PlanetTectonicPlateState second,
        PlanetGeologicalEpoch epoch)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Id.World != second.Id.World || first.Id == second.Id)
        {
            throw new ArgumentException("A contact requires two distinct plates of the same generated world.");
        }

        if (first.Id.Index > second.Id.Index)
        {
            (first, second) = (second, first);
        }

        var position = PlanetVector.Normalize(geographicDirection);
        var difference = first.CenterAt(epoch) - second.CenterAt(epoch);
        // Coincident plate centres do not define a unique contact normal.
        if (difference.Length <= 1e-10)
        {
            return new PlanetPlateBoundarySample(first.Id, second.Id, PlanetPlateBoundaryKind.OutsideContact,
                Math.PI / 2.0, 0.0, 0.0, 0.0, 0.0);
        }

        var globalNormal = PlanetVector.Normalize(difference);
        // Equal angular distance to both centres forms a great circle.
        var signedDistance = Math.Clamp(PlanetVector.Dot(position, globalNormal), -1.0, 1.0);
        var distance = Math.Abs(Math.Asin(signedDistance));
        var influence = ContactInfluence(distance);
        if (influence <= 0.0)
        {
            return new PlanetPlateBoundarySample(first.Id, second.Id, PlanetPlateBoundaryKind.OutsideContact,
                distance, 0.0, 0.0, 0.0, 0.0);
        }

        // Project the B->A separator into the local tangent plane. Keeping the
        // ID order stable makes signed shear/compression identical on either side.
        var normalVector = globalNormal - position * signedDistance;
        var normal = PlanetVector.Normalize(normalVector);
        var shearDirection = PlanetVector.Normalize(PlanetVector.Cross(position, normal));
        var relative = VelocityAt(first, position) - VelocityAt(second, position);
        var compression = -PlanetVector.Dot(relative, normal);
        var shear = PlanetVector.Dot(relative, shearDirection);
        var speed = relative.Length;

        return new PlanetPlateBoundarySample(first.Id, second.Id, Classify(compression, shear),
            distance, influence, compression, shear, speed);
    }

    private static PlanetVector VelocityAt(PlanetTectonicPlateState plate, PlanetVector position)
    {
        // Rotational velocity applies at the contact, not just at the plate centre.
        var angularSpeed = plate.AngularVelocityRadiansPerMillionYears;
        if (!double.IsFinite(angularSpeed) || angularSpeed < 0.0)
        {
            throw new InvalidOperationException("Plate angular velocity must be finite and non-negative.");
        }

        return PlanetVector.Cross(plate.EulerPole, position) *
            (angularSpeed * PlanetTectonicPlateKinematics.NominalPlanetRadiusMeters);
    }

    private static double ContactInfluence(double angularDistance)
    {
        var t = Math.Clamp((angularDistance - ContactInnerWidthRadians) /
            (ContactOuterWidthRadians - ContactInnerWidthRadians), 0.0, 1.0);
        return 1.0 - t * t * (3.0 - 2.0 * t);
    }

    private static PlanetPlateBoundaryKind Classify(double compression, double shear)
    {
        var normal = Math.Abs(compression);
        var lateral = Math.Abs(shear);
        if (normal < MinimumSignificantMotionMetersPerMillionYears &&
            lateral < MinimumSignificantMotionMetersPerMillionYears)
        {
            return PlanetPlateBoundaryKind.Neutral;
        }

        if (normal < lateral * 0.35)
        {
            return PlanetPlateBoundaryKind.Transform;
        }

        var oblique = lateral >= normal * 0.5;
        return compression >= 0.0
            ? (oblique ? PlanetPlateBoundaryKind.ObliqueConvergent : PlanetPlateBoundaryKind.Convergent)
            : (oblique ? PlanetPlateBoundaryKind.ObliqueDivergent : PlanetPlateBoundaryKind.Divergent);
    }
}
