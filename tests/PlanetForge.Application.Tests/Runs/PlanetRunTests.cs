using System.Text.Json;
using PlanetForge.Application.Missions;
using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Runs;
using PlanetForge.Application.Surface;
using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Application.Tests.Runs;

[TestClass]
public sealed class PlanetRunTests
{
    [TestMethod]
    public void StartNew_BeginsInDeadRockEraWithJournalEntry()
    {
        var run = CreateRun();

        var snapshot = run.StartNew();

        Assert.AreEqual(PlanetRunEra.DeadRock, snapshot.Era);
        Assert.AreEqual(0, snapshot.Insight);
        Assert.HasCount(1, snapshot.Journal);
        Assert.AreEqual("run-start", snapshot.Journal[0].Key);
        Assert.IsFalse(snapshot.ResearchChoiceAvailable);
    }

    [TestMethod]
    public void StartNew_DefaultPlanet_UsesShowcaseSeed()
    {
        var run = CreateRun();

        var snapshot = run.StartNew();

        Assert.AreEqual(PlanetSeedCatalog.ShowcaseSeed, snapshot.Mission.Planet.Seed);
    }

    [TestMethod]
    public void StartNew_ExplicitSeed_UsesRequestedWorldSeed()
    {
        var run = CreateRun();

        var snapshot = run.StartNew(31415926);

        Assert.AreEqual(31415926, snapshot.Mission.Planet.Seed);
    }

    [TestMethod]
    public void Restart_GeneratedPlanet_PreservesWorldSeed()
    {
        var run = CreateRun();
        run.StartNew(31415926);

        var snapshot = run.Restart();

        Assert.AreEqual(31415926, snapshot.Mission.Planet.Seed);
    }

    [TestMethod]
    public void SimulateCommit_ReturnsIntermediateClimateFrames()
    {
        var run = CreateRun();
        run.StartNew();
        run.QueueIntervention(MissionInterventionType.OrbitalTransfer);

        var result = run.SimulateCommit();

        Assert.IsGreaterThan(1, result.Frames.Count);
        Assert.IsTrue(result.Frames.Zip(result.Frames.Skip(1)).All(pair => pair.First.Year < pair.Second.Year));
        Assert.AreEqual(result.Run.Mission.Planet.Climate.SurfaceTemperatureKelvin, result.Frames[^1].Planet.Climate.SurfaceTemperatureKelvin, 0.0001);
    }

    [TestMethod]
    public void WinningFrozenWorld_PromotesRunToWaterWorldAndAwardsInsight()
    {
        var run = CreateRun();
        run.StartNew();

        var completed = CompleteFrozenWorld(run);

        Assert.AreEqual(MissionStatus.Won, completed.Mission.Status);
        Assert.AreEqual(PlanetRunEra.WaterWorld, completed.Era);
        Assert.IsGreaterThanOrEqualTo(10, completed.Insight);
        Assert.IsTrue(completed.ResearchChoiceAvailable);
        Assert.IsTrue(completed.Journal.Any(entry => entry.Key == "stable-surface-water"));
        Assert.HasCount(3, completed.ResearchChoices);
    }

    [TestMethod]
    public void SimulateWaterWorld_ProgressivelyActivatesTerrainDerivedRunoff()
    {
        var run = CreateRun();
        run.StartNew();
        CompleteFrozenWorld(run);

        var result = run.SimulateWaterWorld(CancellationToken.None);

        Assert.HasCount(5, result.Frames);
        Assert.IsTrue(result.Frames.Zip(result.Frames.Skip(1)).All(pair => pair.First.Year < pair.Second.Year));
        Assert.IsTrue(result.Frames.Zip(result.Frames.Skip(1)).All(pair => pair.First.State.ActiveRiverSegmentCount <= pair.Second.State.ActiveRiverSegmentCount));
        Assert.IsNotNull(result.Run.WaterSurvey);
        Assert.IsNotNull(result.Run.WaterCycle);
        Assert.IsGreaterThan(0, result.Run.WaterCycle.ActiveRiverSegmentCount);
        Assert.IsGreaterThan(0.0, result.Run.WaterCycle.AnnualPrecipitationMillimeters);
        Assert.IsGreaterThan(0.0, result.Run.WaterCycle.AnnualRunoffMillimeters);
        Assert.IsGreaterThan(0.0, result.Run.WaterCycle.AnnualMeltwaterRunoffMillimeters);
        Assert.IsTrue(result.Run.WaterCycle.AnnualRunoffMillimeters > result.Run.WaterCycle.AnnualPrecipitationRunoffMillimeters);
        Assert.HasCount(result.Run.WaterCycle.ActiveRiverSegmentCount, result.Run.WaterCycle.ActiveRiverSegments);
        Assert.IsTrue(result.Run.WaterCycle.ActiveRiverSegments.All(segment => segment.RelativeDischarge is >= 0.08 and <= 1.0));
        Assert.IsTrue(result.Run.WaterCycle.ActiveRiverSegments.All(segment => segment.MeanDischargeCubicMetersPerSecond > 0.0));
        Assert.IsTrue(result.Run.Journal.Any(entry => entry.Key == "first-precipitation"));
        Assert.IsTrue(result.Run.Journal.Any(entry => entry.Key == "active-runoff-network"));
        Assert.IsTrue(result.Run.Journal.Any(entry => entry.Key == "river-incision-observed"));
        Assert.IsTrue(result.Run.Journal.Any(entry => entry.Key == "cryosphere-meltwater-routing"));
    }

