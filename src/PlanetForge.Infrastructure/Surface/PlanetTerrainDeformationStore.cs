using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

public sealed class PlanetTerrainDeformationStore : IPlanetTerrainDeformationStore
{
    private const int LatitudeBinCount = 90;
    private const int LongitudeBinCount = 180;
    private readonly object sync = new();
    private readonly Dictionary<int, TerrainState> states = [];

    public double SampleElevationDeltaMeters(PlanetVector direction, int seed)
    {
        var normalized = PlanetVector.Normalize(direction);

        lock (sync)
        {
            if (!states.TryGetValue(seed, out var state))
            {
                return 0.0;
            }

            var (latitudeIndex, longitudeIndex) = GetBin(normalized);
            if (!state.Bins.TryGetValue(GetBinKey(latitudeIndex, longitudeIndex), out var deformations))
            {
                return 0.0;
            }

            var minimumDeltaMeters = 0.0;
            foreach (var deformation in deformations)
            {
                var cosine = Math.Clamp(PlanetVector.Dot(normalized, deformation.CenterDirection), -1.0, 1.0);
                var angularDistance = Math.Acos(cosine);
                if (angularDistance >= deformation.ValleyAngularRadiusRadians)
                {
                    continue;
                }

                var valleyWeight = 1.0 - SmoothStep(
                    deformation.ChannelAngularRadiusRadians,
                    deformation.ValleyAngularRadiusRadians,
                    angularDistance);
                var channelWeight = 1.0 - SmoothStep(0.0, deformation.ChannelAngularRadiusRadians, angularDistance);
                var deltaMeters = -((deformation.ValleyIncisionDepthMeters * valleyWeight)
                    + (deformation.ChannelIncisionDepthMeters * channelWeight));
                minimumDeltaMeters = Math.Min(minimumDeltaMeters, deltaMeters);
            }

            return minimumDeltaMeters;
        }
    }

    public int GetRevision(int seed)
    {
        lock (sync)
        {
            return states.TryGetValue(seed, out var state) ? state.Revision : 0;
        }
    }

    public void Apply(int seed, IReadOnlyList<PlanetTerrainDeformation> deformations)
    {
        ArgumentNullException.ThrowIfNull(deformations);
        if (deformations.Count == 0)
        {
            return;
        }

        lock (sync)
        {
            if (!states.TryGetValue(seed, out var state))
            {
                state = new TerrainState();
                states.Add(seed, state);
            }

            foreach (var deformation in deformations)
            {
                var normalized = ValidateAndNormalize(deformation);
                AddToBins(state, normalized);
            }

            state.Revision++;
        }
    }

    public void Clear(int seed)
    {
        lock (sync)
        {
            states.Remove(seed);
        }
    }

    public void ClearAll()
    {
        lock (sync)
        {
            states.Clear();
        }
    }

    private static PlanetTerrainDeformation ValidateAndNormalize(PlanetTerrainDeformation deformation)
    {
        ArgumentNullException.ThrowIfNull(deformation);

        if (!double.IsFinite(deformation.ChannelAngularRadiusRadians)
            || !double.IsFinite(deformation.ValleyAngularRadiusRadians)
            || deformation.ChannelAngularRadiusRadians <= 0.0
            || deformation.ValleyAngularRadiusRadians <= deformation.ChannelAngularRadiusRadians)
        {
            throw new ArgumentOutOfRangeException(nameof(deformation), "Terrain deformation radii must be finite, positive and ordered.");
        }

        if (!double.IsFinite(deformation.ChannelIncisionDepthMeters)
            || !double.IsFinite(deformation.ValleyIncisionDepthMeters)
            || deformation.ChannelIncisionDepthMeters < 0.0
            || deformation.ValleyIncisionDepthMeters < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(deformation), "Terrain deformation depths must be finite and non-negative.");
        }

        return deformation with { CenterDirection = PlanetVector.Normalize(deformation.CenterDirection) };
    }

    private static void AddToBins(TerrainState state, PlanetTerrainDeformation deformation)
    {
        var latitude = Math.Asin(Math.Clamp(deformation.CenterDirection.Y, -1.0, 1.0));
        var longitude = Math.Atan2(deformation.CenterDirection.Z, deformation.CenterDirection.X);
        var latitudeRadius = deformation.ValleyAngularRadiusRadians;
        var longitudeRadius = Math.Min(
            Math.PI,
            deformation.ValleyAngularRadiusRadians / Math.Max(Math.Cos(latitude), 0.08));

        var minimumLatitudeIndex = LatitudeToBin(Math.Max(-Math.PI * 0.5, latitude - latitudeRadius));
        var maximumLatitudeIndex = LatitudeToBin(Math.Min(Math.PI * 0.5, latitude + latitudeRadius));
        var longitudeIndices = EnumerateLongitudeBins(longitude, longitudeRadius);

        for (var latitudeIndex = minimumLatitudeIndex; latitudeIndex <= maximumLatitudeIndex; latitudeIndex++)
        {
            foreach (var longitudeIndex in longitudeIndices)
            {
                var key = GetBinKey(latitudeIndex, longitudeIndex);
                if (!state.Bins.TryGetValue(key, out var bucket))
                {
                    bucket = [];
                    state.Bins.Add(key, bucket);
                }

                bucket.Add(deformation);
            }
        }
    }

    private static IReadOnlyList<int> EnumerateLongitudeBins(double longitude, double angularRadius)
    {
        if (angularRadius >= Math.PI)
        {
            return Enumerable.Range(0, LongitudeBinCount).ToArray();
        }

        var centerIndex = LongitudeToBin(longitude);
        var binWidth = Math.PI * 2.0 / LongitudeBinCount;
        var radiusBins = Math.Max(1, (int)Math.Ceiling(angularRadius / binWidth));
        var result = new int[(radiusBins * 2) + 1];

        for (var offset = -radiusBins; offset <= radiusBins; offset++)
        {
            result[offset + radiusBins] = WrapLongitudeBin(centerIndex + offset);
        }

        return result.Distinct().ToArray();
    }

    private static (int LatitudeIndex, int LongitudeIndex) GetBin(PlanetVector direction)
    {
        var latitude = Math.Asin(Math.Clamp(direction.Y, -1.0, 1.0));
        var longitude = Math.Atan2(direction.Z, direction.X);
        return (LatitudeToBin(latitude), LongitudeToBin(longitude));
    }

    private static int LatitudeToBin(double latitude)
        => Math.Clamp((int)Math.Floor(((latitude + (Math.PI * 0.5)) / Math.PI) * LatitudeBinCount), 0, LatitudeBinCount - 1);

    private static int LongitudeToBin(double longitude)
        => WrapLongitudeBin((int)Math.Floor(((longitude + Math.PI) / (Math.PI * 2.0)) * LongitudeBinCount));

    private static int WrapLongitudeBin(int index) => ((index % LongitudeBinCount) + LongitudeBinCount) % LongitudeBinCount;

    private static int GetBinKey(int latitudeIndex, int longitudeIndex) => (latitudeIndex * LongitudeBinCount) + longitudeIndex;

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        if (edge1 <= edge0)
        {
            return value >= edge1 ? 1.0 : 0.0;
        }

        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }

    private sealed class TerrainState
    {
        public Dictionary<int, List<PlanetTerrainDeformation>> Bins { get; } = [];

        public int Revision { get; set; }
    }
}
