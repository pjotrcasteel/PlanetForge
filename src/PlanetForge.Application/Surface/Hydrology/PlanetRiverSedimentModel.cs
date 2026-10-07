using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

public sealed class PlanetRiverSedimentModel
{
    private const double MaximumAlluvialHeightAboveSeaLevelMeters = 900.0;

    public IReadOnlyList<PlanetTerrainDeposition> Build(
        IReadOnlyList<PlanetRiverSegment> riverSegments,
        int simulatedYears,
        double planetRadiusMeters,
        double seaLevelMeters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(riverSegments);

        if (!double.IsFinite(planetRadiusMeters) || planetRadiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMeters), planetRadiusMeters, "Planet radius must be finite and greater than zero.");
        }

        if (!double.IsFinite(seaLevelMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(seaLevelMeters), seaLevelMeters, "Sea level must be finite.");
        }

        if (simulatedYears <= 0 || riverSegments.Count == 0)
        {
            return [];
        }

        var maximumDischarge = Math.Max(1.0, riverSegments.Max(segment => segment.MeanDischargeCubicMetersPerSecond));
        var maturity = 1.0 - Math.Exp(-simulatedYears / 180.0);
        var result = new List<PlanetTerrainDeposition>();

        for (var index = 0; index < riverSegments.Count; index++)
        {
            CheckCancellation(index, cancellationToken);
            var segment = riverSegments[index];
            var segmentLengthMeters = CalculateSegmentLengthMeters(segment, planetRadiusMeters);
            var slope = Math.Max(0.0, segment.FromElevationMeters - segment.ToElevationMeters) / segmentLengthMeters;
            var lowSlopeResponse = 1.0 - SmoothStep(0.0015, 0.012, slope);
            var relativeDischarge = Math.Clamp(Math.Sqrt(segment.MeanDischargeCubicMetersPerSecond / maximumDischarge), 0.0, 1.0);
            var streamOrderResponse = Math.Clamp((segment.StrahlerOrder - 1) / 4.0, 0.0, 1.0);
            var reachesOcean = segment.FromElevationMeters > seaLevelMeters && segment.ToElevationMeters <= seaLevelMeters;
            var elevationAboveSeaLevel = segment.ToElevationMeters - seaLevelMeters;
            var alluvialResponse = reachesOcean
                ? 1.0
                : (1.0 - SmoothStep(120.0, MaximumAlluvialHeightAboveSeaLevelMeters, Math.Max(0.0, elevationAboveSeaLevel))) * lowSlopeResponse;
            var depositionPotential = Math.Clamp(
                alluvialResponse * (0.34 + (0.66 * lowSlopeResponse)) * (0.42 + (0.58 * relativeDischarge)),
                0.0,
                1.0);

            if (depositionPotential < 0.08)
            {
                continue;
            }

            var coreHeightMeters = Math.Clamp(
                (8.0 + (72.0 * Math.Pow(relativeDischarge, 0.72)) + (8.0 * streamOrderResponse))
                * maturity
                * depositionPotential,
                2.0,
                120.0);
            var apronHeightMeters = coreHeightMeters * (0.18 + (0.16 * lowSlopeResponse));
            var coreRadiusMeters = Math.Clamp(
                3_000.0 + (13_000.0 * Math.Pow(relativeDischarge, 0.52)) + (3_000.0 * streamOrderResponse),
                2_500.0,
                28_000.0);
            var apronRadiusMeters = Math.Clamp(
                18_000.0 + (62_000.0 * Math.Pow(relativeDischarge, 0.58)) + (12_000.0 * streamOrderResponse),
                15_000.0,
                115_000.0);

            if (reachesOcean)
            {
                AddDeltaFan(result, segment, seaLevelMeters, planetRadiusMeters, coreRadiusMeters, apronRadiusMeters, coreHeightMeters, apronHeightMeters);
            }
            else
            {
                result.Add(CreateDeposition(
                    segment.ToDirection,
                    planetRadiusMeters,
                    coreRadiusMeters * 0.72,
                    apronRadiusMeters * 0.68,
                    coreHeightMeters * 0.48,
                    apronHeightMeters * 0.55));
            }
        }

        return result;
    }

    private static void AddDeltaFan(
        List<PlanetTerrainDeposition> result,
        PlanetRiverSegment segment,
        double seaLevelMeters,
        double planetRadiusMeters,
        double coreRadiusMeters,
        double apronRadiusMeters,
        double coreHeightMeters,
        double apronHeightMeters)
    {
        var denominator = segment.FromElevationMeters - segment.ToElevationMeters;
        var crossingAmount = denominator <= 0.0
            ? 1.0
            : Math.Clamp((segment.FromElevationMeters - seaLevelMeters) / denominator, 0.0, 1.0);
        var mouth = SphericalInterpolate(segment.FromDirection, segment.ToDirection, crossingAmount);
        var targetAngle = Math.Acos(Math.Clamp(PlanetVector.Dot(mouth, segment.ToDirection), -1.0, 1.0));
        var seawardAngle = Math.Min(apronRadiusMeters / planetRadiusMeters * 0.30, targetAngle * 0.45);
        var seaward = MoveToward(mouth, segment.ToDirection, seawardAngle);
        var flowTangent = TangentToward(mouth, segment.ToDirection);
        var lateral = PlanetVector.Normalize(PlanetVector.Cross(mouth, flowTangent));
        var lobeOffset = apronRadiusMeters / planetRadiusMeters * 0.24;

        result.Add(CreateDeposition(
            seaward,
            planetRadiusMeters,
            coreRadiusMeters,
            apronRadiusMeters,
            coreHeightMeters,
            apronHeightMeters));
        result.Add(CreateDeposition(
            OffsetDirection(seaward, lateral, lobeOffset),
            planetRadiusMeters,
            coreRadiusMeters * 0.74,
            apronRadiusMeters * 0.72,
            coreHeightMeters * 0.62,
            apronHeightMeters * 0.70));
        result.Add(CreateDeposition(
            OffsetDirection(seaward, lateral, -lobeOffset),
            planetRadiusMeters,
            coreRadiusMeters * 0.74,
            apronRadiusMeters * 0.72,
            coreHeightMeters * 0.62,
            apronHeightMeters * 0.70));
    }

    private static PlanetTerrainDeposition CreateDeposition(
        PlanetVector direction,
        double planetRadiusMeters,
        double coreRadiusMeters,
        double apronRadiusMeters,
        double coreHeightMeters,
        double apronHeightMeters)
        => new(
            direction,
            coreRadiusMeters / planetRadiusMeters,
            apronRadiusMeters / planetRadiusMeters,
            coreHeightMeters,
            apronHeightMeters);

    private static double CalculateSegmentLengthMeters(PlanetRiverSegment segment, double planetRadiusMeters)
    {
        var angularLength = Math.Acos(Math.Clamp(PlanetVector.Dot(segment.FromDirection, segment.ToDirection), -1.0, 1.0));
        return Math.Max(planetRadiusMeters * angularLength, 1.0);
    }

    private static PlanetVector MoveToward(PlanetVector from, PlanetVector to, double angularDistance)
    {
        var angle = Math.Acos(Math.Clamp(PlanetVector.Dot(from, to), -1.0, 1.0));
        if (angle <= 0.000001 || angularDistance <= 0.0)
        {
            return from;
        }

        return SphericalInterpolate(from, to, Math.Clamp(angularDistance / angle, 0.0, 1.0));
    }

    private static PlanetVector TangentToward(PlanetVector from, PlanetVector to)
    {
        var projected = to - (from * PlanetVector.Dot(to, from));
        if (projected.Length <= 0.000001)
        {
            var reference = Math.Abs(from.Y) < 0.9 ? PlanetVector.UnitY : PlanetVector.UnitX;
            projected = PlanetVector.Cross(reference, from);
        }

        return PlanetVector.Normalize(projected);
    }

    private static PlanetVector OffsetDirection(PlanetVector direction, PlanetVector tangent, double angle)
        => PlanetVector.Normalize((direction * Math.Cos(angle)) + (tangent * Math.Sin(angle)));

    private static PlanetVector SphericalInterpolate(PlanetVector from, PlanetVector to, double amount)
    {
        var cosine = Math.Clamp(PlanetVector.Dot(from, to), -1.0, 1.0);
        var angle = Math.Acos(cosine);
        if (angle < 0.000001)
        {
            return from;
        }

        var sine = Math.Sin(angle);
        var fromWeight = Math.Sin((1.0 - amount) * angle) / sine;
        var toWeight = Math.Sin(amount * angle) / sine;
        return PlanetVector.Normalize((from * fromWeight) + (to * toWeight));
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var amount = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
        return amount * amount * (3.0 - (2.0 * amount));
    }

    private static void CheckCancellation(int iteration, CancellationToken cancellationToken)
    {
        if ((iteration & 255) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