    [TestMethod]
    public void SimulateWaterWorld_RepeatedRunContinuesMaturingRiverNetwork()
    {
        var run = CreateRun();
        run.StartNew();
        CompleteFrozenWorld(run);

        var first = run.SimulateWaterWorld(CancellationToken.None).Run.WaterCycle;
        var second = run.SimulateWaterWorld(CancellationToken.None).Run.WaterCycle;

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.IsGreaterThan(first.SimulatedYears, second.SimulatedYears);
        Assert.IsGreaterThan(first.RiverActivationFraction, second.RiverActivationFraction);
        Assert.IsGreaterThanOrEqualTo(first.ActiveRiverSegmentCount, second.ActiveRiverSegmentCount);
        Assert.IsGreaterThan(first.ActiveRiverSegments.Max(segment => segment.RelativeDischarge), second.ActiveRiverSegments.Max(segment => segment.RelativeDischarge));
    }

    [TestMethod]
    public void SurveyWaterWorld_ContinuesRunWithTerrainDerivedHydrology()
    {
        var run = CreateRun();
        run.StartNew();
        CompleteFrozenWorld(run);

        var surveyed = run.SurveyWaterWorld(CancellationToken.None);

        Assert.IsNotNull(surveyed.WaterSurvey);
        Assert.IsGreaterThan(0, surveyed.WaterSurvey.WatershedCount);
        Assert.IsTrue(surveyed.Journal.Any(entry => entry.Key == "water-pathways-mapped"));
        Assert.IsGreaterThanOrEqualTo(6, surveyed.Insight);
    }

    [TestMethod]
    public void SelectResearch_UnlocksCapabilityAndSpendsInsight()
    {
        var run = CreateRun();
        run.StartNew();
        var completed = CompleteFrozenWorld(run);
        var choice = completed.ResearchChoices.Single(definition => definition.Type == PlanetResearchUnlock.HydrologicalSurvey);

        var researched = run.SelectResearch(choice.Type);

        Assert.IsTrue(researched.ResearchUnlocks.Contains(PlanetResearchUnlock.HydrologicalSurvey));
        Assert.AreEqual(completed.Insight - choice.InsightCost, researched.Insight);
        Assert.IsFalse(researched.ResearchChoiceAvailable);
        Assert.IsTrue(researched.Journal.Any(entry => entry.Key == "research-HydrologicalSurvey"));
    }

    [TestMethod]
    public void ExportAndRestore_PreservesActiveWaterWorldThroughJsonRoundTrip()
    {
        var source = CreateRun();
        source.StartNew();
        var completed = CompleteFrozenWorld(source);
        var simulated = source.SimulateWaterWorld(CancellationToken.None).Run;
        source.SelectResearch(PlanetResearchUnlock.SurfaceRadiometry);
        var exportedSave = source.ExportSave();
        Assert.AreEqual(PlanetRun.SaveSchemaVersion, exportedSave.SchemaVersion);
        Assert.IsNotNull(exportedSave.TerrainEvolution);
        Assert.IsTrue(exportedSave.TerrainEvolution.Erosions.Count > 0);
        var json = JsonSerializer.Serialize(exportedSave);
        var restoredSave = JsonSerializer.Deserialize<PlanetRunSave>(json);
        Assert.IsNotNull(restoredSave);

        var restoredRun = CreateRun();
        var restored = restoredRun.Restore(restoredSave);

        Assert.AreEqual(PlanetRunEra.WaterWorld, restored.Era);
        Assert.AreEqual(completed.Mission.MissionYearsElapsed, restored.Mission.MissionYearsElapsed);
        Assert.AreEqual(completed.Mission.Planet.Seed, restored.Mission.Planet.Seed);
        Assert.AreEqual(completed.Mission.Planet.Climate.SurfaceTemperatureKelvin, restored.Mission.Planet.Climate.SurfaceTemperatureKelvin, 0.0001);
        Assert.IsTrue(restored.ResearchUnlocks.Contains(PlanetResearchUnlock.SurfaceRadiometry));
        Assert.IsTrue(restored.Journal.Any(entry => entry.Key == "stable-surface-water"));
        Assert.IsNotNull(restored.WaterSurvey);
        Assert.IsNotNull(restored.WaterCycle);
        Assert.AreEqual(simulated.WaterCycle!.ActiveRiverSegmentCount, restored.WaterCycle.ActiveRiverSegmentCount);
        Assert.HasCount(simulated.WaterCycle.ActiveRiverSegments.Count, restored.WaterCycle.ActiveRiverSegments);
        Assert.IsTrue(restored.Mission.Planet.SurfaceTiles.All(tile => tile.IncludesGeometry));
        var restoredTerrain = restoredRun.ExportSave().TerrainEvolution;
        Assert.IsNotNull(restoredTerrain);
        Assert.AreEqual(exportedSave.TerrainEvolution.Erosions.Count, restoredTerrain.Erosions.Count);
        Assert.AreEqual(exportedSave.TerrainEvolution.Depositions.Count, restoredTerrain.Depositions.Count);
        Assert.AreEqual(exportedSave.TerrainEvolution.Revision, restoredTerrain.Revision);
    }

