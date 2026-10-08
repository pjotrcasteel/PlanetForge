namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Deterministic priority-flood watershed model for a sampled patch of canonical planetary
/// bedrock. All distances and sediment volumes are physical units. Outlets on the outer
/// boundary intentionally export water/sediment: the surrounding world is not a hard wall.
/// This model produces geological state; it is never invoked once per rendered vertex.
/// </summary>
public sealed class PlanetRegionalWatershed
{
    private PlanetRegionalWatershed(
        int width, int height, double cellSpacingMeters, int[] receivers, float[] filled,
        float[] accumulatedRunoff, float[] incision, float[] deposition, float[] evolved,
        double erodedVolume, double depositedVolume, double exportedVolume)
    {
        Width = width;
        Height = height;
        CellSpacingMeters = cellSpacingMeters;
        DownstreamIndices = receivers;
        FilledRoutingElevationMeters = filled;
        AccumulatedRunoffCells = accumulatedRunoff;
        IncisionMeters = incision;
        SedimentDepositionMeters = deposition;
        EvolvedElevationMeters = evolved;
        ErodedVolumeCubicMeters = erodedVolume;
        DepositedVolumeCubicMeters = depositedVolume;
        ExportedVolumeCubicMeters = exportedVolume;
    }

    public int Width { get; }
    public int Height { get; }
    public double CellSpacingMeters { get; }
    public int[] DownstreamIndices { get; }
    public float[] FilledRoutingElevationMeters { get; }
    public float[] AccumulatedRunoffCells { get; }
    public float[] IncisionMeters { get; }
    public float[] SedimentDepositionMeters { get; }
    public float[] EvolvedElevationMeters { get; }
    public double ErodedVolumeCubicMeters { get; }
    public double DepositedVolumeCubicMeters { get; }
    public double ExportedVolumeCubicMeters { get; }

    public static PlanetRegionalWatershed Build(
        int width, int height, double cellSpacingMeters, IReadOnlyList<float> bedrockElevationMeters)
    {
        ArgumentNullException.ThrowIfNull(bedrockElevationMeters);
        if (width is < 8 or > 512 || height is < 8 or > 512 || (long)width * height != bedrockElevationMeters.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Watershed requires 8–512 cells on each axis and a matching elevation grid.");
        }

        if (!double.IsFinite(cellSpacingMeters) || cellSpacingMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSpacingMeters));
        }

        var count = width * height;
        var filled = new float[count];
        var distance = new double[count];
        var receivers = new int[count];
        var settled = new bool[count];
        var visitOrder = new int[count];
        var queue = new PriorityQueue<int, (float Elevation, double Distance, int Index)>();
        Array.Fill(filled, float.PositiveInfinity);
        Array.Fill(distance, double.PositiveInfinity);
        Array.Fill(receivers, -1);

        for (var i = 0; i < count; i++)
        {
            var elevation = bedrockElevationMeters[i];
            if (!float.IsFinite(elevation))
            {
                throw new ArgumentException("Canonical bedrock must contain finite heights.", nameof(bedrockElevationMeters));
            }

            var x = i % width;
            var y = i / width;
            if (x == 0 || x == width - 1 || y == 0 || y == height - 1 || elevation <= 0.0f)
            {
                filled[i] = elevation;
                distance[i] = 0.0;
                queue.Enqueue(i, (elevation, 0.0, i));
            }
        }

        var finalized = 0;
        while (queue.TryDequeue(out var current, out var priority))
        {
            if (settled[current] || priority.Elevation != filled[current] || priority.Distance > distance[current])
            {
                continue;
            }

            settled[current] = true;
            visitOrder[finalized++] = current;
            var x = current % width;
            var y = current / width;

            for (var dy = -1; dy <= 1; dy++)
            {
                var ny = y + dy;
                if (ny < 0 || ny >= height)
                {
                    continue;
                }

                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx;
                    if ((dx == 0 && dy == 0) || nx < 0 || nx >= width)
                    {
                        continue;
                    }

                    var next = ny * width + nx;
                    if (settled[next])
                    {
                        continue;
                    }

                    var spillHeight = Math.Max(filled[current], bedrockElevationMeters[next]);
                    var stepDistance = cellSpacingMeters * (dx == 0 || dy == 0 ? 1.0 : Math.Sqrt(2.0));
                    var travelDistance = distance[current] + stepDistance;

                    // Minimax spillway height comes first; the shortest physical drainage
                    // route resolves ties on flats instead of arbitrarily long row-aligned rivers.
                    if (spillHeight > filled[next] ||
                        (spillHeight == filled[next] && travelDistance >= distance[next]))
                    {
                        continue;
                    }

                    filled[next] = spillHeight;
                    distance[next] = travelDistance;
                    receivers[next] = current;
                    queue.Enqueue(next, (spillHeight, travelDistance, next));
                }
            }
        }

        if (finalized != count)
        {
            throw new InvalidOperationException("Watershed routing failed to connect every cell to an outlet.");
        }

        var accumulation = new double[count];
        Array.Fill(accumulation, 1.0);
        for (var order = count - 1; order >= 0; order--)
        {
            var index = visitOrder[order];
            if (receivers[index] >= 0)
            {
                accumulation[receivers[index]] += accumulation[index];
            }
        }

        var cellArea = cellSpacingMeters * cellSpacingMeters;
        var incision = new float[count];
        var deposition = new float[count];
        var sedimentLoad = new double[count];
        var evolved = new float[count];
        var exported = 0.0;
        var eroded = 0.0;
        var deposited = 0.0;

        // A first bounded geological iteration: stream-power incision and capacity-limited
        // sediment transport, in strict upstream-to-downstream routing order.
        for (var order = count - 1; order >= 0; order--)
        {
            var index = visitOrder[order];
            var downstream = receivers[index];
            var runoff = accumulation[index];
            var slope = downstream < 0 ? 0.0 : Math.Max(0.0,
                (filled[index] - filled[downstream]) /
                (cellSpacingMeters * (IsDiagonal(index, downstream, width) ? Math.Sqrt(2.0) : 1.0)));

            if (downstream >= 0 && runoff >= 6.0 && slope > 0.00001)
            {
                var streamPower = Math.Pow((runoff - 5.0) / 8.0, 0.43) * Math.Pow(slope / 0.06, 0.42);
                incision[index] = (float)Math.Min(180.0, 24.0 * streamPower);
            }

            var removedVolume = incision[index] * cellArea;
            eroded += removedVolume;
            sedimentLoad[index] += removedVolume;

            // Sediment can settle on low-gradient reaches; material not deposited is
            // carried downstream or explicitly exported across this region's boundary.
            var capacity = 10.0 * Math.Pow(runoff, 0.65) * slope * cellArea;
            var depositedHere = downstream < 0 ? 0.0 :
                Math.Min(Math.Max(0.0, sedimentLoad[index] - capacity) * 0.3, 80.0 * cellArea);
            deposition[index] = (float)(depositedHere / cellArea);
            sedimentLoad[index] -= depositedHere;
            deposited += depositedHere;
            evolved[index] = bedrockElevationMeters[index] - incision[index] + deposition[index];

            if (downstream < 0)
            {
                exported += sedimentLoad[index];
            }
            else
            {
                sedimentLoad[downstream] += sedimentLoad[index];
            }
        }

        return new PlanetRegionalWatershed(width, height, cellSpacingMeters, receivers, filled,
            accumulation.Select(value => (float)value).ToArray(), incision, deposition, evolved, eroded, deposited, exported);
    }

    private static bool IsDiagonal(int from, int to, int width) =>
        from % width != to % width && from / width != to / width;
}
