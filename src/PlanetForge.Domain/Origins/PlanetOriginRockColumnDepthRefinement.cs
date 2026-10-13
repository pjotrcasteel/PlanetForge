namespace PlanetForge.Domain.Origins;

/// <summary>
/// Numerical depth-grid diagnostics. A fully solid control volume is NOT a
/// physically calibrated solidification front or a mechanically coherent crust.
/// </summary>
public readonly record struct PlanetOriginRockColumnResolutionSample(
    int LayerCount, double FullySolidTopMeters, double FirstUnresolvedCellBottomMeters,
    double SurfaceTemperatureKelvin, int FullySolidTopLayers)
{
    public double UnresolvedCellWidthMeters => FirstUnresolvedCellBottomMeters - FullySolidTopMeters;

    public static PlanetOriginRockColumnResolutionSample FromColumn(
        PlanetOriginRockColumn column, PlanetOriginSurfaceRockPhaseMaterial material,
        PlanetOriginRockColumnProperties properties)
    {
        ArgumentNullException.ThrowIfNull(column);
        var top = 0.0;
        var firstUnresolvedBottom = 0.0;
        var solidLayers = 0;
        var foundUnresolved = false;
        foreach (var layer in column.Layers)
        {
            var melt = layer.Resolve(material).MeltFraction;
            var thickness = layer.VolumeCubicMeters(material, properties) / column.AreaSquareMeters;
            if (!foundUnresolved && melt == 0.0)
            {
                top += thickness;
                solidLayers++;
            }
            else if (!foundUnresolved)
            {
                firstUnresolvedBottom = top + thickness;
                foundUnresolved = true;
            }
        }
        if (!foundUnresolved) firstUnresolvedBottom = top;
        return new(properties.LayerCount, top, firstUnresolvedBottom,
            column.Layers[0].Resolve(material).TemperatureKelvin, solidLayers);
    }

    /// <summary>
    /// Conservative front bounds from the contiguous fully solid layer region.
    /// The actual solidification front is unresolved inside the next cell; this
    /// interval must not be presented as a measured crust thickness.
    /// </summary>
    public (double MinimumMeters, double MaximumMeters) FrontBoundsMeters =>
        (FullySolidTopMeters, FirstUnresolvedCellBottomMeters);

    /// <summary>
    /// Display-only center of the unresolved interval. It is not a physical
    /// Stefan-front reconstruction and must not be used by the integrator.
    /// </summary>
    public double UnresolvedFrontMidpointMeters =>
        (FullySolidTopMeters + FirstUnresolvedCellBottomMeters) * 0.5;

    /// <summary>
    /// A phase-volume-equivalent depth within the first unresolved cell.
    /// Uses the local equilibrium melt fraction as a sub-cell solid-volume
    /// proxy, not a resolved moving Stefan interface. The result is bounded
    /// by the contiguous-solid surface depth and the next cell bottom.
    /// </summary>
    public static double EstimatePhaseWeightedFrontMeters(
        PlanetOriginRockColumn column, PlanetOriginSurfaceRockPhaseMaterial material,
        PlanetOriginRockColumnProperties properties)
    {
        ArgumentNullException.ThrowIfNull(column);
        properties.Validate();
        var fullySolidMeters = 0.0;
        foreach (var layer in column.Layers)
        {
            var melt = layer.Resolve(material).MeltFraction;
            var thickness = layer.VolumeCubicMeters(material, properties) / column.AreaSquareMeters;
            if (melt == 0.0)
            {
                fullySolidMeters += thickness;
                continue;
            }

            return fullySolidMeters + thickness * (1.0 - melt);
        }

        return fullySolidMeters;
    }

    public void Validate()
    {
        if (LayerCount is < 2 or > 64 || FullySolidTopLayers < 0 || FullySolidTopLayers > LayerCount ||
            !double.IsFinite(FullySolidTopMeters) || FullySolidTopMeters < 0.0 ||
            !double.IsFinite(FirstUnresolvedCellBottomMeters) || FirstUnresolvedCellBottomMeters < FullySolidTopMeters ||
            !double.IsFinite(SurfaceTemperatureKelvin) || SurfaceTemperatureKelvin <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(FullySolidTopMeters), "Depth-grid diagnostics must be finite and physical.");
    }
}

/// <summary>A provisional screening outcome; passing does not prove spatial convergence.</summary>
public readonly record struct PlanetOriginRockColumnResolutionScreen(
    bool MeetsScreeningCriteria, double FinestFrontDifferenceMeters,
    double FinestSurfaceTemperatureDifferenceKelvin, double FinestUnresolvedCellWidthMeters);

public static class PlanetOriginRockColumnDepthRefinement
{
    /// <summary>
    /// Screen the same location/time/boundary hypothesis at 4+ successive
    /// doubling depth resolutions. Callers must enforce matching model inputs.
    /// A multi-cell solid zone, a narrow unresolved cell and agreement of both
    /// temperature and depth are necessary, not sufficient, for convergence.
    /// </summary>
    public static PlanetOriginRockColumnResolutionScreen Screen(
        IReadOnlyList<PlanetOriginRockColumnResolutionSample> samples,
        double maximumFrontDifferenceMeters = 0.02,
        double maximumSurfaceTemperatureDifferenceKelvin = 10.0)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count < 4 || !double.IsFinite(maximumFrontDifferenceMeters) || maximumFrontDifferenceMeters <= 0.0 ||
            !double.IsFinite(maximumSurfaceTemperatureDifferenceKelvin) || maximumSurfaceTemperatureDifferenceKelvin <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(samples), "Four grids and positive screening thresholds are required.");

        for (var i = 0; i < samples.Count; i++)
        {
            samples[i].Validate();
            if (i > 0 && samples[i].LayerCount != samples[i - 1].LayerCount * 2)
                throw new ArgumentException("Depth resolutions must increase by a factor of two.", nameof(samples));
        }

        var last = samples[^1];
        var previous = samples[^2];
        var frontDifference = Math.Abs(last.FullySolidTopMeters - previous.FullySolidTopMeters);
        var temperatureDifference = Math.Abs(last.SurfaceTemperatureKelvin - previous.SurfaceTemperatureKelvin);
        var width = last.UnresolvedCellWidthMeters;
        // If the grids do not even agree on an interval containing the front,
        // comparing their fully solid cell counts is insufficient evidence.
        var commonLowerBound = samples.Max(sample => sample.FrontBoundsMeters.MinimumMeters);
        var commonUpperBound = samples.Min(sample => sample.FrontBoundsMeters.MaximumMeters);
        var intervalsOverlap = commonLowerBound <= commonUpperBound;
        var meets = intervalsOverlap && last.FullySolidTopLayers >= 3 &&
            frontDifference <= maximumFrontDifferenceMeters &&
            temperatureDifference <= maximumSurfaceTemperatureDifferenceKelvin && width <= maximumFrontDifferenceMeters;
        return new(meets, frontDifference, temperatureDifference, width);
    }
}
