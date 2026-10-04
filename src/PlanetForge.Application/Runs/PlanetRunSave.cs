using PlanetForge.Application.Missions;

namespace PlanetForge.Application.Runs;

public sealed record PlanetRunSave(
    int SchemaVersion,
    FrozenWorldMissionState Mission,
    PlanetRunEra Era,
    int Insight,
    IReadOnlyList<PlanetJournalEntry> Journal,
    IReadOnlyList<PlanetResearchUnlock> ResearchUnlocks,
    bool ResearchChoiceAvailable,
    PlanetWaterSurvey? WaterSurvey);