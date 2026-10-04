namespace PlanetForge.Application.Runs;

public sealed record PlanetResearchUnlockDefinition(
    PlanetResearchUnlock Type,
    string Name,
    string Capability,
    string ScientificBasis,
    int InsightCost);