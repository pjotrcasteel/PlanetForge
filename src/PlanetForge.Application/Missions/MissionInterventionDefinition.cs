namespace PlanetForge.Application.Missions;

public sealed record MissionInterventionDefinition(
    MissionInterventionType Type,
    string Name,
    int Cost,
    string Summary,
    string Mechanism,
    string TradeOff);