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

            var key = GetBinKey(GetBin(normalized));
            var erosionMeters = SampleErosion(state.ErosionBins, key, normalized);
            var depositionMeters = SampleDeposition(state.DepositionBins, key, normalized);
            return erosionMeters + depositionMeters;
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
            var state = GetOrCreateState(seed);
            foreach (var deformation in deformations)
            {
                AddErosionToBins(state, ValidateAndNormalize(deformation));
            }

            state.Revision++;
        }
    }

    public void ApplyDeposition(int seed, IReadOnlyList<PlanetTerrainDeposition> depositions)
    {
        ArgumentNullException.ThrowIfNull(depositions);
        if (depositions.Count == 0)
        {
            return;
        }

        lock (sync)
        {
            var state = GetOrCreateState(seed);
            foreach (var deposition in depositions)
            {
                AddDepositionToBins(state, ValidateAndNormalize(deposition));
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

    private TerrainState GetOrCreateState(int seed)
    {
        if (states.TryGetValue(seed, out var state))
        {
            return state;
        }

        state = new TerrainState();
        states.Add(seed, state);
        return state;
    }

    private static double SampleErosion(
        IReadOnlyDictionary<int, List<PlanetTerrainDeformation>> bins,
        int key,
        PlanetVector direction)
    {
        if (!bins.TryGetValue(key, out var deformations))
        {
            return 0.0;
        }

        var minimumDeltaMeters = 0.0;
        foreach (var deformation in deformations)
        {
            var angularDistance = AngularDistance(direction, deformation.CenterDirection);
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

    private static double SampleDeposition(
        IReadOnlyDictionary<int, List<PlanetTerrainDeposition>> bins,
        int key,
        PlanetVector direction)
    {
        if (!bins.TryGetValue(key, out var depositions))
        {
            return 0.0;
        }

        var maximumDeltaMeters = 0.0;
        foreach (var deposition in depositions)
        {
            var angularDistance = AngularDistance(direction, deposition.CenterDirection);
            if (angularDistance >= deposition.ApronAngularRadiusRadians)
            {
                continue;
            }

            var apronWeight = 1.0 - SmoothStep(
                deposition.CoreAngularRadiusRadians,
                deposition.ApronAngularRadiusRadians,
                angularDistance);
            var coreWeight = 1.0 - SmoothStep(0.0, deposition.CoreAngularRadiusRadians, angularDistance);
            var deltaMeters =
                (deposition.ApronDepositionHeightMeters * apronWeight) +
                (deposition.CoreDepositionHeightMeters * coreWeight);
            maximumDeltaMeters = Math.Max(maximumDeltaMeters, deltaMeters);
        }

        return maximumDeltaMeters;
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

    private static PlanetTerrainDeposition ValidateAndNormalize(PlanetTerrainDeposition deposition)
    {
        ArgumentNullException.ThrowIfNull(deposition);

        if (!double.IsFinite(deposition.CoreAngularRadiusRadians)
            || !double.IsFinite(deposition.ApronAngularRadiusRadians)
            || deposition.CoreAngularRadiusRadians <= 0.0
            || deposition.ApronAngularRadiusRadians <= deposition.CoreAngularRadiusRadians)
        {
            throw new ArgumentOutOfRangeException(nameof(deposition), "Terrain deposition radii must be finite, positive and ordered.");
        }

        if (!double.IsFinite(deposition.CoreDepositionHeightMeters)
            || !double.IsFinite(deposition.ApronDepositionHeightMeters)
            || deposition.CoreDepositionHeightMeters < 0.0
            || deposition.ApronDepositionHeightMeters < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(deposition), "Terrain deposition heights must be finite and non-negative.");
        }

        return deposition with { CenterDirection = PlanetVector.Normalize(deposition.CenterDirection) };
    }

    private static void AddErosionToBins(TerrainState state, PlanetTerrainDeformation deformation)
        => AddToBins(state.ErosionBins, deformation.CenterDirection, deformation.ValleyAngularRadiusRadians, deformation);

    private static void AddDepositionToBins(TerrainState state, PlanetTerrainDeposition deposition)
        => AddToBins(state.DepositionBins, deposition.CenterDirection, deposition.ApronAngularRadiusRadians, deposition);

    private static void AddToBins<T>(
        Dictionary<int, List<T>> bins,
        PlanetVector centerDirection,
        double angularRadius,
        T value)
    {
        var latitude = Math.Asin(Math.Clamp(centerDirection.Y, -1.0, 1.0));
        var longitude = Math.Atan2(centerDirection.Z, centerDirection.X);
        var longitudeRadius = Math.Min(Math.PI, angularRadius / Math.Max(Math.Cos(latitude), 0.08));
        var minimumLatitudeIndex = LatitudeToBin(Math.Max(-Math.PI * 0.5, latitude - angularRadius));
        var maximumLatitudeIndex = LatitudeToBin(Math.Min(Math.PI * 0.5, latitude + angularRadius));
        var longitudeIndices = EnumerateLongitudeBins(longitude, longitudeRadius);

        for (var latitudeIndex = minimumLatitudeIndex; latitudeIndex <= maximumLatitudeIndex; latitudeIndex++)
        {
            foreach (var longitudeIndex in longitudeIndices)
            {
                var key = GetBinKey(latitudeIndex, longitudeIndex);
                if (!bins.TryGetValue(key, out var bucket))
                {
                    bucket = [];
                    bins.Add(key, bucket);
                }

                bucket.Add(value);
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

    private static int GetBinKey((int LatitudeIndex, int LongitudeIndex) bin) => GetBinKey(bin.LatitudeIndex, bin.LongitudeIndex);

    private static int GetBinKey(int latitudeIndex, int longitudeIndex) => (latitudeIndex * LongitudeBinCount) + longitudeIndex;

    private static double AngularDistance(PlanetVector first, PlanetVector second)
        => Math.Acos(Math.Clamp(PlanetVector.Dot(first, second), -1.0, 1.0));

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
        public Dictionary<int, List<PlanetTerrainDeformation>> ErosionBins { get; } = [];

        public Dictionary<int, List<PlanetTerrainDeposition>> DepositionBins { get; } = [];

        public int Revision { get; set; }
    }
}
