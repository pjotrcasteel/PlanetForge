namespace PlanetForge.Domain.Origins;

public enum PlanetScenarioId
{
    EarthOrigin = 0,
    FrozenClimateExperiment = 1
}

public enum PlanetScenarioTimeBasis
{
    YearsBeforePresent = 0,
    YearsSinceExperimentStart = 1
}

/// <summary>
/// A scenario states its time basis and scientific purpose. It never implies
/// that an unavailable reconstruction is already implemented by the UI.
/// </summary>
public sealed record PlanetScenarioDefinition(
    PlanetScenarioId Id,
    string Name,
    string Description,
    PlanetScenarioTimeBasis TimeBasis,
    bool IsRuntimeAvailable);

public static class PlanetScenarioCatalog
{
    public static PlanetScenarioDefinition EarthOrigin { get; } = new(
        PlanetScenarioId.EarthOrigin,
        "Earth Origin",
        "Evidence-informed reconstruction from the Solar System's formation through early Earth to plausible conditions for first life.",
        PlanetScenarioTimeBasis.YearsBeforePresent,
        false);

    public static PlanetScenarioDefinition FrozenClimateExperiment { get; } = new(
        PlanetScenarioId.FrozenClimateExperiment,
        "Frozen Climate Experiment",
        "Artificial snowball-like starting conditions with interventions that warm a rocky planet; not a reconstruction of early Earth.",
        PlanetScenarioTimeBasis.YearsSinceExperimentStart,
        true);

    public static PlanetScenarioDefinition IntendedDefault => EarthOrigin;

    public static PlanetScenarioDefinition CurrentPlayable => FrozenClimateExperiment;

    public static PlanetScenarioDefinition Get(PlanetScenarioId scenario) => scenario switch
    {
        PlanetScenarioId.EarthOrigin => EarthOrigin,
        PlanetScenarioId.FrozenClimateExperiment => FrozenClimateExperiment,
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown planet scenario.")
    };
}
