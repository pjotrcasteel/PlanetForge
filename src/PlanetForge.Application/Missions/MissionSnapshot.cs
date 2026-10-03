using PlanetForge.Application.Rendering;

namespace PlanetForge.Application.Missions;

public sealed record MissionSnapshot(
    PlanetRenderSnapshot Planet,
    int BudgetRemaining,
    int MissionYearsElapsed,
    int StableYears,
    MissionStatus Status,
    IReadOnlyList<MissionInterventionType> PlannedInterventions,
    MissionPrediction? Prediction,
    MissionTurnFeedback? LastTurn);