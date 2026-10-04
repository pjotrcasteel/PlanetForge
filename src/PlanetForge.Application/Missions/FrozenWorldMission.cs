using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Domain.Physics;

namespace PlanetForge.Application.Missions;

public sealed class FrozenWorldMission(PlanetExperience planetExperience)
{
    public const int StartingBudget = 100;
    public const int TurnYears = 25;
    public const int MaximumMissionYears = 250;
    public const int RequiredStableYears = 50;
    public const string Title = "The Frozen World";
    public const string Objective = "Create stable liquid-water conditions and keep them for 50 years.";

    private static readonly IReadOnlyList<MissionInterventionDefinition> InterventionDefinitions =
    [
        new(
            MissionInterventionType.ReleaseCapturedCarbon,
            "Release captured CO₂",
            25,
            "Double atmospheric CO₂ from stored carbon reserves.",
            "CO₂ ×2 · roughly +3.7 W/m² radiative forcing",
            "MODERATE WARMING",
            "More CO₂ increases infrared radiative forcing, raising the climate target temperature.",
            "Cheap and powerful, but repeated releases can overshoot into excessive warming."),
        new(
            MissionInterventionType.DarkenSurface,
            "Darken exposed surface",
            20,
            "Reduce base Bond albedo by three percentage points.",
            "Base albedo −3 pp · more incoming starlight absorbed",
            "MODERATE WARMING",
            "A darker surface reflects less incoming starlight and absorbs more energy.",
            "Works against a bright frozen world, but permanently changes the surface energy balance."),
        new(
            MissionInterventionType.OrbitalTransfer,
            "Orbital transfer campaign",
            95,
            "Move the planet 0.10 AU closer to its star.",
            "Orbit −0.10 AU · stellar flux rises sharply",
            "VERY STRONG WARMING",
            "Stellar flux follows the inverse-square law, so a smaller orbit receives substantially more energy.",
            "An extreme megaproject: highly effective, consumes almost the entire mission budget, and is difficult to reverse."),
    ];

    private readonly List<MissionInterventionType> plannedInterventions = [];
    private PlanetRenderSnapshot? currentPlanet;
    private MissionPrediction? prediction;
    private MissionTurnFeedback? lastTurn;
    private int budgetRemaining;
    private int missionYearsElapsed;
    private int stableYears;
    private MissionStatus status;

    public IReadOnlyList<MissionInterventionDefinition> Interventions => InterventionDefinitions;

    public MissionSnapshot Start()
    {
        planetExperience.ResetEarthReference();
        planetExperience.MoveOrbitOutward();
        planetExperience.AdvanceClimate(50.0);
        currentPlanet = planetExperience.CreateInitialRenderSnapshot();
        budgetRemaining = StartingBudget;
        missionYearsElapsed = 0;
        stableYears = 0;
        status = MissionStatus.Active;
        prediction = null;
        lastTurn = null;
        plannedInterventions.Clear();
        return CreateSnapshot();
    }

    public MissionSnapshot Restart() => Start();

    public MissionSnapshot QueueIntervention(MissionInterventionType type)
    {
        EnsureActive();
        var definition = GetDefinition(type);
        if (budgetRemaining < definition.Cost)
        {
            throw new InvalidOperationException($"{definition.Name} costs {definition.Cost} credits, but only {budgetRemaining} remain available.");
        }

        plannedInterventions.Add(type);
        budgetRemaining -= definition.Cost;
        prediction = null;
        return CreateSnapshot();
    }

    public MissionSnapshot ClearPlan()
    {
        EnsureActive();
        budgetRemaining += plannedInterventions.Sum(type => GetDefinition(type).Cost);
        plannedInterventions.Clear();
        prediction = null;
        return CreateSnapshot();
    }

    public MissionSnapshot SelectPrediction(MissionPrediction selectedPrediction)
    {
        EnsureActive();
        prediction = selectedPrediction;
        return CreateSnapshot();
    }

    public MissionSnapshot SimulateTurn()
    {
        EnsureActive();
        if (prediction is null)
        {
            throw new InvalidOperationException("Choose a prediction before advancing the simulation.");
        }

        var before = CurrentPlanet;
        ApplyPlan();
        currentPlanet = planetExperience.AdvanceClimate(TurnYears);
        missionYearsElapsed += TurnYears;
        lastTurn = CreateFeedback(before, currentPlanet, prediction.Value);
        plannedInterventions.Clear();
        prediction = null;

        stableYears = HasStableLiquidWater(currentPlanet) ? stableYears + TurnYears : 0;
        if (stableYears >= RequiredStableYears)
        {
            status = MissionStatus.Won;
        }
        else if (missionYearsElapsed >= MaximumMissionYears)
        {
            status = MissionStatus.Failed;
        }

        return CreateSnapshot();
    }

