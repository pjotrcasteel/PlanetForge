namespace PlanetForge.Application.Runs;

public sealed record PlanetWaterCycleState(
    int SimulatedYears,
    double AnnualPrecipitationMillimeters,
    double AnnualRunoffMillimeters,
    double LakeFillFraction,
    double RiverActivationFraction,
    int ActiveLakeCount,
    int ActiveRiverSegmentCount,
    IReadOnlyList<PlanetWaterPathSegment> ActiveRiverSegments);