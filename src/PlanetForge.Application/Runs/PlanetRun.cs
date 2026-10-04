using PlanetForge.Application.Missions;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Physics;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Runs;

public sealed class PlanetRun(
    FrozenWorldMission frozenWorldMission,
    PlanetHydrologyModelBuilder hydrologyModelBuilder,
    PlanetHydrologyFeatureExtractor hydrologyFeatureExtractor)
{
    public const int SaveSchemaVersion = 3;

    private const int WaterSurveyGridLevel = 6;
    private const long MinimumRiverContributingLandCells = 32;
    private const double ReferenceAnnualPrecipitationMillimeters = 950.0;
    private static readonly int[] WaterCycleFrameYears = [1, 5, 20, 50, 100];

    private static readonly IReadOnlyList<PlanetRunEraDefinition> EraDefinitions =
    [
        new(PlanetRunEra.DeadRock, "Dead Rock", "Create persistent surface liquid water.", "A rocky planet with a climate that can support persistent surface liquid water."),
        new(PlanetRunEra.WaterWorld, "Water World", "Establish a persistent water cycle with precipitation, basin filling and connected runoff.", "Stable liquid water plus a spatial water-cycle model with precipitation, runoff, lakes and active drainage."),
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
    private PlanetWaterCycleState? waterCycle;

    public IReadOnlyList<PlanetRunEraDefinition> Eras => EraDefinitions;

    public IReadOnlyList<MissionInterventionDefinition> Interventions => frozenWorldMission.Interventions;

    public PlanetRunSnapshot StartNew()
    {
        mission = frozenWorldMission.Start();
        era = PlanetRunEra.DeadRock;
        insight = 0;
        researchChoiceAvailable = false;
        waterSurvey = null;
        waterCycle = null;
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
        EnsureWaterWorld();
        EnsureWaterSurvey(cancellationToken);
        return CreateSnapshot();
    }

    public PlanetWaterWorldSimulationResult SimulateWaterWorld(CancellationToken cancellationToken)
    {
        EnsureWaterWorld();
        var features = BuildWaterFeatures(cancellationToken);
        EnsureWaterSurvey(features);

        var planet = CurrentMission.Planet;
        var precipitation = EstimateAnnualPrecipitationMillimeters(planet);
        var runoff = EstimateAnnualRunoffMillimeters(precipitation);
        var startYear = waterCycle?.SimulatedYears ?? 0;
        var frames = WaterCycleFrameYears
            .Select(frameYears => BuildWaterCycleFrame(startYear + frameYears, precipitation, runoff, features, planet.PhysicalParameters.RadiusMeters))
            .ToArray();

        waterCycle = frames[^1].State;
        if (!HasJournalEntry("first-precipitation"))
        {
            AddJournalEntryAt(
                CurrentMission.MissionYearsElapsed + frames[0].Year,
                "first-precipitation",
                "The first persistent precipitation cycle begins",
                "Liquid surface water now participates in a reduced global water-cycle model: evaporation supplies atmospheric moisture, precipitation returns it to the surface, and topography routes the runoff.",
                4);
        }

        if (waterCycle.ActiveRiverSegmentCount > 0 && !HasJournalEntry("active-runoff-network"))
        {
            AddJournalEntryAt(
                CurrentMission.MissionYearsElapsed + waterCycle.SimulatedYears,
                "active-runoff-network",
                "A connected runoff network becomes persistent",
                $"The reduced hydrology model now supplies runoff to {waterCycle.ActiveRiverSegmentCount} terrain-derived drainage segments and fills {waterCycle.ActiveLakeCount} closed depressions strongly enough to persist in the current climate.",
                6);
        }

        return new PlanetWaterWorldSimulationResult(CreateSnapshot(), frames);
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
            waterSurvey,
            waterCycle);

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
        waterCycle = save.WaterCycle;
        journal.Clear();
        journal.AddRange(save.Journal);
        researchUnlocks.Clear();
        researchUnlocks.AddRange(save.ResearchUnlocks);
        return CreateSnapshot();
    }

    private PlanetHydrologyFeatures BuildWaterFeatures(CancellationToken cancellationToken)
    {
        var planet = CurrentMission.Planet;
        var hydrology = hydrologyModelBuilder.Build(
            WaterSurveyGridLevel,
            planet.Seed,
            planet.PhysicalParameters.RadiusMeters,
            planet.SeaLevelMeters,
            cancellationToken);
        return hydrologyFeatureExtractor.Extract(hydrology, MinimumRiverContributingLandCells, cancellationToken);
    }

    private void EnsureWaterSurvey(CancellationToken cancellationToken)
    {
        if (waterSurvey is not null)
        {
            return;
        }

        EnsureWaterSurvey(BuildWaterFeatures(cancellationToken));
    }

    private void EnsureWaterSurvey(PlanetHydrologyFeatures features)
    {
        if (waterSurvey is not null)
        {
            return;
        }

        waterSurvey = new PlanetWaterSurvey(
            WaterSurveyGridLevel,
            features.Watersheds.Count,
            features.Lakes.Count,
            features.RiverSegments.Count);
        AddJournalEntry(
            "water-pathways-mapped",
            "Potential water pathways mapped",
            $"Topography resolves {waterSurvey.WatershedCount} drainage basins, {waterSurvey.LakeCount} closed depressions and {waterSurvey.PotentialRiverSegmentCount} potential high-flow drainage segments. These are dry routing pathways until the water-cycle model supplies runoff.",
            6);
    }

    private static PlanetWaterCycleFrame BuildWaterCycleFrame(
        int year,
        double annualPrecipitationMillimeters,
        double annualRunoffMillimeters,
        PlanetHydrologyFeatures features,
        double planetRadiusMeters)
    {
        var wettingResponse = 1.0 - Math.Exp(-year / 18.0);
        var runoffPotential = Math.Clamp(annualRunoffMillimeters / 350.0, 0.0, 1.0);
        var riverActivation = Math.Clamp(wettingResponse * runoffPotential, 0.0, 1.0);
        var lakeFillResponse = 1.0 - Math.Exp(-year / 30.0);
        var lakeFill = Math.Clamp(lakeFillResponse * Math.Clamp(annualPrecipitationMillimeters / 700.0, 0.0, 1.0), 0.0, 1.0);
        var maximumContribution = features.RiverSegments.Count == 0 ? 1L : features.RiverSegments.Max(segment => segment.ContributingLandCellCount);
        var availableSegments = features.RiverSegments
            .OrderByDescending(segment => segment.ContributingLandCellCount)
            .ThenByDescending(segment => segment.StrahlerOrder)
            .Select(segment => new PlanetWaterPathSegment(
                segment.FromDirection.X,
                segment.FromDirection.Y,
                segment.FromDirection.Z,
                segment.ToDirection.X,
                segment.ToDirection.Y,
                segment.ToDirection.Z,
                Math.Clamp(Math.Sqrt(segment.ContributingLandCellCount / (double)maximumContribution), 0.08, 1.0),
                segment.StrahlerOrder))
            .ToArray();
        var state = new PlanetWaterCycleState(
            year,
            annualPrecipitationMillimeters,
            annualRunoffMillimeters,
            lakeFill,
            riverActivation,
            features.Lakes.Count,
            features.RiverSegments.Count,
            availableSegments);
        return new PlanetWaterCycleFrame(year, state with { ActiveLakeCells = BuildActiveLakeCells(features, state, planetRadiusMeters) });
    }

    private static IReadOnlyList<PlanetWaterLakeCell> BuildActiveLakeCells(
        PlanetHydrologyFeatures features,
        PlanetWaterCycleState state,
        double planetRadiusMeters)
    {
        if (state.ActiveLakeCount == 0 || features.Lakes.Count == 0)
        {
            return [];
        }

        return features.Lakes
            .OrderByDescending(LakeSignificance)
            .ThenByDescending(lake => lake.MaximumDepthMeters)
            .ThenBy(lake => lake.Id)
            .Take(state.ActiveLakeCount)
            .SelectMany(lake => lake.Cells.Select(cell => BuildLakeCell(lake, cell, planetRadiusMeters)))
            .ToArray();
    }

    private static double LakeSignificance(PlanetLake lake) => lake.Cells.Count * Math.Max(lake.MaximumDepthMeters, 1.0);

    private static PlanetWaterLakeCell BuildLakeCell(PlanetLake lake, PlanetSurfaceGridCellId cell, double planetRadiusMeters)
    {
        var direction = PlanetSurfaceGridGeometry.GetCenterDirection(cell);
        var angularRadius = Math.PI / 2.0 / cell.CellsPerAxis * 0.78;
        var surfaceRadiusRatio = 1.0 + ((lake.SurfaceElevationMeters + 2.0) / planetRadiusMeters);
        return new PlanetWaterLakeCell(direction.X, direction.Y, direction.Z, angularRadius)
        {
            LakeId = lake.Id,
            SurfaceRadiusRatio = surfaceRadiusRatio,
            BoundaryDirections = PlanetSurfaceGridGeometry.GetCornerDirections(cell),
        };
    }

    private static double EstimateAnnualPrecipitationMillimeters(PlanetRenderSnapshot planet)
    {
        var temperatureCelsius = planet.Climate.SurfaceTemperatureKelvin - PhysicalConstants.KelvinOffsetCelsius;
        var liquidAvailability = Math.Clamp(planet.Water.LiquidFraction, 0.0, 1.0);
        var thermalScale = Math.Exp(Math.Clamp((temperatureCelsius - 15.0) * 0.035, -1.0, 1.0));
        var pressureRatio = Math.Max(planet.Atmosphere.SurfacePressurePascals, 1.0) / 101_325.0;
        var pressureScale = Math.Clamp(Math.Sqrt(pressureRatio), 0.5, 1.5);
        return ReferenceAnnualPrecipitationMillimeters * liquidAvailability * thermalScale * pressureScale;
    }

    private static double EstimateAnnualRunoffMillimeters(double annualPrecipitationMillimeters)
    {
        var runoffFraction = Math.Clamp(0.25 + (annualPrecipitationMillimeters / 2_000.0 * 0.20), 0.20, 0.45);
        return annualPrecipitationMillimeters * runoffFraction;
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
        => AddJournalEntryAt(CurrentMission.MissionYearsElapsed, key, title, description, insightAwarded);

    private void AddJournalEntryAt(double year, string key, string title, string description, int insightAwarded)
    {
        if (HasJournalEntry(key))
        {
            return;
        }

        journal.Add(new PlanetJournalEntry(key, year, title, description, insightAwarded));
        insight += insightAwarded;
    }

    private bool HasJournalEntry(string key) => journal.Any(entry => entry.Key == key);

    private MissionSnapshot CurrentMission => mission ?? throw new InvalidOperationException("Start or restore the Planet Run before using it.");

    private void EnsureWaterWorld()
    {
        if (era != PlanetRunEra.WaterWorld)
        {
            throw new InvalidOperationException("The water-cycle simulation requires the Water World era.");
        }
    }

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
            waterSurvey,
            waterCycle);
    }
}
