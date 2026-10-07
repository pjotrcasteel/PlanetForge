using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

public sealed class PlanetRiverGeomorphologyModel
{
    private const double TargetStampSpacingMeters = 20_000.0;
    private const int MaximumStampsPerSegment = 48;

    public IReadOnlyList<PlanetTerrainDeformation> Build(
        IReadOnlyList<PlanetRiverSegment> riverSegments,
        int simulatedYears,
        double planetRadiusMeters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(riverSegments);

        if (!double.IsFinite(planetRadiusMeters) || planetRadiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMeters), planetRadiusMeters, "Planet radius must be finite and greater than zero.");
        }

        if (simulatedYears <= 0 || riverSegments.Count == 0)
        {
            return [];
        }

        var maximumDischarge = Math.Max(1.0, riverSegments.Max(segment => segment.MeanDischargeCubicMetersPerSecond));
        var maturity = 1.0 - Math.Exp(-simulatedYears / 90.0);
        var result = new List<PlanetTerrainDeformation>();

        for (var segmentIndex = 0; segmentIndex < riverSegments.Count; segmentIndex++)
        {
            CheckCancellation(segmentIndex, cancellationToken);
            var segment = riverSegments[segmentIndex];
            var angularLength = Math.Acos(Math.Clamp(PlanetVector.Dot(segment.FromDirection, segment.ToDirection), -1.0, 1.0));
            var segmentLengthMeters = Math.Max(planetRadiusMeters * angularLength, 1.0);
            var physicalSlope = Math.Max(0.0, segment.FromElevationMeters - segment.ToElevationMeters) / segmentLengthMeters;
            var relativeDischarge = Math.Clamp(Math.Sqrt(segment.MeanDischargeCubicMetersPerSecond / maximumDischarge), 0.0, 1.0);
            var slopeResponse = Math.Clamp(Math.Sqrt(Math.Max(physicalSlope, 0.00005) / 0.002), 0.55, 1.60);
            var floodplainResponse = 1.0 - Math.Clamp(physicalSlope / 0.015, 0.0, 1.0);

            var channelDepthMeters = Math.Clamp(
                (12.0 + (95.0 * Math.Pow(relativeDischarge, 0.70)) + (7.0 * Math.Max(0, segment.StrahlerOrder - 1)))
                * maturity
                * slopeResponse,
                4.0,
                180.0);
            var valleyDepthMeters = channelDepthMeters * (0.18 + (0.20 * floodplainResponse));
            var channelRadiusMeters = Math.Clamp(
                600.0 + (2_400.0 * Math.Pow(relativeDischarge, 0.45)) + (250.0 * Math.Max(0, segment.StrahlerOrder - 1)),
                500.0,
                5_000.0);
            var valleyRadiusMeters = Math.Clamp(
                (7_000.0 + (26_000.0 * Math.Pow(relativeDischarge, 0.55)) + (2_500.0 * Math.Max(0, segment.StrahlerOrder - 1)))
                * (1.0 + (0.50 * floodplainResponse)),
                6_000.0,
                55_000.0);

            var stampCount = Math.Clamp((int)Math.Ceiling(segmentLengthMeters / TargetStampSpacingMeters) + 1, 2, MaximumStampsPerSegment);
            for (var stampIndex = 0; stampIndex < stampCount; stampIndex++)
            {
                var amount = stampCount == 1 ? 0.0 : stampIndex / (double)(stampCount - 1);
                var direction = SphericalInterpolate(segment.FromDirection, segment.ToDirection, amount);
                result.Add(new PlanetTerrainDeformation(
                    direction,
                    channelRadiusMeters / planetRadiusMeters,
                    valleyRadiusMeters / planetRadiusMeters,
                    channelDepthMeters,
                    valleyDepthMeters));
            }
        }

        return result;
    }

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

    private static void CheckCancellation(int iteration, CancellationToken cancellationToken)
    {
        if ((iteration & 255) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
