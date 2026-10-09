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
        int width, int height, double cellSpacingMeters, int[] receivers, int[] secondaryReceivers, float[] secondaryFractions, float[] filled,
        float[] accumulatedRunoff, float[] incision, float[] deposition, float[] evolved,
        double erodedVolume, double depositedVolume, double exportedVolume)
    {
        Width = width;
        Height = height;
        CellSpacingMeters = cellSpacingMeters;
        DownstreamIndices = receivers;
        SecondaryDownstreamIndices = secondaryReceivers;
        SecondaryFlowFractions = secondaryFractions;
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
    public int[] SecondaryDownstreamIndices { get; }
    public float[] SecondaryFlowFractions { get; }
    public float[] FilledRoutingElevationMeters { get; }
    public float[] AccumulatedRunoffCells { get; }
    public float[] IncisionMeters { get; }
    public float[] SedimentDepositionMeters { get; }
    public float[] EvolvedElevationMeters { get; }
    public double ErodedVolumeCubicMeters { get; }
    public double DepositedVolumeCubicMeters { get; }
    public double ExportedVolumeCubicMeters { get; }

    public static PlanetRegionalWatershed Build(
        int width, int height, double cellSpacingMeters, IReadOnlyList<float> bedrockElevationMeters,
        IReadOnlyList<float>? rainfallCellWeights = null, IReadOnlyList<float>? erodibilityCellWeights = null)
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
        if (rainfallCellWeights is not null && rainfallCellWeights.Count != count)
        {
            throw new ArgumentException("Rainfall weights must match the sampled region grid.", nameof(rainfallCellWeights));
        }

        if (erodibilityCellWeights is not null && erodibilityCellWeights.Count != count)
        {
            throw new ArgumentException("Rock erodibility weights must match the elevation grid.", nameof(erodibilityCellWeights));
        }

        if (erodibilityCellWeights is not null &&
            erodibilityCellWeights.Any(value => !float.IsFinite(value) || value < 0.0f || value > 3.0f))
        {
            throw new ArgumentException("Rock erodibility must be finite and within 0 to 3.", nameof(erodibilityCellWeights));
        }

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
            if (elevation <= 0.0f || IsBoundarySpillway(x, y, width, height, bedrockElevationMeters))
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

        var secondaryReceivers = new int[count];
        var secondaryFractions = new float[count];
        Array.Fill(secondaryReceivers, -1);
        var priorityOrder = new int[count];
        for (var position = 0; position < count; position++)
        {
            priorityOrder[visitOrder[position]] = position;
        }

        CalculateDirectionalReceivers(width, height, filled, distance, priorityOrder, receivers,
            secondaryReceivers, secondaryFractions);

        var accumulation = new double[count];
        for (var i = 0; i < count; i++)
        {
            var rainfall = rainfallCellWeights is null ? 1.0f : rainfallCellWeights[i];
            if (!float.IsFinite(rainfall) || rainfall < 0.0f)
            {
                throw new ArgumentException("Rainfall weights must be finite and non-negative.", nameof(rainfallCellWeights));
            }

            accumulation[i] = rainfall;
        }

        for (var order = count - 1; order >= 0; order--)
        {
            var index = visitOrder[order];
            if (receivers[index] >= 0)
            {
                var secondaryPart = secondaryFractions[index];
                accumulation[receivers[index]] += accumulation[index] * (1.0 - secondaryPart);
                if (secondaryReceivers[index] >= 0)
                {
                    accumulation[secondaryReceivers[index]] += accumulation[index] * secondaryPart;
                }
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
            // Water routed through a catchment is an area, not a number of
            // pixels. Refining 1 km cells to 500 m cells must not quadruple
            // physical stream power for the same mountain basin.
            var drainageAreaSquareKilometers = runoff * cellArea / 1_000_000.0;
            var secondary = secondaryReceivers[index];
            var secondaryPart = secondaryFractions[index];
            var primarySlope = downstream < 0 ? 0.0 : Math.Max(0.0,
                (filled[index] - filled[downstream]) /
                (cellSpacingMeters * (IsDiagonal(index, downstream, width) ? Math.Sqrt(2.0) : 1.0)));
            var alternateSlope = secondary < 0 ? 0.0 : Math.Max(0.0,
                (filled[index] - filled[secondary]) /
                (cellSpacingMeters * (IsDiagonal(index, secondary, width) ? Math.Sqrt(2.0) : 1.0)));
            var slope = (primarySlope * (1.0 - secondaryPart)) + (alternateSlope * secondaryPart);

            if (downstream >= 0 && drainageAreaSquareKilometers >= 6.0 && slope > 0.00001)
            {
                var streamPower = Math.Pow((drainageAreaSquareKilometers - 5.0) / 8.0, 0.43) *
                    Math.Pow(slope / 0.06, 0.42);
                var erodibility = erodibilityCellWeights is null ? 1.0f : erodibilityCellWeights[index];
                // One pass is an uncalibrated numerical iteration, not a
                // geological era. A 180 m incision per pass generated 100 m
                // of region-wide sediment export and grid-aligned cliffs.
                incision[index] = (float)Math.Min(12.0, 24.0 * streamPower * erodibility);
            }

            var removedVolume = incision[index] * cellArea;
            eroded += removedVolume;
            sedimentLoad[index] += removedVolume;

            // Sediment can settle on low-gradient reaches; material not deposited is
            // carried downstream or explicitly exported across this region's boundary.
            // Sediment is advected by moving water. The previous linear-slope
            // capacity severely underestimated transport on active hillslopes and
            // deposited up to 80 m per numerical pass across entire catchments.
            // Only a connected, low-gradient reach can accumulate a floodplain.
            // Keep the remaining sediment in transit until another reach or outlet.
            var capacity = 12.0 * Math.Pow(drainageAreaSquareKilometers, 0.65) * Math.Sqrt(slope) * cellArea;
            var floodplainFactor = Math.Clamp((0.035 - slope) / 0.035, 0.0, 1.0);
            var depositionalCapacityMeters = Math.Min(6.0, 0.35 * Math.Sqrt(drainageAreaSquareKilometers));
            var depositedHere = downstream < 0 || drainageAreaSquareKilometers < 8.0 ? 0.0 :
                Math.Min(Math.Max(0.0, sedimentLoad[index] - capacity) * 0.25 * floodplainFactor,
                    depositionalCapacityMeters * cellArea);
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
                sedimentLoad[downstream] += sedimentLoad[index] * (1.0 - secondaryPart);
                if (secondary >= 0)
                {
                    sedimentLoad[secondary] += sedimentLoad[index] * secondaryPart;
                }
            }
        }

        return new PlanetRegionalWatershed(width, height, cellSpacingMeters, receivers, secondaryReceivers, secondaryFractions, filled,
            accumulation.Select(value => (float)value).ToArray(), incision, deposition, evolved, eroded, deposited, exported);
    }

    private static readonly (int X, int Y)[] Compass =
    [
        (1, 0), (1, 1), (0, 1), (-1, 1),
        (-1, 0), (-1, -1), (0, -1), (1, -1),
    ];

    private static void CalculateDirectionalReceivers(
        int width, int height, float[] filled, double[] distance, int[] priorityOrder,
        int[] primary, int[] secondary, float[] secondaryFractions)
    {
        // Priority-flood provides a valid downhill, acyclic fallback receiver tree.
        // Flow direction follows the continuous local height gradient, with runoff split
        // between the two adjacent D-infinity octants (not forced into a single 45° line).
        for (var i = 0; i < primary.Length; i++)
        {
            if (primary[i] < 0)
            {
                continue;
            }

            var x = i % width;
            var y = i / width;
            var left = RoutingPotential(y * width + Math.Max(0, x - 1), filled, distance);
            var right = RoutingPotential(y * width + Math.Min(width - 1, x + 1), filled, distance);
            var up = RoutingPotential(Math.Max(0, y - 1) * width + x, filled, distance);
            var down = RoutingPotential(Math.Min(height - 1, y + 1) * width + x, filled, distance);
            var gradientX = right - left;
            var gradientY = down - up;
            if (Math.Abs(gradientX) + Math.Abs(gradientY) <= 1e-10)
            {
                continue;
            }

            var angle = Math.Atan2(-gradientY, -gradientX);
            if (angle < 0.0)
            {
                angle += Math.PI * 2.0;
            }

            var sector = angle / (Math.PI / 4.0);
            var lowerDirection = (int)Math.Floor(sector) % 8;
            var upperDirection = (lowerDirection + 1) % 8;
            var lower = DownhillNeighbor(i, lowerDirection, width, height, filled, distance, priorityOrder);
            var upper = DownhillNeighbor(i, upperDirection, width, height, filled, distance, priorityOrder);

            if (lower < 0 && upper < 0)
            {
                continue;
            }

            var upperWeight = sector - Math.Floor(sector);
            if (lower < 0 || upperWeight >= 0.999999)
            {
                primary[i] = upper >= 0 ? upper : primary[i];
                continue;
            }

            if (upper < 0 || upperWeight <= 0.000001)
            {
                primary[i] = lower;
                continue;
            }

            if (upperWeight <= 0.5)
            {
                primary[i] = lower;
                secondary[i] = upper;
                secondaryFractions[i] = (float)upperWeight;
            }
            else
            {
                primary[i] = upper;
                secondary[i] = lower;
                secondaryFractions[i] = (float)(1.0 - upperWeight);
            }
        }
    }

    private static int DownhillNeighbor(
        int index, int direction, int width, int height, float[] filled, double[] distance, int[] priorityOrder)
    {
        var nextX = index % width + Compass[direction].X;
        var nextY = index / width + Compass[direction].Y;
        if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height)
        {
            return -1;
        }

        var next = nextY * width + nextX;
        if (priorityOrder[next] >= priorityOrder[index] ||
            RoutingPotential(next, filled, distance) >= RoutingPotential(index, filled, distance))
        {
            return -1;
        }

        return next;
    }

    private static double RoutingPotential(int index, float[] filled, double[] distance) =>
        filled[index] + (distance[index] * 0.00001);

    private static bool IsBoundarySpillway(int x, int y, int width, int height, IReadOnlyList<float> elevation)
    {
        var value = elevation[y * width + x];
        // Open regional patches are not boxes with independent drains at every edge cell:
        // select physically plausible low saddles along the actual perimeter. High boundary
        // ridges must not become artificial sinks merely because the raster stops here.
        var alongRow = (y == 0 || y == height - 1) &&
            value <= elevation[y * width + Math.Max(0, x - 1)] &&
            value <= elevation[y * width + Math.Min(width - 1, x + 1)];
        var alongColumn = (x == 0 || x == width - 1) &&
            value <= elevation[Math.Max(0, y - 1) * width + x] &&
            value <= elevation[Math.Min(height - 1, y + 1) * width + x];
        return alongRow || alongColumn;
    }

    private static bool IsDiagonal(int from, int to, int width) =>
        from % width != to % width && from / width != to / width;
}
