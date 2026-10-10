namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Experimental physical bank retreat derived from the regional watershed.
/// Channel width follows upstream drainage area in square metres, not grid-cell
/// count. Where a downstream receiver is known, bank retreat acts chiefly
/// across the stream, preserving a connected, direction-following valley.
/// This modifies the heightfield; there is no shader-only river displacement.
/// </summary>
public static class PlanetValleyBankCarver
{
    public sealed record Result(float[] ElevationMeters, double AdditionalExportedSedimentCubicMeters);

    public static Result Apply(
        IReadOnlyList<float> original, IReadOnlyList<float> eroded, IReadOnlyList<float> accumulatedRunoff,
        int width, int height, double spacingMeters, IReadOnlyList<int>? downstreamIndices = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(eroded);
        ArgumentNullException.ThrowIfNull(accumulatedRunoff);
        if (width is < 9 or > 512 || height is < 9 or > 512 ||
            (long)width * height != original.Count || original.Count != eroded.Count ||
            original.Count != accumulatedRunoff.Count)
        {
            throw new ArgumentException("Bank carving requires three matching geological raster fields.");
        }

        if (!double.IsFinite(spacingMeters) || spacingMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(spacingMeters));
        }

        if (downstreamIndices is not null &&
            (downstreamIndices.Count != original.Count ||
             downstreamIndices.Any(index => index < -1 || index >= original.Count)))
        {
            throw new ArgumentException("Downstream receiver indices must match the elevation grid.", nameof(downstreamIndices));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var length = original.Count;
        var deepestCut = new double[length];
        for (var i = 0; i < length; i++)
        {
            if (!float.IsFinite(original[i]) || !float.IsFinite(eroded[i]) ||
                !float.IsFinite(accumulatedRunoff[i]) || accumulatedRunoff[i] < 0)
            {
                throw new ArgumentException("Geological raster fields must be finite; runoff must be nonnegative.");
            }

            deepestCut[i] = Math.Max(0, original[i] - eroded[i]);
        }

        // River sources require both routed water and existing physical erosion.
        // Drainage area = accumulated runoff cells * cell area. The same physical
        // catchment should form roughly the same width on a finer nested raster.
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var center = y * width + x;
                var runoff = accumulatedRunoff[center];
                var drainageAreaSquareKilometers = runoff * spacingMeters * spacingMeters / 1_000_000.0;
                var existingCut = original[center] - eroded[center];
                // A fixed pixel-count threshold becomes 16x easier to meet
                // when cell width halves. Tributary width and activation
                // must respond to contributing *physical* drainage area.
                if (drainageAreaSquareKilometers < 0.8 || existingCut < 4.0f)
                {
                    continue;
                }
                var physicalHalfWidth = 55.0 * Math.Pow(drainageAreaSquareKilometers, 0.38) + spacingMeters * 0.2;
                var halfWidth = Math.Clamp(physicalHalfWidth, spacingMeters * 1.35, Math.Max(spacingMeters * 1.35, 1_400.0));
                var radius = Math.Min(12, (int)Math.Ceiling(halfWidth * 1.8 / spacingMeters));
                var receiver = downstreamIndices is null ? -1 : downstreamIndices[center];
                var channelDepth = Math.Min(260.0, existingCut);
                if (receiver >= 0)
                {
                    // Headward erosion deepens only an existing, connected,
                    // steep-water channel. The local bed cannot cut through
                    // the downstream bed: incision follows a bounded fraction
                    // of that reach's head drop and existing physical cut.
                    var receiverDx = receiver % width - x;
                    var receiverDy = receiver / width - y;
                    var reachMeters = Math.Sqrt(receiverDx * receiverDx + receiverDy * receiverDy) * spacingMeters;
                    var headDrop = Math.Max(0.0, eroded[center] - eroded[receiver]);
                    var streamSlope = headDrop / reachMeters;
                    var streamEnergy = Math.Clamp((streamSlope - 0.04) / 0.08, 0.0, 1.0);
                    var routedRunoff = Math.Clamp(Math.Sqrt(drainageAreaSquareKilometers / 80.0), 0.0, 1.0);
                    // The former 55%-of-one-cell head-drop rule could excavate
                    // hundreds of metres at a single 1 km raster vertex and
                    // form visible stair-steps. A reach can only retreat a small
                    // fraction of its already physically incised channel depth
                    // and of its own length in one geological solve.
                    // Smaller cells consequently evolve smaller headward steps.
                    var incrementalLimit = Math.Min(existingCut * 0.28, reachMeters * 0.06);
                    var headwardCut = Math.Min(incrementalLimit, headDrop * 0.2 * streamEnergy * routedRunoff);
                    channelDepth = Math.Min(340.0, channelDepth + headwardCut);
                    deepestCut[center] = Math.Max(deepestCut[center], channelDepth);
                }
                var directionX = receiver < 0 ? 0.0 : receiver % width - x;
                var directionY = receiver < 0 ? 0.0 : receiver / width - y;
                var directionLength = Math.Sqrt(directionX * directionX + directionY * directionY);
                var alongHalfWidth = Math.Max(spacingMeters * 0.9, Math.Min(halfWidth * 0.65, spacingMeters * 2.0));

                for (var dy = -radius; dy <= radius; dy++)
                {
                    var ny = y + dy;
                    if (ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var nx = x + dx;
                        if (nx < 0 || nx >= width)
                        {
                            continue;
                        }

                        var east = dx * spacingMeters;
                        var north = dy * spacingMeters;
                        var normalizedDistanceSquared = directionLength > 0.0
                            ? DirectionalDistanceSquared(east, north, directionX / directionLength,
                                directionY / directionLength, halfWidth, alongHalfWidth)
                            : (east * east + north * north) / (halfWidth * halfWidth);
                        if (normalizedDistanceSquared > 3.24)
                        {
                            continue;
                        }

                        var shoulderCut = channelDepth * Math.Exp(-1.8 * normalizedDistanceSquared);
                        var neighbor = ny * width + nx;
                        // Never raise an existing riverbed, erase sediment deposition
                        // unless actually excavated, or invent a channel without runoff.
                        deepestCut[neighbor] = Math.Max(deepestCut[neighbor], shoulderCut);
                    }
                }
            }
        }

        var output = new float[length];
        var extraRemoved = 0.0;
        for (var i = 0; i < length; i++)
        {
            output[i] = (float)Math.Min(eroded[i], original[i] - deepestCut[i]);
            extraRemoved += Math.Max(0.0, eroded[i] - (double)output[i]);
        }

        return new Result(output, extraRemoved * spacingMeters * spacingMeters);
    }

    private static double DirectionalDistanceSquared(
        double east, double north, double eastFlow, double northFlow,
        double acrossHalfWidth, double alongHalfWidth)
    {
        var across = (north * eastFlow - east * northFlow) / acrossHalfWidth;
        var along = (east * eastFlow + north * northFlow) / alongHalfWidth;
        return across * across + along * along;
    }
}
