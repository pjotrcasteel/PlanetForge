using PlanetForge.Application.Planets;

namespace PlanetForge.Application.Missions;

public sealed record FrozenWorldMissionState(
    PlanetExperienceState Planet,
    int BudgetRemaining,
    int MissionYearsElapsed,
    int StableYears,
    MissionStatus Status,
    IReadOnlyList<MissionInterventionType> PlannedInterventions,
    MissionPrediction? Prediction,
    MissionTurnFeedback? LastTurn);