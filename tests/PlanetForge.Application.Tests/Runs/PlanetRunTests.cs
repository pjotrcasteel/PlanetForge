using System.Text.Json;
using PlanetForge.Application.Missions;
using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Runs;
using PlanetForge.Application.Surface;
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
    public void ExportAndRestore_PreservesPlanetRunStateThroughJsonRoundTrip()
    {
        var source = CreateRun();
        source.StartNew();
        var completed = CompleteFrozenWorld(source);
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
        Assert.IsTrue(restored.Mission.Planet.SurfaceTiles.All(tile => tile.IncludesGeometry));
    }

    private static PlanetRunSnapshot CompleteFrozenWorld(PlanetRun run)
    {
        run.QueueIntervention(MissionInterventionType.ReleaseCapturedCarbon);
        run.QueueIntervention(MissionInterventionType.ReleaseCapturedCarbon);
        run.QueueIntervention(MissionInterventionType.DarkenSurface);
        run.QueueIntervention(MissionInterventionType.DarkenSurface);
        run.SelectPrediction(MissionPrediction.Warmer);
        run.SimulateTurn();
        run.SelectPrediction(MissionPrediction.Warmer);
        return run.SimulateTurn();
    }

    private static PlanetRun CreateRun()
    {
        var experience = CreateExperience();
        return new PlanetRun(new FrozenWorldMission(experience));
    }

    private static PlanetExperience CreateExperience()
    {
        var elevationSource = new FlatElevationSource();
        var sampler = new PlanetSurfaceTileSampler(elevationSource);
        var meshBuilder = new PlanetSurfaceMeshBuilder(sampler);
        var meshCache = new PlanetSurfaceMeshCache(meshBuilder);
        var localSampler = new PlanetLocalSurfacePatchSampler(elevationSource);
        var localMeshBuilder = new PlanetLocalSurfaceMeshBuilder();
        return new PlanetExperience(meshCache, localSampler, localMeshBuilder);
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }
}