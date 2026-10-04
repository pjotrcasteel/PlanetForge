namespace PlanetForge.Application.Missions;

public sealed record MissionInterventionDefinition(
    MissionInterventionType Type,
    string Name,
    int Cost,
    string Summary,
    string EffectPreview,
    string ImpactLabel,
    string Mechanism,
    string TradeOff);