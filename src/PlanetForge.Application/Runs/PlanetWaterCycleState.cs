using System.Text.Json.Serialization;

namespace PlanetForge.Application.Runs;

public sealed record PlanetWaterCycleState
{
    private const double RiverNetworkMaturationYears = 260.0;
    private const double LakeMaturationYears = 360.0;
    private const double InitialDischargeScale = 0.22;

    [JsonConstructor]
    public PlanetWaterCycleState()
    {
    }

    public PlanetWaterCycleState(
        int simulatedYears,
        double annualPrecipitationMillimeters,
        double annualRunoffMillimeters,
        double lakeFillFraction,
        double riverActivationFraction,
        int activeLakeCount,
        int activeRiverSegmentCount,
        IReadOnlyList<PlanetWaterPathSegment> activeRiverSegments)
    {
        SimulatedYears = simulatedYears;
        AnnualPrecipitationMillimeters = annualPrecipitationMillimeters;
        AnnualRunoffMillimeters = annualRunoffMillimeters;
        AnnualPrecipitationRunoffMillimeters = annualRunoffMillimeters;

        var riverMaturity = MaturationResponse(simulatedYears, RiverNetworkMaturationYears);
        var lakeMaturity = MaturationResponse(simulatedYears, LakeMaturationYears);
        LakeFillFraction = Math.Clamp(lakeFillFraction * lakeMaturity, 0.0, 1.0);
        RiverActivationFraction = Math.Clamp(riverActivationFraction * riverMaturity, 0.0, 1.0);
        ActiveLakeCount = ScaleCount(activeLakeCount, lakeMaturity);

        var availableRiverCount = Math.Min(activeRiverSegmentCount, activeRiverSegments.Count);
        var maturedRiverCount = ScaleCount(availableRiverCount, riverMaturity);
        var dischargeScale = InitialDischargeScale + ((1.0 - InitialDischargeScale) * riverMaturity);
        ActiveRiverSegments = activeRiverSegments
            .Take(maturedRiverCount)
            .Select(segment => segment with
            {
                RelativeDischarge = Math.Clamp(segment.RelativeDischarge * dischargeScale, 0.08, 1.0),
                MeanDischargeCubicMetersPerSecond = segment.MeanDischargeCubicMetersPerSecond * dischargeScale,
            })
            .ToArray();
        ActiveRiverSegmentCount = ActiveRiverSegments.Count;
    }

    public int SimulatedYears { get; init; }

    public double AnnualPrecipitationMillimeters { get; init; }

    public double AnnualRunoffMillimeters { get; init; }

    public double AnnualPrecipitationRunoffMillimeters { get; init; }

    public double AnnualMeltwaterRunoffMillimeters { get; init; }

    public double LakeFillFraction { get; init; }

    public double RiverActivationFraction { get; init; }

    public int ActiveLakeCount { get; init; }

    public int ActiveRiverSegmentCount { get; init; }

    public IReadOnlyList<PlanetWaterPathSegment> ActiveRiverSegments { get; init; } = [];

    public IReadOnlyList<PlanetWaterLakeCell> ActiveLakeCells { get; init; } = [];

    private static double MaturationResponse(int simulatedYears, double timeScaleYears)
        => 1.0 - Math.Exp(-Math.Max(simulatedYears, 0) / timeScaleYears);

    private static int ScaleCount(int availableCount, double fraction)
        => availableCount == 0 ? 0 : Math.Clamp((int)Math.Round(availableCount * fraction), 1, availableCount);
}
