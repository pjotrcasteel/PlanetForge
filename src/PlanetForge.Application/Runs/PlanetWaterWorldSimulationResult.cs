namespace PlanetForge.Application.Runs;

public sealed record PlanetWaterWorldSimulationResult(
    PlanetRunSnapshot Run,
    IReadOnlyList<PlanetWaterCycleFrame> Frames);