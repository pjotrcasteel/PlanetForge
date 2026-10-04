namespace PlanetForge.Application.Runs;

public sealed record PlanetJournalEntry(
    string Key,
    double Year,
    string Title,
    string Description,
    int InsightAwarded);