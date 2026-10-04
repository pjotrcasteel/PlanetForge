using PlanetForge.Application.Missions;

namespace PlanetForge.Application.Runs;

public sealed record PlanetRunSnapshot(
    MissionSnapshot Mission,
    PlanetRunEra Era,
    int Insight,
    IReadOnlyList<PlanetJournalEntry> Journal,
    IReadOnlyList<PlanetResearchUnlock> ResearchUnlocks,
    IReadOnlyList<PlanetResearchUnlockDefinition> ResearchChoices,
    bool ResearchChoiceAvailable);