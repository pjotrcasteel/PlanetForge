using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginViscousMeltFlowTests
{
    private const double Radius = 100_000.0;
    private static readonly PlanetOriginSurfaceRockPhaseMaterial Material = new(850.0, 400_000.0, 1_300.0, 1_900.0);
    private static readonly PlanetOriginViscousMeltParameters Flow = new(9.81, 1_000.0);
    private static PlanetOriginRegionalAirlessParameters ClosedBoundary => new(PlanetVector.UnitX, 0.0, 0.0, 0.0, 0.0, 25.0);

    [TestMethod]
    public void Funding_ExistingThermalAccount_PaysForPotentialWithoutChangingMassTimeOrTotalEnergy()
    {
        var original = CreateState(true);
        var funded = original.FundViscousMeltMechanics(Flow);
        var potential = funded.MechanicalReference!.PotentialEnergyJoules(funded.Columns!);
        Assert.IsTrue(potential > 0.0);
        Assert.AreEqual(potential, funded.Epoch.CoupledEnergy!.SurfaceMechanics!.StoredEnergyJoules, potential * 1e-12);
        Assert.AreEqual(original.Epoch.CoupledEnergy!.StoredEnergyJoules, funded.Epoch.CoupledEnergy.StoredEnergyJoules,
            original.Epoch.CoupledEnergy.StoredEnergyJoules * 1e-14);
        Assert.AreEqual(original.Field.TotalStoredEnthalpyJoules - potential, funded.Field.TotalStoredEnthalpyJoules,
            original.Field.TotalStoredEnthalpyJoules * 1e-14);
        Assert.AreEqual(original.Field.TotalActiveRockMassKilograms, funded.Field.TotalActiveRockMassKilograms, 0.0);
        Assert.AreEqual(original.Epoch.Clock, funded.Epoch.Clock);
        Assert.IsNull(original.Epoch.CoupledEnergy.SurfaceMechanics);
        Assert.ThrowsExactly<InvalidOperationException>(() => funded.FundViscousMeltMechanics(Flow));
        Assert.ThrowsExactly<InvalidOperationException>(() => CreateState(false, variableDensity: true).FundViscousMeltMechanics(Flow));
        AssertConservation(funded);
    }

    [TestMethod]
    public void Flow_HydrostaticHead_MovesMaterialDownhillAndCreditsOnlyLostPotentialAsHeat()
    {
        var funded = CreateState(true).FundViscousMeltMechanics(Flow);
        var reference = funded.MechanicalReference!;
        var columns = funded.Columns!;
        var interval = PlanetOriginDuration.FromSeconds(3_600.0);
        var trial = columns.ProposeViscousMeltFlow(interval, reference);
        Assert.IsTrue(trial.Faces.Count > 0);
        Assert.IsTrue(trial.TransportedMassKilograms > 0.0);
        Assert.IsTrue(trial.DissipatedWorkJoules > 0.0);
        foreach (var face in trial.Faces)
        {
            var donor = columns.Columns[funded.Field.Layout.GetIndex(face.Donor)];
            var recipient = columns.Columns[funded.Field.Layout.GetIndex(face.Recipient)];
            Assert.IsTrue(Head(donor, columns) > Head(recipient, columns));
            Assert.IsTrue(face.MeanVelocityMetersPerSecond > 0.0 && face.ReynoldsNumber <= Flow.MaximumReynoldsNumber);
        }
        var next = funded.AdvanceWithViscousMeltFlow(interval, ClosedBoundary);
        var beforeBudget = funded.Epoch.CoupledEnergy!.SurfaceMechanics!;
        var afterBudget = next.Epoch.CoupledEnergy!.SurfaceMechanics!;
        Assert.AreEqual(trial.DissipatedWorkJoules, afterBudget.CumulativeThermalizedJoules, trial.DissipatedWorkJoules * 1e-12);
        Assert.AreEqual(beforeBudget.StoredEnergyJoules - trial.DissipatedWorkJoules, afterBudget.StoredEnergyJoules, beforeBudget.StoredEnergyJoules * 1e-12);
        Assert.AreEqual(funded.Field.TotalStoredEnthalpyJoules + trial.DissipatedWorkJoules, next.Field.TotalStoredEnthalpyJoules,
            funded.Field.TotalStoredEnthalpyJoules * 1e-14);
        Assert.AreEqual(funded.Field.TotalActiveRockMassKilograms, next.Field.TotalActiveRockMassKilograms, funded.Field.TotalActiveRockMassKilograms * 1e-12);
        Assert.AreEqual(3_600_000_000L, next.Epoch.Clock.MicrosecondsIntoYear);
        Assert.IsTrue(next.Columns!.Columns.Max(column => Head(column, next.Columns)) < columns.Columns.Max(column => Head(column, columns)));
        AssertConservation(next);
    }

    [TestMethod]
    public void Flow_FlatSurface_IsAtRestWithoutInventingSeededVelocityOrHeat()
    {
        var funded = CreateState(false).FundViscousMeltMechanics(Flow);
        var trial = funded.Columns!.ProposeViscousMeltFlow(PlanetOriginDuration.FromSeconds(1.0), funded.MechanicalReference!);
        Assert.AreEqual(0, trial.Faces.Count);
        Assert.AreEqual(0.0, funded.Epoch.CoupledEnergy!.SurfaceMechanics!.InitialStoredEnergyJoules);
        Assert.AreEqual(0.0, trial.TransportedMassKilograms);
        Assert.AreEqual(0.0, trial.DissipatedWorkJoules);
        var next = funded.AdvanceWithViscousMeltFlow(PlanetOriginDuration.FromSeconds(1.0), ClosedBoundary);
        CollectionAssert.AreEqual(funded.Field.Cells.ToArray(), next.Field.Cells.ToArray());
        Assert.AreEqual(0.0, next.Epoch.CoupledEnergy!.SurfaceMechanics!.CumulativeThermalizedJoules);
        AssertConservation(next);
    }

    [TestMethod]
    public void Flow_Viscosity_ResponseIsPhysicalAndInvalidInertiaOrTimestepIsRejected()
    {
        var funded = CreateState(true).FundViscousMeltMechanics(Flow);
        var reference = funded.MechanicalReference!;
        var interval = PlanetOriginDuration.FromSeconds(1.0);
        var ordinary = funded.Columns!.ProposeViscousMeltFlow(interval, reference);
        var viscous = funded.Columns.ProposeViscousMeltFlow(interval, reference with
        {
            Parameters = Flow with { DynamicViscosityPascalSeconds = Flow.DynamicViscosityPascalSeconds * 2.0 }
        });
        Assert.AreEqual(ordinary.Faces.Count, viscous.Faces.Count);
        for (var index = 0; index < ordinary.Faces.Count; index++)
            Assert.AreEqual(ordinary.Faces[index].MeanVelocityMetersPerSecond * 0.5, viscous.Faces[index].MeanVelocityMetersPerSecond, 1e-12);
        Assert.ThrowsExactly<InvalidOperationException>(() => funded.Columns.ProposeViscousMeltFlow(interval, reference with
        {
            Parameters = Flow with { DynamicViscosityPascalSeconds = 1.0 }
        }));
        var bound = funded.Columns.MaximumViscousMeltStepSeconds(reference);
        Assert.ThrowsExactly<InvalidOperationException>(() => funded.AdvanceWithViscousMeltFlow(PlanetOriginDuration.FromSeconds(bound * 2.0), ClosedBoundary));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => funded.AdvanceWithViscousMeltFlow(interval, ClosedBoundary, cancellation.Token));
        Assert.AreEqual(0L, funded.Epoch.Revision);
        Assert.AreEqual(0.0, funded.Epoch.CoupledEnergy!.SurfaceMechanics!.CumulativeThermalizedJoules);
    }

    [TestMethod]
    public void Integration_MatchedPhysicalTime_ReplaysConvergesAndCannotBypassTheMechanicalAccount()
    {
        var funded = CreateState(true).FundViscousMeltMechanics(Flow);
        PlanetOriginCanonicalRockCells Integrate(double cap) => PlanetOriginRockColumnIntegrator.Advance(funded,
            PlanetOriginDuration.FromSeconds(86_400.0), ClosedBoundary, new(cap, 10_000), CancellationToken.None);
        var coarse = Integrate(3_600.0);
        var fine = Integrate(1_800.0);
        var reference = Integrate(300.0);
        var replay = Integrate(1_800.0);
        double Error(PlanetOriginCanonicalRockCells candidate) => candidate.Columns!.Columns.Select((column, index) =>
            Math.Abs(Head(column, candidate.Columns) - Head(reference.Columns!.Columns[index], reference.Columns))).Max();
        Assert.IsTrue(Error(fine) < Error(coarse));
        Assert.AreEqual(coarse.Epoch.Clock, reference.Epoch.Clock);
        CollectionAssert.AreEqual(fine.Field.Cells.ToArray(), replay.Field.Cells.ToArray());
        Assert.AreEqual(fine.Epoch.CoupledEnergy!.SurfaceMechanics!.StoredEnergyJoules, replay.Epoch.CoupledEnergy!.SurfaceMechanics!.StoredEnergyJoules, 0.0);
        AssertConservation(fine);
        AssertConservation(reference);
        var donor = funded.Columns!.Columns[0];
        var recipient = PlanetSurfaceGridTopology.GetNeighbor(donor.Id, PlanetGridDirection.East).Cell;
        Assert.ThrowsExactly<InvalidOperationException>(() => funded.AdvanceWithMeltTransfers(PlanetOriginDuration.FromSeconds(1.0),
            ClosedBoundary, [new(donor.Id, 0, recipient, 0, donor.Layers[0].MassKilograms * 0.01)]));
    }

    [TestMethod]
    public void MechanicalBudget_OverdrawInvalidInputAndCancellation_CannotCreateEnergy()
    {
        var initial = new PlanetOriginMechanicalEnergyBudget(100.0);
        var next = initial.Thermalize(25.0);
        Assert.AreEqual(75.0, next.StoredEnergyJoules);
        Assert.AreEqual(25.0, next.CumulativeThermalizedJoules);
        Assert.AreEqual(0.0, next.ConservationResidualJoules);
        Assert.AreEqual(100.0, initial.StoredEnergyJoules);
        Assert.ThrowsExactly<InvalidOperationException>(() => next.Thermalize(76.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => next.Thermalize(-1.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetOriginMechanicalEnergyBudget(double.NaN));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => CreateState(true).FundViscousMeltMechanics(Flow, cancellation.Token));
    }

    [TestMethod]
    public void Cooling_FrozenOrMushySurface_BlocksFlowWithoutLosingItsPotentialAccount()
    {
        var funded = CreateState(true).FundViscousMeltMechanics(Flow);
        var cold = PlanetOriginRockColumnIntegrator.Advance(funded, PlanetOriginDuration.FromSeconds(1_200.0),
            ClosedBoundary with { InfraredEmissivity = 0.9 }, new(10.0, 10_000));
        Assert.IsTrue(cold.Columns!.Columns.All(column => column.Layers[0].Resolve(Material).MeltFraction < 1.0));
        var trial = cold.Columns.ProposeViscousMeltFlow(PlanetOriginDuration.FromSeconds(1.0), cold.MechanicalReference!);
        Assert.AreEqual(0, trial.Faces.Count);
        Assert.AreEqual(0.0, trial.DissipatedWorkJoules);
        Assert.AreEqual(cold.Columns.Columns.Count, trial.ExcludedColumnCount);
        var next = cold.AdvanceWithViscousMeltFlow(PlanetOriginDuration.FromSeconds(1.0), ClosedBoundary);
        Assert.AreEqual(cold.Epoch.CoupledEnergy!.SurfaceMechanics!.StoredEnergyJoules, next.Epoch.CoupledEnergy!.SurfaceMechanics!.StoredEnergyJoules, 0.0);
        AssertConservation(next);
    }

    [TestMethod]
    public void CanonicalGravity_ChangedDensityOrGravity_IsRejectedRatherThanCreatingUnfundedWork()
    {
        var funded = CreateState(true).FundViscousMeltMechanics(Flow);
        var changed = funded.MechanicalReference! with { Parameters = Flow with { GravityMetersPerSecondSquared = Flow.GravityMetersPerSecondSquared * 2.0 } };
        Assert.ThrowsExactly<InvalidOperationException>(() => new PlanetOriginCanonicalRockCells(funded.Epoch, funded.Field, funded.Columns, changed));
        Assert.ThrowsExactly<InvalidOperationException>(() => funded.Columns!.ProposeViscousMeltFlow(PlanetOriginDuration.FromSeconds(1.0),
            funded.MechanicalReference with { DensityKilogramsPerCubicMeter = 6_000.0 }));
        Assert.ThrowsExactly<InvalidOperationException>(() => new PlanetOriginCanonicalRockCells(funded.Epoch, funded.Field, funded.Columns));
    }

    private static double Head(PlanetOriginRockColumn column, PlanetOriginRockColumnField columns) =>
        (column.MassKilograms - column.ReferenceMassKilograms) / (columns.Properties.LiquidDensityKilogramsPerCubicMeter * column.AreaSquareMeters);

    private static void AssertConservation(PlanetOriginCanonicalRockCells state)
    {
        var ledger = state.Epoch.CoupledEnergy!;
        Assert.AreEqual(0.0, ledger.ConservationResidualJoules, ledger.InitialStoredEnergyJoules * 1e-12);
        var potential = state.MechanicalReference!.PotentialEnergyJoules(state.Columns!);
        Assert.AreEqual(potential, ledger.SurfaceMechanics!.StoredEnergyJoules, potential * 2e-12);
        Assert.AreEqual(0.0, ledger.SurfaceMechanics.ConservationResidualJoules, potential * 2e-12);
    }

    private static PlanetOriginCanonicalRockCells CreateState(bool initialDisplacement, bool variableDensity = false)
    {
        var mantle = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(4e24, 1_200.0, 400_000.0, 1_300.0, 1_900.0));
        var interior = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(mantle.CalculateStoredEnergyJoules(2_400.0)), mantle);
        var mass = 1e19 * Math.Pow(Radius / 6_371_000.0, 2);
        var energy = new PlanetRockEnthalpyModel(Material.ToRockProperties(mass)).CalculateStoredEnergyJoules(2_200.0);
        var environment = new PlanetWaterEnvironmentalState(PlanetWaterInventory.Empty, new PlanetEnvironmentalEnergyBudget(0.0, 0.0, energy));
        var capacity = new PlanetEnvironmentHeatCapacityParameters(0.0, 1_000.0, 2_000.0, 4_200.0, mass, 850.0) { SurfaceRockPhaseMaterial = Material };
        var ledger = new PlanetOriginEnergyLedger(interior, environment, capacity);
        var field = PlanetOriginSurfaceRockPhaseField.FromCanonicalPhaseSurface(ledger, new PlanetSurfaceGridLayout(1), Radius);
        var columns = PlanetOriginRockColumnField.Partition(field, new(8, 3_000.0, variableDensity ? 2_800.0 : 3_000.0, 0.0));
        if (initialDisplacement)
        {
            var donor = columns.Columns[0];
            var recipient = PlanetSurfaceGridTopology.GetNeighbor(donor.Id, PlanetGridDirection.East).Cell;
            columns = columns.ProposeMeltTransfers([new(donor.Id, 0, recipient, 0, donor.Layers[0].MassKilograms * 0.02)]).Candidate;
        }
        return new(new PlanetOriginEpochState(ledger), columns.Aggregate, columns);
    }
}
