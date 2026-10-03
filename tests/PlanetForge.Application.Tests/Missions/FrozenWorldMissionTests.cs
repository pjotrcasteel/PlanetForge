using PlanetForge.Application.Missions;
using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Missions;

[TestClass]
public sealed class FrozenWorldMissionTests
{
    [TestMethod]
    public void Start_CreatesFrozenWorldWithFullBudget()
    {
        var mission = new FrozenWorldMission(CreateExperience());

        var snapshot = mission.Start();

        Assert.AreEqual(FrozenWorldMission.StartingBudget, snapshot.BudgetRemaining);
        Assert.AreEqual(0, snapshot.MissionYearsElapsed);
        Assert.AreEqual(MissionStatus.Active, snapshot.Status);
        Assert.IsLessThan(273.15, snapshot.Planet.Climate.SurfaceTemperatureKelvin);
        Assert.IsGreaterThan(0.25, snapshot.Planet.ClimateFeedback.CryosphereFraction);
    }

    [TestMethod]
    public void QueueIntervention_ReservesBudgetWithoutAdvancingTime()
    {
        var mission = new FrozenWorldMission(CreateExperience());
        var started = mission.Start();

        var planned = mission.QueueIntervention(MissionInterventionType.ReleaseCapturedCarbon);

        Assert.AreEqual(started.MissionYearsElapsed, planned.MissionYearsElapsed);
        Assert.AreEqual(started.BudgetRemaining - 25, planned.BudgetRemaining);
        Assert.AreEqual(started.Planet.AtmosphereParameters.CarbonDioxidePartsPerMillion, planned.Planet.AtmosphereParameters.CarbonDioxidePartsPerMillion);
        Assert.AreEqual(1, planned.PlannedInterventions.Count);
    }

    [TestMethod]
    public void SimulateTurn_WithoutPrediction_Throws()
    {
        var mission = new FrozenWorldMission(CreateExperience());
        mission.Start();
        mission.QueueIntervention(MissionInterventionType.DarkenSurface);

        Assert.ThrowsExactly<InvalidOperationException>(() => mission.SimulateTurn());
    }

    [TestMethod]
    public void SimulateTurn_WrongPrediction_ExplainsObservedDirection()
    {
        var mission = new FrozenWorldMission(CreateExperience());
        mission.Start();
        mission.SelectPrediction(MissionPrediction.Warmer);

        var result = mission.SimulateTurn();

        Assert.IsNotNull(result.LastTurn);
        Assert.IsFalse(result.LastTurn.PredictionCorrect);
        Assert.IsLessThan(0.0, result.LastTurn.TemperatureDeltaKelvin);
        StringAssert.Contains(result.LastTurn.Explanation, "Cryosphere");
    }

    [TestMethod]
    public void CombinedInterventions_CanWinMissionWithoutUsingOrbitalTransfer()
    {
        var mission = new FrozenWorldMission(CreateExperience());
        mission.Start();
        mission.QueueIntervention(MissionInterventionType.ReleaseCapturedCarbon);
        mission.QueueIntervention(MissionInterventionType.ReleaseCapturedCarbon);
        mission.QueueIntervention(MissionInterventionType.DarkenSurface);
        var planned = mission.QueueIntervention(MissionInterventionType.DarkenSurface);
        Assert.AreEqual(10, planned.BudgetRemaining);

        mission.SelectPrediction(MissionPrediction.Warmer);
        var firstTurn = mission.SimulateTurn();
        mission.SelectPrediction(MissionPrediction.Warmer);
        var secondTurn = mission.SimulateTurn();

        Assert.AreEqual(MissionStatus.Won, secondTurn.Status);
        Assert.AreEqual(FrozenWorldMission.RequiredStableYears, secondTurn.StableYears);
        Assert.IsGreaterThan(0.5, secondTurn.Planet.Water.LiquidFraction);
        Assert.IsLessThan(0.35, secondTurn.Planet.ClimateFeedback.CryosphereFraction);
        Assert.IsNotNull(firstTurn.LastTurn);
        Assert.IsNotNull(secondTurn.LastTurn);
        Assert.IsTrue(firstTurn.LastTurn.PredictionCorrect);
        Assert.IsTrue(secondTurn.LastTurn.PredictionCorrect);
    }

    [TestMethod]
    public void ClearPlan_RefundsReservedBudget()
    {
        var mission = new FrozenWorldMission(CreateExperience());
        mission.Start();
        mission.QueueIntervention(MissionInterventionType.ReleaseCapturedCarbon);
        mission.QueueIntervention(MissionInterventionType.DarkenSurface);

        var cleared = mission.ClearPlan();

        Assert.AreEqual(FrozenWorldMission.StartingBudget, cleared.BudgetRemaining);
        Assert.AreEqual(0, cleared.PlannedInterventions.Count);
        Assert.IsNull(cleared.Prediction);
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