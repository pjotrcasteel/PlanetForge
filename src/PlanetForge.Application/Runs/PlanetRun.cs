using PlanetForge.Application.Missions;
using PlanetForge.Application.Surface.Hydrology;

namespace PlanetForge.Application.Runs;

public sealed class PlanetRun(
    FrozenWorldMission frozenWorldMission,
    PlanetHydrologyModelBuilder hydrologyModelBuilder,
    PlanetHydrologyFeatureExtractor hydrologyFeatureExtractor)
{
    public const int SaveSchemaVersion = 2;

    private const int WaterSurveyGridLevel = 5;
    private const long MinimumRiverContributingLandCells = 8;

    private static readonly IReadOnlyList<PlanetRunEraDefinition> EraDefinitions =
    [
        new(PlanetRunEra.DeadRock, "Dead Rock", "Create persistent surface liquid water.", "A rocky planet with a climate that can support persistent surface liquid water."),
        new(PlanetRunEra.WaterWorld, "Water World", "Map where liquid water can persist and flow across the terrain.", "Stable liquid water plus a spatial water-cycle model with drainage, lakes and runoff."),
        new(PlanetRunEra.FirstLife, "First Life", "Establish conditions in which life could plausibly originate and persist.", "Prebiotic chemistry, sustained free-energy gradients and a scientifically explicit origin-of-life model."),
        new(PlanetRunEra.GreenWorld, "Green World", "Allow photosynthetic ecosystems to transform suitable environments.", "Evolved photosynthetic lineages, available nutrients, suitable climate and persistent habitats."),
        new(PlanetRunEra.AnimalWorld, "Animal World", "Support complex mobile multicellular ecosystems.", "Sufficient ecosystem productivity, oxygen availability where required, and evolved multicellular lineages."),
        new(PlanetRunEra.LivingPlanet, "Living Planet", "Maintain a resilient self-sustaining complex biosphere.", "Long-term biodiversity and ecosystem persistence under changing planetary conditions."),
    ];

    private static readonly IReadOnlyList<PlanetResearchUnlockDefinition> ResearchDefinitions =
    [
        new(
            PlanetResearchUnlock.AtmosphericSpectroscopy,
            "Atmospheric Spectroscopy",
            "Unlock detailed atmospheric composition and radiative-forcing analysis.",
            "Spectroscopy infers atmospheric composition from wavelength-dependent absorption and emission.",
            8),
        new(
            PlanetResearchUnlock.SurfaceRadiometry,
            "Surface Radiometry",
            "Unlock a detailed reflected-versus-absorbed stellar-energy diagnostic.",
            "Radiometry measures electromagnetic energy and is directly related to planetary energy balance and albedo.",
            8),
        new(
            PlanetResearchUnlock.HydrologicalSurvey,
            "Hydrological Survey",
            "Unlock detailed water inventory diagnostics for the Water World era.",
            "Hydrology constrains where liquid water collects and how topography routes surface flow.",
            8),
    ];

    private readonly List<PlanetJournalEntry> journal = [];
    private readonly List<PlanetResearchUnlock> researchUnlocks = [];
    private MissionSnapshot? mission;
    private PlanetRunEra era;
    private int insight;
    private bool researchChoiceAvailable;
    private PlanetWaterSurvey? waterSurvey;

    public IReadOnlyList<PlanetRunEraDefinition> Eras => EraDefinitions;

    public IReadOnlyList<MissionInterventionDefinition> Interventions => frozenWorldMission.Interventions;

    public PlanetRunSnapshot StartNew()
    {
        mission = frozenWorldMission.Start();
        era = PlanetRunEra.DeadRock;
        insight = 0;
        researchChoiceAvailable = false;
        waterSurvey = null;
        journal.Clear();
        researchUnlocks.Clear();
        AddJournalEntry(
            "run-start",
            "Planetary survey begins",
            "A frozen rocky world has been selected for long-term observation and intervention. The first objective is persistent surface liquid water.",
            0);
        return CreateSnapshot();
    }

    public PlanetRunSnapshot Restart() => StartNew();

    public PlanetRunSnapshot QueueIntervention(MissionInterventionType type)
    {
        mission = frozenWorldMission.QueueIntervention(type);
        return CreateSnapshot();
    }

    public PlanetRunSnapshot ClearPlan()
    {
        mission = frozenWorldMission.ClearPlan();
        return CreateSnapshot();
    }

    public PlanetRunSnapshot SelectPrediction(MissionPrediction prediction)
    {
        mission = frozenWorldMission.SelectPrediction(prediction);
        return CreateSnapshot();
    }

    public PlanetRunSnapshot SimulateTurn()
    {
        mission = frozenWorldMission.SimulateTurn();
        EvaluateDiscoveries();
        return CreateSnapshot();
    }

    public PlanetRunCommitResult SimulateCommit()
    {
        var result = frozenWorldMission.SimulateCommit();
        mission = result.Mission;
        EvaluateDiscoveries();
        return new PlanetRunCommitResult(CreateSnapshot(), result.Frames);
    }

    public PlanetRunSnapshot SurveyWaterWorld(CancellationToken cancellationToken)
    {
        if (era != PlanetRunEra.WaterWorld)
        {
            throw new InvalidOperationException("A hydrological survey requires the Water World era.");
        }

        if (waterSurvey is not null)
        {
            return CreateSnapshot();
        }

        var planet = CurrentMission.Planet;
        var hydrology = hydrologyModelBuilder.Build(
            WaterSurveyGridLevel,
            planet.Seed,
            planet.PhysicalParameters.RadiusMeters,
            planet.SeaLevelMeters,
            cancellationToken);
        var features = hydrologyFeatureExtractor.Extract(hydrology, MinimumRiverContributingLandCells, cancellationToken);
        waterSurvey = new PlanetWaterSurvey(
            WaterSurveyGridLevel,
            features.Watersheds.Count,
            features.Lakes.Count,
            features.RiverSegments.Count);
        AddJournalEntry(
            "water-pathways-mapped",
            "Potential water pathways mapped",
            $"Topography resolves {waterSurvey.WatershedCount} drainage basins, {waterSurvey.LakeCount} closed depressions and {waterSurvey.PotentialRiverSegmentCount} potential high-flow drainage segments. This is terrain-routing potential, not yet a rainfall or discharge simulation.",
            6);
        return CreateSnapshot();
    }

    public bool CanAfford(MissionInterventionType type) => frozenWorldMission.CanAfford(type);

    public MissionInterventionDefinition GetDefinition(MissionInterventionType type) => frozenWorldMission.GetDefinition(type);

    public PlanetRunSnapshot SelectResearch(PlanetResearchUnlock unlock)
    {
        if (!researchChoiceAvailable)
        {
            throw new InvalidOperationException("No research choice is currently available.");
        }

        var definition = ResearchDefinitions.Single(value => value.Type == unlock);
        if (insight < definition.InsightCost)
        {
            throw new InvalidOperationException($"{definition.Name} requires {definition.InsightCost} Insight, but only {insight} is available.");
        }

        insight -= definition.InsightCost;
        researchUnlocks.Add(unlock);
        researchChoiceAvailable = false;
        AddJournalEntry(
            $"research-{unlock}",
            $"Research capability unlocked: {definition.Name}",
            definition.Capability,
            0);
        return CreateSnapshot();
    }

    public PlanetRunSave ExportSave()
        => new(
            SaveSchemaVersion,
            frozenWorldMission.ExportState(),
            era,
            insight,
            journal.ToArray(),
            researchUnlocks.ToArray(),
            researchChoiceAvailable,
            waterSurvey);

    public PlanetRunSnapshot Restore(PlanetRunSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (save.SchemaVersion != SaveSchemaVersion)
        {
            throw new NotSupportedException($"Planet Run save schema {save.SchemaVersion} is not supported by schema {SaveSchemaVersion}.");
        }

        mission = frozenWorldMission.RestoreState(save.Mission);
        era = save.Era;
        insight = save.Insight;
        researchChoiceAvailable = save.ResearchChoiceAvailable;
        waterSurvey = save.WaterSurvey;
        journal.Clear();
        journal.AddRange(save.Journal);
        researchUnlocks.Clear();
        researchUnlocks.AddRange(save.ResearchUnlocks);
        return CreateSnapshot();
    }

    private void EvaluateDiscoveries()
    {
        var currentMission = CurrentMission;
        var lastTurn = currentMission.LastTurn;
        if (lastTurn is not null && Math.Abs(lastTurn.CryosphereDelta) >= 0.02 && !HasJournalEntry("ice-albedo-measured"))
        {
            AddJournalEntry(
                "ice-albedo-measured",
                "Ice-albedo feedback measured",
                "The climate response changed cryosphere cover enough to measurably alter effective planetary albedo, demonstrating a positive climate feedback.",
                4);
        }

        if (currentMission.Status == MissionStatus.Won && era == PlanetRunEra.DeadRock)
        {
            era = PlanetRunEra.WaterWorld;
            AddJournalEntry(
                "stable-surface-water",
                "Persistent surface liquid water established",
                "Surface temperature, liquid-water fraction and cryosphere state remained within the stability criteria for fifty consecutive simulated years.",
                10);
            researchChoiceAvailable = true;
        }

        if (currentMission.Status == MissionStatus.Failed && !HasJournalEntry("dead-rock-attempt-failed"))
        {
            AddJournalEntry(
                "dead-rock-attempt-failed",
                "Dead Rock objective not achieved",
                "The intervention window ended before persistent liquid-water conditions were maintained for fifty consecutive years.",
                0);
        }
    }

    private void AddJournalEntry(string key, string title, string description, int insightAwarded)
    {
        if (HasJournalEntry(key))
        {
            return;
        }

        journal.Add(new PlanetJournalEntry(key, CurrentMission.MissionYearsElapsed, title, description, insightAwarded));
        insight += insightAwarded;
    }

    private bool HasJournalEntry(string key) => journal.Any(entry => entry.Key == key);

    private MissionSnapshot CurrentMission => mission ?? throw new InvalidOperationException("Start or restore the Planet Run before using it.");

    private PlanetRunSnapshot CreateSnapshot()
    {
        var choices = researchChoiceAvailable
            ? ResearchDefinitions.Where(definition => !researchUnlocks.Contains(definition.Type)).ToArray()
            : Array.Empty<PlanetResearchUnlockDefinition>();
        return new PlanetRunSnapshot(
            CurrentMission,
            era,
            insight,
            journal.ToArray(),
            researchUnlocks.ToArray(),
            choices,
            researchChoiceAvailable,
            waterSurvey);
    }
}