    [TestMethod]
    public void Restore_Schema3WithoutTerrainEvolution_MigratesToEmptyTerrainState()
    {
        var source = CreateRun();
        source.StartNew();
        CompleteFrozenWorld(source);
        var current = source.ExportSave();
        var legacy = current with { SchemaVersion = 3, TerrainEvolution = null };

        var restoredRun = CreateRun();
        var restored = restoredRun.Restore(legacy);
        var migrated = restoredRun.ExportSave();

        Assert.AreEqual(PlanetRunEra.WaterWorld, restored.Era);
        Assert.AreEqual(PlanetRun.SaveSchemaVersion, migrated.SchemaVersion);
        Assert.IsNotNull(migrated.TerrainEvolution);
        Assert.HasCount(0, migrated.TerrainEvolution.Erosions);
        Assert.HasCount(0, migrated.TerrainEvolution.Depositions);
    }

    private static PlanetRunSnapshot CompleteFrozenWorld(PlanetRun run)
    {
        run.QueueIntervention(MissionInterventionType.ReleaseCapturedCarbon);
        run.QueueIntervention(MissionInterventionType.ReleaseCapturedCarbon);
        run.QueueIntervention(MissionInterventionType.DarkenSurface);
        run.QueueIntervention(MissionInterventionType.DarkenSurface);
        return run.SimulateCommit().Run;
    }

    private static PlanetRun CreateRun()
    {
        var elevationSource = new TestElevationSource();
        var experience = CreateExperience(elevationSource);
        var mission = new FrozenWorldMission(experience);
        var hydrologyBuilder = new PlanetHydrologyModelBuilder(elevationSource);
        var runoffModel = new PlanetRunoffModel();
        var cryosphereRunoffModel = new PlanetCryosphereRunoffModel();
        var hydrologyExtractor = new PlanetHydrologyFeatureExtractor();
        var geomorphologyModel = new PlanetRiverGeomorphologyModel();
        var sedimentModel = new PlanetRiverSedimentModel();
        var deformationStore = new TestTerrainDeformationStore();
        return new PlanetRun(mission, hydrologyBuilder, runoffModel, cryosphereRunoffModel, hydrologyExtractor, geomorphologyModel, sedimentModel, deformationStore);
    }

    private static PlanetExperience CreateExperience(IPlanetElevationSource elevationSource)
    {
        var sampler = new PlanetSurfaceTileSampler(elevationSource);
        var meshBuilder = new PlanetSurfaceMeshBuilder(sampler);
        var meshCache = new PlanetSurfaceMeshCache(meshBuilder);
        var localSampler = new PlanetLocalSurfacePatchSampler(elevationSource);
        var localMeshBuilder = new PlanetLocalSurfaceMeshBuilder();
        return new PlanetExperience(meshCache, localSampler, localMeshBuilder);
    }

    private sealed class TestElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => (direction.Y * 4_000.0) + (direction.X * 1_000.0);
    }

    private sealed class TestTerrainDeformationStore : IPlanetTerrainDeformationStore
    {
        private readonly List<PlanetTerrainDeformation> erosions = [];
        private readonly List<PlanetTerrainDeposition> depositions = [];
        private int revision;

        public double SampleElevationDeltaMeters(PlanetVector direction, int seed) => 0.0;

        public int GetRevision(int seed) => revision;

        public void Apply(int seed, IReadOnlyList<PlanetTerrainDeformation> deformations)
        {
            erosions.AddRange(deformations);
            revision++;
        }

        public void ApplyDeposition(int seed, IReadOnlyList<PlanetTerrainDeposition> values)
        {
            depositions.AddRange(values);
            revision++;
        }

        public PlanetTerrainEvolutionState Export(int seed) => new(revision, erosions.ToArray(), depositions.ToArray());

        public void Restore(int seed, PlanetTerrainEvolutionState state)
        {
            erosions.Clear();
            erosions.AddRange(state.Erosions);
            depositions.Clear();
            depositions.AddRange(state.Depositions);
            revision = state.Revision;
        }

        public void Clear(int seed) => ClearAll();

        public void ClearAll()
        {
            erosions.Clear();
            depositions.Clear();
            revision = 0;
        }
    }
}