    public bool CanAfford(MissionInterventionType type) => budgetRemaining >= GetDefinition(type).Cost;

    public MissionInterventionDefinition GetDefinition(MissionInterventionType type)
        => InterventionDefinitions.Single(definition => definition.Type == type);

    private PlanetRenderSnapshot CurrentPlanet => currentPlanet ?? throw new InvalidOperationException("Start the mission before using it.");

    private void ApplyPlan()
    {
        foreach (var intervention in plannedInterventions)
        {
            currentPlanet = intervention switch
            {
                MissionInterventionType.ReleaseCapturedCarbon => planetExperience.DoubleCarbonDioxide(),
                MissionInterventionType.DarkenSurface => planetExperience.DarkenSurface(),
                MissionInterventionType.OrbitalTransfer => planetExperience.MoveOrbitInward(),
                _ => throw new ArgumentOutOfRangeException(nameof(intervention), intervention, "Unknown mission intervention."),
            };
        }
    }

    private MissionSnapshot CreateSnapshot()
        => new(
            CurrentPlanet,
            budgetRemaining,
            missionYearsElapsed,
            stableYears,
            status,
            plannedInterventions.ToArray(),
            prediction,
            lastTurn);

    private static MissionTurnFeedback CreateFeedback(PlanetRenderSnapshot before, PlanetRenderSnapshot after, MissionPrediction selectedPrediction)
    {
        var temperatureDelta = after.Climate.SurfaceTemperatureKelvin - before.Climate.SurfaceTemperatureKelvin;
        var cryosphereDelta = after.ClimateFeedback.CryosphereFraction - before.ClimateFeedback.CryosphereFraction;
        var albedoDelta = after.ClimateFeedback.EffectiveBondAlbedo - before.ClimateFeedback.EffectiveBondAlbedo;
        var actualPrediction = ClassifyTemperatureChange(temperatureDelta);
        var predictionCorrect = selectedPrediction == actualPrediction;
        var headline = predictionCorrect ? "Your prediction matched the simulation." : $"The planet became {DescribePrediction(actualPrediction)} instead.";
        var temperatureSentence = $"Global mean surface temperature changed by {temperatureDelta:+0.0;-0.0;0.0}°C over {TurnYears} years.";
        var feedbackSentence = DescribeFeedback(cryosphereDelta, albedoDelta);
        return new MissionTurnFeedback(
            predictionCorrect,
            headline,
            $"{temperatureSentence} {feedbackSentence}",
            before.Climate.SurfaceTemperatureKelvin,
            after.Climate.SurfaceTemperatureKelvin,
            before.ClimateFeedback.CryosphereFraction,
            after.ClimateFeedback.CryosphereFraction,
            before.Water.LiquidFraction,
            after.Water.LiquidFraction,
            before.ClimateFeedback.EffectiveBondAlbedo,
            after.ClimateFeedback.EffectiveBondAlbedo);
    }

    private static MissionPrediction ClassifyTemperatureChange(double temperatureDeltaKelvin)
    {
        if (temperatureDeltaKelvin > 0.5)
        {
            return MissionPrediction.Warmer;
        }

        if (temperatureDeltaKelvin < -0.5)
        {
            return MissionPrediction.Cooler;
        }

        return MissionPrediction.LittleChange;
    }

    private static string DescribePrediction(MissionPrediction value)
        => value switch
        {
            MissionPrediction.Warmer => "warmer",
            MissionPrediction.Cooler => "cooler",
            MissionPrediction.LittleChange => "almost unchanged",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown prediction."),
        };

    private static string DescribeFeedback(double cryosphereDelta, double albedoDelta)
    {
        if (cryosphereDelta < -0.02)
        {
            return $"Cryosphere cover retreated by {-cryosphereDelta * 100.0:0.0} percentage points, reducing effective albedo by {-albedoDelta * 100.0:0.0} points and reinforcing warming.";
        }

        if (cryosphereDelta > 0.02)
        {
            return $"Cryosphere cover expanded by {cryosphereDelta * 100.0:0.0} percentage points, increasing effective albedo by {albedoDelta * 100.0:0.0} points and reinforcing cooling.";
        }

        return "Cryosphere cover changed little, so ice-albedo feedback was weak during this turn.";
    }

    private static bool HasStableLiquidWater(PlanetRenderSnapshot planet)
        => planet.Climate.SurfaceTemperatureKelvin >= PhysicalConstants.KelvinOffsetCelsius
            && planet.Climate.SurfaceTemperatureKelvin <= PhysicalConstants.KelvinOffsetCelsius + 30.0
            && planet.Water.LiquidFraction >= 0.5
            && planet.ClimateFeedback.CryosphereFraction <= 0.35;

    private void EnsureActive()
    {
        if (status != MissionStatus.Active)
        {
            throw new InvalidOperationException("Restart the mission before making another move.");
        }
    }
}