using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Origins;

/// <summary>
/// Read-only density contraction of funded columns relative to their initial
/// thickness, optionally composed with the world's existing seeded terrain.
/// Terrain is a static visual prior; this source does not evolve or amplify it.
/// A bounded geographic cache accelerates continuous orbital/local terrain sampling.
/// </summary>
public sealed class PlanetOriginRockColumnElevationSource : IPlanetElevationSource
{
    private const int AtlasWidth = 128;
    private const int AtlasHeight = 64;
    private const int LongitudeBins = 72;
    private const int LatitudeBins = 36;
    private const int DisplayNeighbors = 8;
    private const double CandidateRadiusRadians = Math.PI * 40.0 / 180.0;
    private readonly PlanetSurfaceGridCellId[] cellIds;
    private readonly PlanetVector[] centers;
    private readonly double[] elevationMeters;
    private readonly IPlanetElevationSource? terrainPrior;
    private readonly Dictionary<DirectionKey, int> exactCenterIndices = [];
    private readonly List<int>?[]? spatialBins;
    private readonly Lazy<double[]>? elevationAtlas;
    private readonly int layoutLevel;
    private readonly double? uniformElevationMeters;

    public PlanetOriginRockColumnElevationSource(
        PlanetOriginRockColumnField columns, IPlanetElevationSource? terrainPrior = null)
    {
        ArgumentNullException.ThrowIfNull(columns);
        this.terrainPrior = terrainPrior;
        var layout = columns.Aggregate.Layout;
        layoutLevel = layout.Level;
        cellIds = new PlanetSurfaceGridCellId[columns.Columns.Count];
        centers = new PlanetVector[columns.Columns.Count];
        elevationMeters = new double[columns.Columns.Count];
        var material = columns.Material;
        var properties = columns.Properties;
        var model = new PlanetRockEnthalpyModel(material.ToRockProperties(1.0));
        for (var cell = 0; cell < columns.Columns.Count; cell++)
        {
            var column = columns.Columns[cell];
            cellIds[cell] = column.Id;
            var center = PlanetSurfaceGridGeometry.GetCenterDirection(column.Id);
            centers[cell] = center;
            exactCenterIndices.Add(DirectionKey.Create(PlanetVector.Normalize(center)), cell);
            var volume = 0.0;
            foreach (var layer in column.Layers)
            {
                var phase = model.Resolve(layer.StoredEnthalpyJoules / layer.MassKilograms);
                volume += layer.MassKilograms * ((1.0 - phase.MeltFraction) / properties.SolidDensityKilogramsPerCubicMeter +
                    phase.MeltFraction / properties.LiquidDensityKilogramsPerCubicMeter);
            }
            elevationMeters[cell] = volume / column.AreaSquareMeters - column.ReferenceThicknessMeters;
        }

        uniformElevationMeters = elevationMeters.All(height => height == elevationMeters[0]) ? elevationMeters[0] : null;
        if (uniformElevationMeters is not null) return;

        if (layout.Level >= 3)
        {
            spatialBins = new List<int>?[LongitudeBins * LatitudeBins];
            for (var cell = 0; cell < centers.Length; cell++)
            {
                var direction = centers[cell];
                var longitude = Math.Atan2(direction.Z, direction.X);
                var latitude = Math.Asin(Math.Clamp(direction.Y, -1.0, 1.0));
                var x = Math.Clamp((int)((longitude + Math.PI) / (2.0 * Math.PI) * LongitudeBins), 0, LongitudeBins - 1);
                var y = Math.Clamp((int)((latitude + Math.PI / 2.0) / Math.PI * LatitudeBins), 0, LatitudeBins - 1);
                var address = y * LongitudeBins + x;
                (spatialBins[address] ??= []).Add(cell);
            }
        }

        elevationAtlas = new(BuildElevationAtlas, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsWithinDisplacementTolerance(PlanetOriginRockColumnElevationSource other, double toleranceMeters)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!double.IsFinite(toleranceMeters) || toleranceMeters < 0.0)
            throw new ArgumentOutOfRangeException(nameof(toleranceMeters));
        if (layoutLevel != other.layoutLevel || elevationMeters.Length != other.elevationMeters.Length) return false;
        for (var cell = 0; cell < elevationMeters.Length; cell++)
        {
            if (cellIds[cell] != other.cellIds[cell]) return false;
            if (Math.Abs(elevationMeters[cell] - other.elevationMeters[cell]) > toleranceMeters) return false;
        }
        return true;
    }

