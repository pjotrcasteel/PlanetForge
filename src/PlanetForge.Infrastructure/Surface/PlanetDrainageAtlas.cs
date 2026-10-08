namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Deterministic, seed-independent drainage solver over a sampled canonical spherical elevation field.
/// This atlas is geological data, not a rendering mesh. Longitude wraps; polar rows remain connected.
/// </summary>
public sealed class PlanetDrainageAtlas
{
    private const double PlanetRadiusKilometers = 6_371.0;

    private PlanetDrainageAtlas(int width, int height, float[] filled, int[] receivers, float[] accumulation,
        float[] incision, float[] deposition, float[] eroded, double exportedSedimentVolume)
    {
        Width = width;
        Height = height;
        FilledElevationMeters = filled;
        DownstreamIndices = receivers;
        FlowAccumulation = accumulation;
        ChannelIncisionMeters = incision;
        SedimentDepositionMeters = deposition;
        ErodedElevationMeters = eroded;
        ExportedSedimentVolume = exportedSedimentVolume;
    }

    public int Width { get; }
    public int Height { get; }
    public float[] FilledElevationMeters { get; }
    public int[] DownstreamIndices { get; }
    public float[] FlowAccumulation { get; }
    public float[] ChannelIncisionMeters { get; }
    public float[] SedimentDepositionMeters { get; }
    public float[] ErodedElevationMeters { get; }

    /// <summary>Sediment leaving the computational domain through ocean or endorheic outlets, in area-weighted metre units.</summary>
    public double ExportedSedimentVolume { get; }

    /// <summary>
    /// Routes runoff with a priority flood. Depression filling is routing-only; actual bedrock is unchanged.
    /// Receiver edges always point to earlier flood visits, so drainage accumulation has no cycles.
    /// Area-weighted rainfall and sediment are expressed in grid-cell units, not physical cubic metres.
    /// </summary>
    public static PlanetDrainageAtlas Build(int width, int height, IReadOnlyList<float> bedrockElevationMeters)
    {
        ArgumentNullException.ThrowIfNull(bedrockElevationMeters);
        if (width < 4 || height < 3 || (long)width * height != bedrockElevationMeters.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "A drainage atlas requires a rectangular grid of at least 4 x 3.");
        }

        var count = bedrockElevationMeters.Count;
        var filled = new float[count];
        var receivers = new int[count];
        var visited = new bool[count];
        var visitOrder = new int[count];
        var queue = new PriorityQueue<int, (float Height, int Index)>();
        var depressionQueue = new Queue<int>();
        Array.Fill(receivers, -1);

        var hasOcean = false;
        var lowestIndex = 0;
        for (var i = 0; i < count; i++)
        {
            var bedrock = bedrockElevationMeters[i];
            if (!float.IsFinite(bedrock))
            {
                throw new ArgumentException("The elevation grid contains a non-finite value.", nameof(bedrockElevationMeters));
            }

            filled[i] = bedrock;
            if (bedrock < bedrockElevationMeters[lowestIndex])
            {
                lowestIndex = i;
            }

            if (bedrock <= 0.0f)
            {
                hasOcean = true;
                visited[i] = true;
                queue.Enqueue(i, (bedrock, i));
            }
        }

        // An entirely dry world retains its global minimum as an endorheic outlet.
        if (!hasOcean)
        {
            visited[lowestIndex] = true;
            queue.Enqueue(lowestIndex, (filled[lowestIndex], lowestIndex));
        }

