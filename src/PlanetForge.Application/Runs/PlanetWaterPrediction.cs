namespace PlanetForge.Application.Runs;

public enum PlanetWaterPrediction
{
    RunoffFollowsRidges,
    RunoffConcentratesInDrainage,
    RunoffSpreadsUniformly,
    BasinsDelayDownstreamFlow,
    BasinsDrainImmediately,
    BasinsDoNotAffectRunoff,
    DownstreamDischargeGrows,
    DownstreamDischargeStaysConstant,
    DownstreamDischargeFalls,
}
