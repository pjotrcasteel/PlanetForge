namespace PlanetForge.Application.Runs;

public sealed record PlanetRunEraDefinition(
    PlanetRunEra Era,
    string Name,
    string Goal,
    string ScientificPrerequisite);