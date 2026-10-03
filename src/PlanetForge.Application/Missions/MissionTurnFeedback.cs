namespace PlanetForge.Application.Missions;

public sealed record MissionTurnFeedback(
    bool PredictionCorrect,
    string Headline,
    string Explanation,
    double TemperatureDeltaKelvin,
    double CryosphereDelta,
    double EffectiveAlbedoDelta);