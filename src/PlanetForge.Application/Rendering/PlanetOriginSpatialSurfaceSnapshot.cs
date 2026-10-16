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
    public float[] SubsurfaceTemperatureKelvin { get; init; } = [];
    public float[] SubsurfaceMeltFraction { get; init; } = [];
    public float[] ThermalFracturePotential { get; init; } = [];
}

public static class PlanetOriginSpatialSurfaceProjection
{
    private const int AtlasWidth = 128;
    private const int AtlasHeight = 64;
    private const int DisplayNeighbors = 8;
    private const int LongitudeBins = 72;
    private const int LatitudeBins = 36;
    private const double CandidateRadiusRadians = Math.PI * 40.0 / 180.0;
    private const double EffectiveYoungsModulusPascals = 70.0e9;
    private const double ThermalExpansionPerKelvin = 8.0e-6;
    private const double PoissonRatio = 0.25;
    private const double EffectiveTensileStrengthPascals = 10.0e6;

    private static readonly object StencilGate = new();
    private static (int Level, int[] Indices, double[] Weights)? cachedStencil;

    public static PlanetOriginSpatialSurfaceSnapshot Create(PlanetOriginCanonicalRockCells region)
    {
        ArgumentNullException.ThrowIfNull(region);
        var field = region.Field;
        var rock = field.Cells.Select(c => region.ResolveSurfacePhase(c.Id) with
        { TemperatureKelvin = region.ResolveRadiatingSurfaceTemperature(c.Id) }).ToArray();
        if (rock.Any(value => !double.IsFinite(value.TemperatureKelvin) || value.TemperatureKelvin < 0.0 || value.TemperatureKelvin > 6553.5))
            throw new InvalidOperationException("Physical surface temperature exceeds the declared RGBA8 display envelope; publication was rejected.");
        var stencil = GetStencil(field.Layout);
        var temperatures = new float[AtlasWidth * AtlasHeight];
        var melts = new float[temperatures.Length];
        var subsurfaceTemperatures = new float[temperatures.Length];
        var subsurfaceMelts = new float[temperatures.Length];
        var fracturePotential = new float[temperatures.Length];
        var subsurfaceRock = new PlanetRockThermalState[rock.Length];
        var fracture = new double[rock.Length];
        for (var cell = 0; cell < rock.Length; cell++)
        {
            subsurfaceRock[cell] = rock[cell];
            if (region.Columns is not { } columns) continue;
            var column = columns.Columns[cell];
            var top = column.Layers[0].Resolve(columns.Material);
            var shallow = column.Layers[Math.Min(1, column.Layers.Count - 1)].Resolve(columns.Material);
            subsurfaceRock[cell] = shallow;
            var solidThickness = column.SolidSurfaceThicknessMeters(columns.Material, columns.Properties);
            if (solidThickness == 0.0 || top.MeltFraction != 0.0) continue;
            var differential = Math.Max(0.0, shallow.TemperatureKelvin - top.TemperatureKelvin);
            var thermoelasticStress = EffectiveYoungsModulusPascals * ThermalExpansionPerKelvin * differential /
                (1.0 - PoissonRatio);
            fracture[cell] = Math.Clamp((thermoelasticStress / EffectiveTensileStrengthPascals) - 1.0, 0.0, 1.0);
        }

        for (var pixel = 0; pixel < temperatures.Length; pixel++)
        {
            var weightSum = 0.0;
            var temperatureSum = 0.0;
            var meltSum = 0.0;
            var subsurfaceTemperatureSum = 0.0;
            var subsurfaceMeltSum = 0.0;
            var fractureSum = 0.0;
            var minimumTemperature = double.PositiveInfinity;
            var maximumTemperature = double.NegativeInfinity;
            var minimumMelt = 1.0;
            var maximumMelt = 0.0;
            var minimumSubsurfaceTemperature = double.PositiveInfinity;
            var maximumSubsurfaceTemperature = double.NegativeInfinity;
            var minimumSubsurfaceMelt = 1.0;
            var maximumSubsurfaceMelt = 0.0;
            var minimumFracture = 1.0;
            var maximumFracture = 0.0;
            for (var slot = 0; slot < DisplayNeighbors; slot++)
            {
                var address = pixel * DisplayNeighbors + slot;
                var weight = stencil.Weights[address];
                if (weight == 0.0) continue;
                var state = rock[stencil.Indices[address]];
                var shallow = subsurfaceRock[stencil.Indices[address]];
                var cellFracture = fracture[stencil.Indices[address]];
                weightSum += weight;
                temperatureSum += weight * state.TemperatureKelvin;
                meltSum += weight * state.MeltFraction;
                subsurfaceTemperatureSum += weight * shallow.TemperatureKelvin;
                subsurfaceMeltSum += weight * shallow.MeltFraction;
                fractureSum += weight * cellFracture;
                minimumTemperature = Math.Min(minimumTemperature, state.TemperatureKelvin);
                maximumTemperature = Math.Max(maximumTemperature, state.TemperatureKelvin);
                minimumMelt = Math.Min(minimumMelt, state.MeltFraction);
                maximumMelt = Math.Max(maximumMelt, state.MeltFraction);
                minimumSubsurfaceTemperature = Math.Min(minimumSubsurfaceTemperature, shallow.TemperatureKelvin);
                maximumSubsurfaceTemperature = Math.Max(maximumSubsurfaceTemperature, shallow.TemperatureKelvin);
                minimumSubsurfaceMelt = Math.Min(minimumSubsurfaceMelt, shallow.MeltFraction);
                maximumSubsurfaceMelt = Math.Max(maximumSubsurfaceMelt, shallow.MeltFraction);
                minimumFracture = Math.Min(minimumFracture, cellFracture);
                maximumFracture = Math.Max(maximumFracture, cellFracture);
            }
            temperatures[pixel] = (float)Math.Clamp(temperatureSum / weightSum, minimumTemperature, maximumTemperature);
            melts[pixel] = (float)Math.Clamp(meltSum / weightSum, minimumMelt, maximumMelt);
            subsurfaceTemperatures[pixel] = (float)Math.Clamp(subsurfaceTemperatureSum / weightSum,
                minimumSubsurfaceTemperature, maximumSubsurfaceTemperature);
            subsurfaceMelts[pixel] = (float)Math.Clamp(subsurfaceMeltSum / weightSum, minimumSubsurfaceMelt, maximumSubsurfaceMelt);
            fracturePotential[pixel] = (float)Math.Clamp(fractureSum / weightSum, minimumFracture, maximumFracture);
        }
        return new(region.Epoch.Revision, AtlasWidth, AtlasHeight, temperatures, melts)
        {
            HasDepthColumns = region.Columns is not null,
            SubsurfaceTemperatureKelvin = subsurfaceTemperatures,
            SubsurfaceMeltFraction = subsurfaceMelts,
            ThermalFracturePotential = fracturePotential
        };
    }

