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
                // Activation is measured in physical drainage area and actual
                // pre-existing incision. Hard 0.8 km² / 4 m switches used to
                // stamp finite bank cuts onto the first eligible 8 km cells.
                if (drainageAreaSquareKilometers <= 0.8 || existingCut <= 0.0f)
                {
                    continue;
                }

                var areaProgress = Math.Clamp((drainageAreaSquareKilometers - 0.8) / 1.6, 0.0, 1.0);
                var cutProgress = Math.Clamp(existingCut / 8.0, 0.0, 1.0);
                var activation = SmoothStep(areaProgress) * SmoothStep(cutProgress);
                // Width is a property of the physical catchment. A minimum in pixels
                // artificially broadens every coarse-grid channel; a pixel-radius
                // cap truncates the same banks when the grid is refined.
                var halfWidth = Math.Min(55.0 * Math.Pow(drainageAreaSquareKilometers, 0.38), 1_400.0);
                // Include one receiver reach beyond the source's shoulder support.
                // Bound the search by grid extent before converting to an integer.
                var radius = (int)Math.Ceiling(Math.Min(Math.Max(width, height), halfWidth * 1.8 / spacingMeters + 1.0));
                var receiver = downstreamIndices is null ? -1 : downstreamIndices[center];
                var channelDepth = Math.Min(260.0, existingCut) * activation;
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
                    var headwardCut = Math.Min(incrementalLimit, headDrop * 0.2 * streamEnergy * routedRunoff) * activation;
                    // Bank shoulder geometry is tapered, but the already-incised
                    // river centreline must keep its original depth as the
                    // baseline for headward retreat. Otherwise the shoulder
                    // onset can erase a legitimate small incremental headcut.
                    deepestCut[center] = Math.Max(deepestCut[center], Math.Min(340.0, existingCut + headwardCut));
                    channelDepth = Math.Min(340.0, channelDepth + headwardCut);
                }
                var segmentEast = receiver < 0 ? 0.0 : (receiver % width - x) * spacingMeters;
                var segmentNorth = receiver < 0 ? 0.0 : (receiver / width - y) * spacingMeters;
                var segmentLengthSquared = segmentEast * segmentEast + segmentNorth * segmentNorth;

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
                        // Rock is cut along the entire upstream-to-downstream
                        // receiver reach, not as disconnected elliptical scars
                        // centered on each routed raster vertex. Projection
                        // onto the segment is continuous across diagonal links.
                        var normalizedDistanceSquared = segmentLengthSquared > 0.0
                            ? DistanceToSegmentSquared(east, north, segmentEast, segmentNorth,
                                segmentLengthSquared, halfWidth)
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

    private static double SmoothStep(double value) => value * value * (3.0 - 2.0 * value);

    private static double DistanceToSegmentSquared(double east, double north,
        double segmentEast, double segmentNorth, double segmentLengthSquared, double halfWidth)
    {
        var progress = Math.Clamp((east * segmentEast + north * segmentNorth) /
            segmentLengthSquared, 0.0, 1.0);
        var acrossEast = (east - progress * segmentEast) / halfWidth;
        var acrossNorth = (north - progress * segmentNorth) / halfWidth;
        return acrossEast * acrossEast + acrossNorth * acrossNorth;
    }
}