        var visitedCount = 0;
        while (depressionQueue.Count > 0 || queue.Count > 0)
        {
            // Process all equal-height boundary fronts before their flooded interior.
            // Otherwise a single heap tie wins the entire flat and creates a long, artificial drain.
            var hasBoundary = queue.TryPeek(out _, out var nextBoundary);
            var takeBoundary = hasBoundary && (depressionQueue.Count == 0 ||
                nextBoundary.Height <= filled[depressionQueue.Peek()]);
            var current = takeBoundary ? queue.Dequeue() : depressionQueue.Dequeue();
            visitOrder[visitedCount++] = current;
            var x = current % width;
            var y = current / width;

            for (var dy = -1; dy <= 1; dy++)
            {
                var ny = y + dy;
                var crossingPole = ny < 0 || ny >= height;
                ny = Math.Clamp(ny, 0, height - 1);

                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    // Opposite meridians are adjacent across a polar cap, not a hard latitude wall.
                    var nx = (x + dx + width + (crossingPole ? width / 2 : 0)) % width;
                    var neighbor = (ny * width) + nx;
                    if (visited[neighbor])
                    {
                        continue;
                    }

                    visited[neighbor] = true;
                    receivers[neighbor] = current;
                    filled[neighbor] = Math.Max(filled[neighbor], filled[current]);
                    if (bedrockElevationMeters[neighbor] <= filled[current])
                    {
                        depressionQueue.Enqueue(neighbor);
                    }
                    else
                    {
                        queue.Enqueue(neighbor, (filled[neighbor], neighbor));
                    }
                }
            }
        }

        var area = new double[count];
        var accumulation = new float[count];
        for (var y = 0; y < height; y++)
        {
            var cellArea = Math.Cos(Math.PI * (0.5 - ((y + 0.5) / height)));
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                area[index] = cellArea;
                accumulation[index] = (float)cellArea;
            }
        }

        // Reverse flood visitation is an exact upstream-to-downstream topological order.
        for (var position = visitedCount - 1; position >= 0; position--)
        {
            var index = visitOrder[position];
            if (receivers[index] >= 0)
            {
                accumulation[receivers[index]] += accumulation[index];
            }
        }

        var incision = new float[count];
        var deposition = new float[count];
        var eroded = new float[count];
        var sedimentLoad = new double[count];
        var exported = 0.0;

        for (var position = visitedCount - 1; position >= 0; position--)
        {
            var index = visitOrder[position];
            var receiver = receivers[index];
            var runoff = accumulation[index];
            var slope = receiver < 0 ? 0.0 : Math.Max(0.0,
                (filled[index] - filled[receiver]) / DistanceKilometers(index, receiver, width, height));

            // Stream power is a bounded geological estimate, not a per-vertex erosion simulation.
            // A connected channel needs contributing drainage area and real routed gradient.
            if (bedrockElevationMeters[index] > 0.0f && receiver >= 0 && runoff >= 6.0f)
            {
                var streamPower = Math.Pow(runoff - 5.0, 0.30) * Math.Pow(slope, 0.55);
                incision[index] = (float)Math.Min(240.0, 18.0 * streamPower);
            }

            sedimentLoad[index] += incision[index] * area[index];
            var capacity = 20.0 * Math.Pow(runoff, 0.75) * slope * area[index];
            var isOutlet = receiver < 0;
            var depositionVolume = isOutlet
                ? Math.Min(sedimentLoad[index], 240.0 * area[index])
                : Math.Min(Math.Max(0.0, sedimentLoad[index] - capacity) * 0.35, 120.0 * area[index]);

            deposition[index] = (float)(depositionVolume / area[index]);
            sedimentLoad[index] -= depositionVolume;
            eroded[index] = bedrockElevationMeters[index] - incision[index] + deposition[index];

            if (isOutlet)
            {
                exported += sedimentLoad[index];
            }
            else
            {
                sedimentLoad[receiver] += sedimentLoad[index];
            }
        }

        return new PlanetDrainageAtlas(width, height, filled, receivers, accumulation, incision, deposition, eroded, exported);
    }

    private static double DistanceKilometers(int from, int to, int width, int height)
    {
        var fromX = from % width;
        var toX = to % width;
        var deltaX = Math.Abs(fromX - toX);
        deltaX = Math.Min(deltaX, width - deltaX);
        var fromY = from / width;
        var toY = to / width;
        var fromLatitude = Math.PI * (0.5 - ((fromY + 0.5) / height));
        var toLatitude = Math.PI * (0.5 - ((toY + 0.5) / height));
        var angularLongitude = 2.0 * Math.PI * deltaX / width;
        var dot = (Math.Sin(fromLatitude) * Math.Sin(toLatitude)) +
            (Math.Cos(fromLatitude) * Math.Cos(toLatitude) * Math.Cos(angularLongitude));
        return Math.Max(1.0, Math.Acos(Math.Clamp(dot, -1.0, 1.0)) * PlanetRadiusKilometers);
    }
}