    private double[] BuildElevationAtlas()
    {
        var atlas = new double[AtlasWidth * AtlasHeight];
        for (var y = 0; y < AtlasHeight; y++)
        {
            var latitude = (((y + 0.5) / AtlasHeight) - 0.5) * Math.PI;
            var cosine = Math.Cos(latitude);
            for (var x = 0; x < AtlasWidth; x++)
            {
                var longitude = (((x + 0.5) / AtlasWidth) - 0.5) * 2.0 * Math.PI;
                var direction = new PlanetVector(cosine * Math.Cos(longitude), Math.Sin(latitude), cosine * Math.Sin(longitude));
                atlas[y * AtlasWidth + x] = InterpolateSource(direction);
            }
        }
        return atlas;
    }

    public double SampleElevationMeters(PlanetVector direction, int seed)
    {
        var unit = PlanetVector.Normalize(direction);
        double contraction;
        if (exactCenterIndices.TryGetValue(DirectionKey.Create(unit), out var exactCell))
        {
            contraction = elevationMeters[exactCell];
        }
        else if (uniformElevationMeters is { } height)
        {
            contraction = height;
        }
        else
        {
            var latitude = Math.Asin(Math.Clamp(unit.Y, -1.0, 1.0));
            var longitude = Math.Atan2(unit.Z, unit.X);
            var xPosition = (longitude + Math.PI) / (2.0 * Math.PI) * AtlasWidth - 0.5;
            var xFloor = (int)Math.Floor(xPosition);
            var xFraction = xPosition - xFloor;
            var x0 = (xFloor % AtlasWidth + AtlasWidth) % AtlasWidth;
            var x1 = (x0 + 1) % AtlasWidth;
            var yPosition = Math.Clamp((latitude + Math.PI / 2.0) / Math.PI * AtlasHeight - 0.5, 0.0, AtlasHeight - 1.0);
            var y0 = (int)Math.Floor(yPosition);
            var y1 = Math.Min(y0 + 1, AtlasHeight - 1);
            var yFraction = yPosition - y0;
            var atlas = elevationAtlas!.Value;
            var south = atlas[y0 * AtlasWidth + x0] * (1.0 - xFraction) +
                atlas[y0 * AtlasWidth + x1] * xFraction;
            var north = atlas[y1 * AtlasWidth + x0] * (1.0 - xFraction) +
                atlas[y1 * AtlasWidth + x1] * xFraction;
            contraction = south * (1.0 - yFraction) + north * yFraction;
        }

        return contraction + (terrainPrior?.SampleElevationMeters(unit, seed) ?? 0.0);
    }

    private double InterpolateSource(PlanetVector unit)
    {
        Span<int> nearest = stackalloc int[DisplayNeighbors + 1];
        Span<double> proximity = stackalloc double[DisplayNeighbors + 1];
        nearest.Fill(-1);
        proximity.Fill(double.NegativeInfinity);
        if (spatialBins is null)
        {
            for (var cell = 0; cell < centers.Length; cell++)
                InsertNearest(cell, PlanetVector.Dot(unit, centers[cell]), nearest, proximity);
        }
        else
        {
            var latitude = Math.Asin(Math.Clamp(unit.Y, -1.0, 1.0));
            var longitude = Math.Atan2(unit.Z, unit.X);
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
                        InsertNearest(cell, PlanetVector.Dot(unit, centers[cell]), nearest, proximity);
                }
            }
            if (nearest[DisplayNeighbors] < 0)
            {
                nearest.Fill(-1);
                proximity.Fill(double.NegativeInfinity);
                for (var cell = 0; cell < centers.Length; cell++)
                    InsertNearest(cell, PlanetVector.Dot(unit, centers[cell]), nearest, proximity);
            }
        }

        if (nearest[0] >= 0 && 1.0 - proximity[0] <= 1e-12) return elevationMeters[nearest[0]];
        var weightedHeight = 0.0;
        var totalWeight = 0.0;
        for (var slot = 0; slot < DisplayNeighbors; slot++)
        {
            var cell = nearest[slot];
            if (cell < 0) continue;
            var distance = 1.0 - proximity[slot];
            var weight = 1.0 / (distance * distance);
            weightedHeight += elevationMeters[cell] * weight;
            totalWeight += weight;
        }
        return weightedHeight / totalWeight;
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

    private readonly record struct DirectionKey(long X, long Y, long Z)
    {
        public static DirectionKey Create(PlanetVector direction) => new(
            BitConverter.DoubleToInt64Bits(direction.X),
            BitConverter.DoubleToInt64Bits(direction.Y),
            BitConverter.DoubleToInt64Bits(direction.Z));
    }
}
