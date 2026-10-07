using PlanetForge.Application.Missions;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Physics;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Runs;

public sealed class PlanetRun(
    FrozenWorldMission frozenWorldMission,
    PlanetHydrologyModelBuilder hydrologyModelBuilder,
    PlanetRunoffModel runoffModel,
    PlanetCryosphereRunoffModel cryosphereRunoffModel,
    PlanetHydrologyFeatureExtractor hydrologyFeatureExtractor,
    PlanetRiverGeomorphologyModel riverGeomorphologyModel,
    PlanetRiverSedimentModel riverSedimentModel,
    IPlanetTerrainDeformationStore terrainDeformationStore)
{
    public const int SaveSchemaVersion = 3;

    private const int WaterSurveyGridLevel = 5;
    private const double MinimumRiverMeanDischargeCubicMetersPerSecond = 2_500.0;
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
    private PlanetHydrologySnapshot? waterHydrology;

    public IReadOnlyList<PlanetRunEraDefinition> Eras => EraDefinitions;

    public IReadOnlyList<MissionInterventionDefinition> Interventions => frozenWorldMission.Interventions;

    public PlanetRunSnapshot StartNew()
    {
        terrainDeformationStore.ClearAll();
        mission = frozenWorldMission.Start();
        era = PlanetRunEra.DeadRock;
        insight = 0;
        researchChoiceAvailable = false;
        waterSurvey = null;
        waterCycle = null;
        waterHydrology = null;
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
        var features = BuildWaterFeatures(cancellationToken);
        EnsureWaterSurvey(features);
        return CreateSnapshot();
    }

    public PlanetWaterWorldSimulationResult SimulateWaterWorld(CancellationToken cancellationToken)
    {
        EnsureWaterWorld();

        var planet = CurrentMission.Planet;
        var precipitation = EstimateAnnualPrecipitationMillimeters(planet);
        var precipitationRunoff = EstimateAnnualRunoffMillimeters(precipitation);
        var hydrologyResult = BuildWaterHydrology(precipitationRunoff, cancellationToken);
        var features = hydrologyResult.Features;
        var meltwaterRunoff = hydrologyResult.CryosphereRunoff.MeanAnnualMeltwaterRunoffMillimeters;
        EnsureWaterSurvey(features);
        var startYear = waterCycle?.SimulatedYears ?? 0;
        var frames = WaterCycleFrameYears
            .Select(frameYears => BuildWaterCycleFrame(startYear + frameYears, precipitation, precipitationRunoff, meltwaterRunoff, features))
            .ToArray();

        waterCycle = frames[^1].State;
        ApplyRiverGeomorphology(features, cancellationToken);

        if (!HasJournalEntry("first-precipitation"))
        {
            AddJournalEntryAt(
                CurrentMission.MissionYearsElapsed + frames[0].Year,
                "first-precipitation",
                "The first persistent precipitation cycle begins",
                "Liquid surface water now participates in a reduced global water-cycle model: evaporation supplies atmospheric moisture, precipitation returns it to the surface, and topography routes the runoff.",
                4);
        }

        if (waterCycle.AnnualMeltwaterRunoffMillimeters > 1.0 && !HasJournalEntry("cryosphere-meltwater-routing"))
        {
            AddJournalEntryAt(
                CurrentMission.MissionYearsElapsed + waterCycle.SimulatedYears,
                "cryosphere-meltwater-routing",
                "Cryosphere meltwater joins the drainage network",
                $"Retreating snow and land ice now contribute about {waterCycle.AnnualMeltwaterRunoffMillimeters:0} mm/year of mean land runoff, strengthening downstream discharge and basin filling.",
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

        terrainDeformationStore.ClearAll();
        mission = frozenWorldMission.RestoreState(save.Mission);
        era = save.Era;
        insight = save.Insight;
        researchChoiceAvailable = save.ResearchChoiceAvailable;
        waterSurvey = save.WaterSurvey;
        waterCycle = save.WaterCycle;
        waterHydrology = null;
        journal.Clear();
        journal.AddRange(save.Journal);
        researchUnlocks.Clear();
        researchUnlocks.AddRange(save.ResearchUnlocks);
        return CreateSnapshot();
    }

    private void ApplyRiverGeomorphology(PlanetHydrologyFeatures features, CancellationToken cancellationToken)
    {
        if (waterCycle is null || waterCycle.ActiveRiverSegmentCount == 0)
        {
            return;
        }

        var activeSegments = features.RiverSegments
            .OrderByDescending(segment => segment.MeanDischargeCubicMetersPerSecond)
            .ThenByDescending(segment => segment.StrahlerOrder)
            .Take(waterCycle.ActiveRiverSegmentCount)
            .ToArray();
        var planet = CurrentMission.Planet;
        var deformations = riverGeomorphologyModel.Build(
            activeSegments,
            waterCycle.SimulatedYears,
            planet.PhysicalParameters.RadiusMeters,
            cancellationToken);
        var depositions = riverSedimentModel.Build(
            activeSegments,
            waterCycle.SimulatedYears,
            planet.PhysicalParameters.RadiusMeters,
            planet.SeaLevelMeters,
            cancellationToken);
        if (deformations.Count == 0 && depositions.Count == 0)
        {
            return;
        }

        if (deformations.Count > 0)
        {
            terrainDeformationStore.Apply(planet.Seed, deformations);
        }

        if (depositions.Count > 0)
        {
            terrainDeformationStore.ApplyDeposition(planet.Seed, depositions);
        }

        waterHydrology = null;
        mission = frozenWorldMission.RefreshTerrain();

        if (deformations.Count > 0 && !HasJournalEntry("river-incision-observed"))
        {
            AddJournalEntryAt(
                CurrentMission.MissionYearsElapsed + waterCycle.SimulatedYears,
                "river-incision-observed",
                "Persistent rivers begin reshaping the terrain",
                "Discharge and slope now feed a geomorphic terrain layer: major channels incise into the surface while broader valleys emerge around persistent high-flow routes.",
                4);
        }

        if (depositions.Count > 0 && !HasJournalEntry("sediment-deposition-observed"))
        {
            AddJournalEntryAt(
                CurrentMission.MissionYearsElapsed + waterCycle.SimulatedYears,
                "sediment-deposition-observed",
                "Sediment begins building floodplains and deltas",
                "Low-gradient high-discharge reaches now deposit transported sediment. River-mouth deposition modifies the canonical elevation surface, allowing coastlines to evolve from terrain rather than a visual shoreline effect.",
                4);
        }
    }

    private PlanetHydrologyFeatures BuildWaterFeatures(CancellationToken cancellationToken)
    {
        var planet = CurrentMission.Planet;
        var precipitation = EstimateAnnualPrecipitationMillimeters(planet);
        var annualRunoffMillimeters = EstimateAnnualRunoffMillimeters(precipitation);
        return BuildWaterHydrology(annualRunoffMillimeters, cancellationToken).Features;
    }

    private WaterHydrologyResult BuildWaterHydrology(double annualPrecipitationRunoffMillimeters, CancellationToken cancellationToken)
    {
        var planet = CurrentMission.Planet;
        var hydrology = EnsureWaterHydrology(cancellationToken);
        var cryosphereRunoff = cryosphereRunoffModel.Build(
            hydrology,
            planet.SeaLevelMeters,
            planet.Climate.SurfaceTemperatureKelvin,
            planet.ClimateFeedback.LandIceFraction,
            planet.ClimateFeedback.SnowCoverFraction,
            cancellationToken);
        var runoff = runoffModel.Build(
            hydrology,
            planet.PhysicalParameters.RadiusMeters,
            planet.SeaLevelMeters,
            annualPrecipitationRunoffMillimeters,
            cryosphereRunoff.LocalAnnualMeltwaterRunoffMillimeters,
            cancellationToken);
        var features = hydrologyFeatureExtractor.Extract(hydrology, runoff, MinimumRiverMeanDischargeCubicMetersPerSecond, cancellationToken);
        return new WaterHydrologyResult(features, runoff, cryosphereRunoff);
    }

    private PlanetHydrologySnapshot EnsureWaterHydrology(CancellationToken cancellationToken)
    {
        if (waterHydrology is not null)
        {
            return waterHydrology;
        }

        var planet = CurrentMission.Planet;
        waterHydrology = hydrologyModelBuilder.Build(
            WaterSurveyGridLevel,
            planet.Seed,
            planet.PhysicalParameters.RadiusMeters,
            planet.SeaLevelMeters,
            cancellationToken);
        return waterHydrology;
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
        double annualPrecipitationRunoffMillimeters,
        double annualMeltwaterRunoffMillimeters,
        PlanetHydrologyFeatures features)
    {
        var annualRunoffMillimeters = annualPrecipitationRunoffMillimeters + annualMeltwaterRunoffMillimeters;
        var wettingResponse = 1.0 - Math.Exp(-year / 18.0);
        var runoffPotential = Math.Clamp(annualRunoffMillimeters / 350.0, 0.0, 1.0);
        var riverActivation = Math.Clamp(wettingResponse * runoffPotential, 0.0, 1.0);
        var lakeFillResponse = 1.0 - Math.Exp(-year / 30.0);
        var lakeWaterSupplyMillimeters = annualPrecipitationMillimeters + annualMeltwaterRunoffMillimeters;
        var lakeFill = Math.Clamp(lakeFillResponse * Math.Clamp(lakeWaterSupplyMillimeters / 700.0, 0.0, 1.0), 0.0, 1.0);
        var activeRiverCount = features.RiverSegments.Count == 0 ? 0 : Math.Clamp((int)Math.Round(features.RiverSegments.Count * riverActivation), 1, features.RiverSegments.Count);
        var activeLakeCount = features.Lakes.Count == 0 ? 0 : Math.Clamp((int)Math.Round(features.Lakes.Count * lakeFill), 1, features.Lakes.Count);
        var maximumDischarge = features.RiverSegments.Count == 0 ? 1.0 : features.RiverSegments.Max(segment => segment.MeanDischargeCubicMetersPerSecond);
        var activeSegments = features.RiverSegments
            .OrderByDescending(segment => segment.MeanDischargeCubicMetersPerSecond)
            .ThenByDescending(segment => segment.StrahlerOrder)
            .Take(activeRiverCount)
            .Select(segment => new PlanetWaterPathSegment(
                segment.FromDirection.X,
                segment.FromDirection.Y,
                segment.FromDirection.Z,
                segment.ToDirection.X,
                segment.ToDirection.Y,
                segment.ToDirection.Z,
                Math.Clamp(Math.Sqrt(segment.MeanDischargeCubicMetersPerSecond / maximumDischarge), 0.08, 1.0),
                segment.StrahlerOrder,
                segment.MeanDischargeCubicMetersPerSecond))
            .ToArray();
        var state = new PlanetWaterCycleState(
            year,
            annualPrecipitationMillimeters,
            annualRunoffMillimeters,
            lakeFill,
            riverActivation,
            activeLakeCount,
            activeRiverCount,
            activeSegments)
        {
            AnnualPrecipitationRunoffMillimeters = annualPrecipitationRunoffMillimeters,
            AnnualMeltwaterRunoffMillimeters = annualMeltwaterRunoffMillimeters,
        };
        return new PlanetWaterCycleFrame(year, state with { ActiveLakeCells = BuildActiveLakeCells(features, state) });
    }

    private static IReadOnlyList<PlanetWaterLakeCell> BuildActiveLakeCells(PlanetHydrologyFeatures features, PlanetWaterCycleState state)
    {
        if (state.ActiveLakeCount == 0 || features.Lakes.Count == 0)
        {
            return [];
        }

        return features.Lakes
            .OrderBy(lake => lake.Id)
            .Take(state.ActiveLakeCount)
            .SelectMany(lake => lake.Cells)
            .Select(cell =>
            {
                var direction = PlanetSurfaceGridGeometry.GetCenterDirection(cell);
                var angularRadius = Math.PI / 2.0 / cell.CellsPerAxis * 0.78;
                return new PlanetWaterLakeCell(direction.X, direction.Y, direction.Z, angularRadius);
            })
            .ToArray();
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

    private sealed record WaterHydrologyResult(
        PlanetHydrologyFeatures Features,
        PlanetRunoffSnapshot Runoff,
        PlanetCryosphereRunoffSnapshot CryosphereRunoff);

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
