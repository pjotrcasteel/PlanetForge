namespace PlanetForge.Application.Runs;

public sealed record PlanetWaterSurvey(
    int GridLevel,
    int WatershedCount,
    int LakeCount,
    int PotentialRiverSegmentCount);