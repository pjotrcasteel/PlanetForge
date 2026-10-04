namespace PlanetForge.Application.Missions;

public sealed record MissionTurnFeedback(
    bool PredictionCorrect,
    string Headline,
    string Explanation,
    IReadOnlyList<MissionInterventionType> InterventionsApplied,
    int CreditsSpent,
    double TemperatureBeforeKelvin,
    double TemperatureAfterKelvin,
    double CryosphereBefore,
    double CryosphereAfter,
    double LiquidWaterBefore,
    double LiquidWaterAfter,
    double EffectiveAlbedoBefore,
    double EffectiveAlbedoAfter)
{
    public double TemperatureDeltaKelvin => TemperatureAfterKelvin - TemperatureBeforeKelvin;

    public double CryosphereDelta => CryosphereAfter - CryosphereBefore;

    public double LiquidWaterDelta => LiquidWaterAfter - LiquidWaterBefore;

    public double EffectiveAlbedoDelta => EffectiveAlbedoAfter - EffectiveAlbedoBefore;
}