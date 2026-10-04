using System.Text.Json;
using PlanetForge.Application.Missions;
using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Runs;
using PlanetForge.Application.Surface;
using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;

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
        Assert.HasCount(result.Run.WaterCycle.ActiveRiverSegmentCount, result.Run.WaterCycle.ActiveRiverSegments);
        Assert.IsTrue(result.Run.WaterCycle.ActiveRiverSegments.All(segment => segment.RelativeDischarge is >= 0.08 and <= 1.0));
        Assert.IsTrue(result.Run.Journal.Any(entry => entry.Key == "first-precipitation"));
        Assert.IsTrue(result.Run.Journal.Any(entry => entry.Key == "active-runoff-network"));
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
        Assert.IsGreaterThan(first.SimulatedYears, 0);
        Assert.IsGreaterThan(first.ActiveRiverSegmentCount, 0);
        Assert.IsGreaterThan(first.ActiveRiverSegmentCount, 0);
        Assert.IsGreaterThan(first.SimulatedYears, 0);
        Assert.IsGreaterThan(first.ActiveRiverSegmentCount, 0);
        Assert.IsGreaterThan(first.RiverActivationFraction, 0.0);
        Assert.IsGreaterThan(second.SimulatedYears, first.SimulatedYears);
        Assert.IsGreaterThan(second.RiverActivationFraction, first.RiverActivationFraction);
        Assert.IsGreaterThanOrEqualTo(second.ActiveRiverSegmentCount, first.ActiveRiverSegmentCount);
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
        var json = JsonSerializer.Serialize(source.ExportSave());
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
        var hydrologyExtractor = new PlanetHydrologyFeatureExtractor();
        return new PlanetRun(mission, hydrologyBuilder, hydrologyExtractor);
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
}
