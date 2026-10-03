using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Planets;

namespace PlanetForge.Domain.Climate;

public sealed record ClimateFeedbackResult(
    ClimateFeedbackState State,
    PlanetPhysicsSnapshot Physics,
    ClimateSnapshot Climate,
    WaterPhaseSnapshot Water,
    ClimateFeedbackSnapshot Feedback);