using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginSurfaceRockLateralHeatTests
{
    private const double RadiusMeters = 1_000.0;
    private const double SurfaceMassKilograms = 1.0e9;
    private static readonly PlanetOriginSurfaceRockPhaseMaterial Material = new(850.0, 400_000.0, 1_300.0, 1_900.0);

    [TestMethod]
    public void ProposeLateralHeat_CubeFaceSeam_TransfersHeatBetweenPhysicalNeighborsWithoutChangingTotalEnergy()
    {
        var original = CreateCanonical();
        var hot = new PlanetSurfaceGridCellId(CubeFace.PositiveZ, 1, 1, 0);
        var cold = PlanetSurfaceGridTopology.GetNeighbor(hot, PlanetGridDirection.East).Cell;
        var hotIndex = original.Field.Layout.GetIndex(hot);
        var coldIndex = original.Field.Layout.GetIndex(cold);
        Assert.AreEqual(CubeFace.PositiveX, cold.Face);
        Assert.IsTrue(original.Field.Cells[hotIndex].Resolve(Material).TemperatureKelvin >
            original.Field.Cells[coldIndex].Resolve(Material).TemperatureKelvin);

        var trial = original.Field.ProposeLateralHeat(PlanetOriginDuration.FromSeconds(600.0), 10.0, 10.0);

        Assert.IsTrue(trial.TransportedJoules > 0.0);
        Assert.IsTrue(trial.Candidate.Cells[hotIndex].StoredEnthalpyJoules < original.Field.Cells[hotIndex].StoredEnthalpyJoules);
        Assert.IsTrue(trial.Candidate.Cells[coldIndex].StoredEnthalpyJoules > original.Field.Cells[coldIndex].StoredEnthalpyJoules);
        Assert.AreEqual(original.Field.TotalStoredEnthalpyJoules, trial.Candidate.TotalStoredEnthalpyJoules,
            original.Field.TotalStoredEnthalpyJoules * 1e-12);
        Assert.AreEqual(0.0, trial.ConservationResidualJoules, original.Field.TotalStoredEnthalpyJoules * 1e-12);
        Assert.AreEqual(0L, original.Epoch.Revision);
    }

    [TestMethod]
    public void ProposeLateralHeat_SameInputs_ReplaysExactCellEnergyAndLeavesSourceUnchanged()
    {
        var original = CreateCanonical();
        var duration = PlanetOriginDuration.FromSeconds(600.0);
        var first = original.Field.ProposeLateralHeat(duration, 10.0, 10.0);
        var replay = original.Field.ProposeLateralHeat(duration, 10.0, 10.0);

        Assert.AreEqual(first.TransportedJoules, replay.TransportedJoules, 0.0);
        for (var i = 0; i < original.Field.Cells.Count; i++)
        {
            Assert.AreEqual(first.Candidate.Cells[i].StoredEnthalpyJoules, replay.Candidate.Cells[i].StoredEnthalpyJoules, 0.0);
            Assert.AreEqual(original.Field.Cells[i].StoredEnthalpyJoules,
                CreateCanonical().Field.Cells[i].StoredEnthalpyJoules, 0.0);
        }

        var noTransport = original.Field.ProposeLateralHeat(duration, 0.0, 10.0);
        Assert.AreSame(original.Field, noTransport.Candidate);
        Assert.AreEqual(0.0, noTransport.TransportedJoules, 0.0);
    }

    [TestMethod]
    public void ProposeLateralHeat_UnstableStepOrCancelledProposal_DoesNotMutatePublishedWorld()
    {
        var original = CreateCanonical();
        var first = original.Field.Cells[0].StoredEnthalpyJoules;
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            original.Field.ProposeLateralHeat(PlanetOriginDuration.FromSeconds(86_400.0), 1.0e12, 1.0e6));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            original.Field.ProposeLateralHeat(PlanetOriginDuration.FromSeconds(1.0), -1.0, 10.0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            original.Field.ProposeLateralHeat(PlanetOriginDuration.FromSeconds(600.0), 10.0, 10.0, cancellation.Token));
        Assert.AreEqual(first, original.Field.Cells[0].StoredEnthalpyJoules, 0.0);
        Assert.AreEqual(0L, original.Epoch.Revision);
    }

    [TestMethod]
    public void Advance_WithOnlyLateralHeat_PublishesOneConservativeCanonicalEpoch()
    {
        var initial = CreateCanonical();
        var parameters = new PlanetOriginRegionalAirlessParameters(
            PlanetVector.UnitX, 0.0, 0.0, 0.0, 0.0, 10.0)
        {
            LateralConductivityWattsPerMeterKelvin = 10.0,
            LateralLayerThicknessMeters = 10.0
        };
        var duration = PlanetOriginDuration.FromSeconds(600.0);
        var next = initial.Advance(duration, parameters);
        var replay = initial.Advance(duration, parameters);

        Assert.AreEqual(1L, next.Epoch.Revision);
        Assert.AreEqual(600_000_000L, next.Epoch.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(initial.Field.TotalStoredEnthalpyJoules, next.Field.TotalStoredEnthalpyJoules,
            initial.Field.TotalStoredEnthalpyJoules * 1e-12);
        Assert.AreEqual(next.Epoch.CoupledEnergy!.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock),
            next.Field.TotalStoredEnthalpyJoules, initial.Field.TotalStoredEnthalpyJoules * 1e-12);
        Assert.AreEqual(0.0, next.Epoch.EnergyConservationResidualJoules,
            initial.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
        Assert.AreEqual(0.0, next.Epoch.WaterConservationResidualKilograms, 0.0);
        Assert.IsTrue(next.Field.Cells.Where((cell, index) =>
            cell.StoredEnthalpyJoules != initial.Field.Cells[index].StoredEnthalpyJoules).Any());
        for (var i = 0; i < next.Field.Cells.Count; i++)
            Assert.AreEqual(next.Field.Cells[i].StoredEnthalpyJoules, replay.Field.Cells[i].StoredEnthalpyJoules, 0.0);
        Assert.AreEqual(0L, initial.Epoch.Revision);
    }

    private static PlanetOriginCanonicalRockCells CreateCanonical()
    {
        var mantle = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(
            1.0e12, 1_200.0, 400_000.0, 1_300.0, 1_900.0));
        var interior = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(mantle.CalculateStoredEnergyJoules(1_750.0)), mantle);
        var surfaceEnthalpy = new PlanetRockEnthalpyModel(Material.ToRockProperties(SurfaceMassKilograms))
            .CalculateStoredEnergyJoules(1_600.0);
        var environment = new PlanetWaterEnvironmentalState(PlanetWaterInventory.Empty,
            new PlanetEnvironmentalEnergyBudget(0.0, 0.0, surfaceEnthalpy));
        var capacities = new PlanetEnvironmentHeatCapacityParameters(
            0.0, 1_000.0, 2_000.0, 4_200.0, SurfaceMassKilograms, Material.SpecificHeatCapacityJoulesPerKilogramKelvin)
        {
            SurfaceRockPhaseMaterial = Material
        };
        var ledger = new PlanetOriginEnergyLedger(interior, environment, capacities);
        var field = PlanetOriginSurfaceRockPhaseField.FromCanonicalPhaseSurface(
            ledger, new PlanetSurfaceGridLayout(1), RadiusMeters);
        var selected = field.RedistributeInitialEnergy(
            cell => cell.Id.Face == CubeFace.PositiveZ ? cell.ActiveRockMassKilograms : 0.0,
            surfaceEnthalpy * 0.08);
        return new PlanetOriginCanonicalRockCells(new PlanetOriginEpochState(ledger), selected);
    }
}
