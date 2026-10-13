using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Rendering;

/// <summary>
/// Display-only spatial samples from one canonical, conserved rock epoch.
/// Every value is derived from the existing regional cell enthalpy; these
/// samples are not a second simulation state or a source of physical heat.
/// </summary>
public sealed record PlanetOriginSpatialSurfaceSnapshot(
    long Revision, int Width, int Height, float[] TemperatureKelvin, float[] MeltFraction)
{
    public bool HasDepthColumns { get; init; }
}

public static class PlanetOriginSpatialSurfaceProjection
{
    private const int AtlasWidth = 256;
    private const int AtlasHeight = 128;
    private const int DisplayNeighbors = 8;

    private static readonly object StencilGate = new();
    private static (int Level, int[] Indices, double[] Weights)? cachedStencil;

    public static PlanetOriginSpatialSurfaceSnapshot Create(PlanetOriginCanonicalRockCells region)
    {
        ArgumentNullException.ThrowIfNull(region);
        var field = region.Field;
        var rock = field.Cells.Select(c => region.ResolveRadiatingSurfacePhase(c.Id)).ToArray();
        if (rock.Any(value => !double.IsFinite(value.TemperatureKelvin) || value.TemperatureKelvin < 0.0 || value.TemperatureKelvin > 6553.5))
            throw new InvalidOperationException("Physical surface temperature exceeds the declared RGBA8 display envelope; publication was rejected.");
        var stencil = GetStencil(field.Layout);
        var temperatures = new float[AtlasWidth * AtlasHeight];
        var melts = new float[temperatures.Length];
        for (var pixel = 0; pixel < temperatures.Length; pixel++)
        {
            var weightSum = 0.0;
            var temperatureSum = 0.0;
            var meltSum = 0.0;
            var minimumTemperature = double.PositiveInfinity;
            var maximumTemperature = double.NegativeInfinity;
            var minimumMelt = 1.0;
            var maximumMelt = 0.0;
            for (var slot = 0; slot < DisplayNeighbors; slot++)
            {
                var address = pixel * DisplayNeighbors + slot;
                var weight = stencil.Weights[address];
                if (weight == 0.0) continue;
                var state = rock[stencil.Indices[address]];
                weightSum += weight;
                temperatureSum += weight * state.TemperatureKelvin;
                meltSum += weight * state.MeltFraction;
                minimumTemperature = Math.Min(minimumTemperature, state.TemperatureKelvin);
                maximumTemperature = Math.Max(maximumTemperature, state.TemperatureKelvin);
                minimumMelt = Math.Min(minimumMelt, state.MeltFraction);
                maximumMelt = Math.Max(maximumMelt, state.MeltFraction);
            }
            temperatures[pixel] = (float)Math.Clamp(temperatureSum / weightSum, minimumTemperature, maximumTemperature);
            melts[pixel] = (float)Math.Clamp(meltSum / weightSum, minimumMelt, maximumMelt);
        }
        return new(region.Epoch.Revision, AtlasWidth, AtlasHeight, temperatures, melts) { HasDepthColumns = region.Columns is not null };
    }

    // Geometry-only, one-entry bounded cache. No temperature/phase/epoch is cached here.
    // Eulerian cell addresses and spherical display interpolation are invariant as the rock cools.
    private static (int Level, int[] Indices, double[] Weights) GetStencil(PlanetSurfaceGridLayout layout)
    {
        lock (StencilGate)
        {
            if (cachedStencil is { } cached && cached.Level == layout.Level) return cached;
            var centres = Enumerable.Range(0, layout.CellCount).Select(i => PlanetSurfaceGridGeometry.GetCenterDirection(layout.GetCell(i))).ToArray();
            var indices = new int[AtlasWidth * AtlasHeight * DisplayNeighbors];
            var weights = new double[indices.Length];
            Span<int> nearest = stackalloc int[DisplayNeighbors + 1];
            Span<double> proximity = stackalloc double[DisplayNeighbors + 1];
            for (var y = 0; y < AtlasHeight; y++)
            {
                var latitude = (((y + 0.5) / AtlasHeight) - 0.5) * Math.PI;
                for (var x = 0; x < AtlasWidth; x++)
                {
                    var longitude = (((x + 0.5) / AtlasWidth) - 0.5) * 2.0 * Math.PI;
                    var unit = new PlanetVector(Math.Cos(latitude) * Math.Cos(longitude), Math.Sin(latitude), Math.Cos(latitude) * Math.Sin(longitude));
                    nearest.Fill(-1);
                    proximity.Fill(double.NegativeInfinity);
                    for (var cell = 0; cell < centres.Length; cell++)
                    {
                        var dot = PlanetVector.Dot(unit, centres[cell]);
                        for (var slot = 0; slot <= DisplayNeighbors; slot++)
                        {
                            if (dot <= proximity[slot]) continue;
                            for (var shift = DisplayNeighbors; shift > slot; shift--)
                            {
                                proximity[shift] = proximity[shift - 1];
                                nearest[shift] = nearest[shift - 1];
                            }
                            proximity[slot] = dot;
                            nearest[slot] = cell;
                            break;
                        }
                    }
                    for (var slot = 0; slot < DisplayNeighbors; slot++)
                    {
                        var address = (y * AtlasWidth + x) * DisplayNeighbors + slot;
                        indices[address] = nearest[slot];
                        if (nearest[slot] < 0) { weights[address] = 0.0; continue; }
                        var distance = Math.Max(1e-12, 1.0 - proximity[slot]);
                        var support = Math.Max(1e-12, 1.0 - proximity[DisplayNeighbors]);
                        // Normalized compact Wendland reconstruction is smooth at cell centres
                        // and vanishes at support boundaries. Singular inverse-distance weights
                        // created visible square hot plateaus on the cubed-sphere sampling grid.
                        // This bounded optical footprint does not modify the physical cells.
                        var ratio = Math.Min(1.0, Math.Sqrt(distance / support));
                        weights[address] = Math.Pow(1.0 - ratio, 4.0) * (1.0 + 4.0 * ratio);
                    }
                }
            }
            cachedStencil = (layout.Level, indices, weights);
            return cachedStencil.Value;
        }
    }
}