    // Geometry-only, one-entry bounded cache. No temperature/phase/epoch is cached here.
    // Eulerian cell addresses and spherical display interpolation are invariant as the rock cools.
    private static (int Level, int[] Indices, double[] Weights) GetStencil(PlanetSurfaceGridLayout layout)
    {
        lock (StencilGate)
        {
            if (cachedStencil is { } cached && cached.Level == layout.Level) return cached;
            var centres = Enumerable.Range(0, layout.CellCount).Select(i => PlanetSurfaceGridGeometry.GetCenterDirection(layout.GetCell(i))).ToArray();
            var spatialBins = new List<int>?[LongitudeBins * LatitudeBins];
            if (layout.Level >= 3)
            {
                for (var cell = 0; cell < centres.Length; cell++)
                {
                    var direction = centres[cell];
                    var longitude = Math.Atan2(direction.Z, direction.X);
                    var latitude = Math.Asin(Math.Clamp(direction.Y, -1.0, 1.0));
                    var x = Math.Clamp((int)((longitude + Math.PI) / (2.0 * Math.PI) * LongitudeBins), 0, LongitudeBins - 1);
                    var y = Math.Clamp((int)((latitude + Math.PI / 2.0) / Math.PI * LatitudeBins), 0, LatitudeBins - 1);
                    var address = y * LongitudeBins + x;
                    (spatialBins[address] ??= []).Add(cell);
                }
            }
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
                    if (layout.Level < 3)
                    {
                        for (var cell = 0; cell < centres.Length; cell++)
                        {
                            InsertNearest(cell, PlanetVector.Dot(unit, centres[cell]), nearest, proximity);
                        }
                    }
                    else
                    {
                        var latitudeBand = Math.Clamp((int)((latitude + Math.PI / 2.0) / Math.PI * LatitudeBins), 0, LatitudeBins - 1);
                        var longitudeBand = Math.Clamp((int)((longitude + Math.PI) / (2.0 * Math.PI) * LongitudeBins), 0, LongitudeBins - 1);
                        var latitudeRadius = (int)Math.Ceiling(CandidateRadiusRadians / Math.PI * LatitudeBins);
                        var longitudeRadius = Math.Min(LongitudeBins / 2, (int)Math.Ceiling(
                            CandidateRadiusRadians / Math.Max(0.1, Math.Cos(latitude)) / (2.0 * Math.PI) * LongitudeBins));
                        var firstLongitude = longitudeRadius == LongitudeBins / 2 ? 0 : -longitudeRadius;
                        var lastLongitude = longitudeRadius == LongitudeBins / 2 ? LongitudeBins - 1 : longitudeRadius;
                        for (var latitudeOffset = -latitudeRadius; latitudeOffset <= latitudeRadius; latitudeOffset++)
                        {
                            var candidateLatitude = latitudeBand + latitudeOffset;
                            if (candidateLatitude < 0 || candidateLatitude >= LatitudeBins) continue;
                            for (var longitudeOffset = firstLongitude; longitudeOffset <= lastLongitude; longitudeOffset++)
                            {
                                var candidateLongitude = longitudeRadius == LongitudeBins / 2
                                    ? longitudeOffset
                                    : (longitudeBand + longitudeOffset + LongitudeBins) % LongitudeBins;
                                var candidates = spatialBins[candidateLatitude * LongitudeBins + candidateLongitude];
                                if (candidates is null) continue;
                                foreach (var cell in candidates)
                                    InsertNearest(cell, PlanetVector.Dot(unit, centres[cell]), nearest, proximity);
                            }
                        }
                    }
                    for (var slot = 0; slot < DisplayNeighbors; slot++)
                    {
                        var address = (y * AtlasWidth + x) * DisplayNeighbors + slot;
                        indices[address] = nearest[slot];
                        if (nearest[slot] < 0) { weights[address] = 0.0; continue; }
                        var distance = Math.Max(1e-12, 1.0 - proximity[slot]);
                        var support = Math.Max(1e-12, 1.0 - proximity[DisplayNeighbors]);
                        // Compact Shepard support goes to zero as a cell enters/leaves the neighbourhood.
                        // Avoid visible four-neighbour boundaries without adding a new physical heat/phase field.
                        weights[address] = proximity[0] >= 1.0 - 1e-12 ? (slot == 0 ? 1.0 : 0.0)
                            : Math.Pow(Math.Max(0.0, 1.0 / distance - 1.0 / support), 2.0);
                    }
                }
            }
            cachedStencil = (layout.Level, indices, weights);
            return cachedStencil.Value;
        }
    }

    private static void InsertNearest(int cell, double dot, Span<int> nearest, Span<double> proximity)
    {
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
}
