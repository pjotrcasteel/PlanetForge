using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginCanonicalRockCellsTests
{
    private const double Radius = 6_371_000.0;
    private static readonly PlanetOriginSurfaceRockPhaseMaterial Material = new(850.0, 400_000.0, 1_300.0, 1_900.0);

    [TestMethod]
    public void Initialize_PhaseFundedHotSurface_ResolvesTrueMeltWithoutCreatingEnergy()
    {
        var state = CreateCanonical();
        Assert.AreEqual(1_600.0, state.Epoch.Temperatures!.Value.SurfaceRockTemperatureKelvin!.Value, 1e-9);
        Assert.AreEqual(0.5, state.Field.Cells[0].Resolve(Material).MeltFraction, 1e-12);
        Assert.AreEqual(state.Epoch.CoupledEnergy!.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock),
            state.Field.TotalStoredEnthalpyJoules, state.Field.TotalStoredEnthalpyJoules * 1e-12);
    }

    [TestMethod]
    public void Advance_LocalSolarRadiation_IsConservativeDeterministicAndEpochOwned()
    {
        var original = CreateCanonical();
        var params1 = new PlanetOriginRegionalAirlessParameters(
            new PlanetVector(1.0, 0.0, 0.0), 1_361.0, 0.15, 0.9, 0.0, 25.0);
        var next = original.Advance(PlanetOriginDuration.FromSeconds(1.0), params1);
        var replay = original.Advance(PlanetOriginDuration.FromSeconds(1.0), params1);

        Assert.AreEqual(1L, next.Epoch.Revision);
        Assert.AreEqual(1_000_000L, next.Epoch.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(0.0, next.Epoch.EnergyConservationResidualJoules,
            next.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
        Assert.AreEqual(0.0, next.Epoch.WaterConservationResidualKilograms, 0.0);
        Assert.AreEqual(next.Field.TotalStoredEnthalpyJoules, replay.Field.TotalStoredEnthalpyJoules, 0.0);
        Assert.AreEqual(original.Epoch.CoupledEnergy!.Environment.Environment.TotalEnergyJoules,
            original.Epoch.CoupledEnergy!.Environment.Environment.TotalEnergyJoules, 0.0);
        Assert.AreEqual(original.Epoch.Revision, 0L);
        Assert.AreEqual(next.Epoch.CoupledEnergy.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock),
            next.Field.TotalStoredEnthalpyJoules, next.Field.TotalStoredEnthalpyJoules * 1e-12);
    }

    [TestMethod]
    public void Advance_UnsupportedSteam_RejectsWithoutAnyPartialEpoch()
    {
        var original = CreateCanonical(withVapor: true);
        var parameters = new PlanetOriginRegionalAirlessParameters(
            new PlanetVector(1.0, 0.0, 0.0), 1_361.0, 0.15, 0.9, 0.0, 25.0);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            original.Advance(PlanetOriginDuration.FromSeconds(1.0), parameters));
        Assert.AreEqual(0L, original.Epoch.Clock.MicrosecondsIntoYear);
    }

    [TestMethod]
    public void Advance_InvalidTimeOrCancellation_RejectsBeforePublication()
    {
        var state = CreateCanonical();
        var parameters = new PlanetOriginRegionalAirlessParameters(
            new PlanetVector(1.0, 0.0, 0.0), 1_361.0, 0.15, 0.9, 0.0, 25.0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => state.Advance(new PlanetOriginDuration(0), parameters));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            state.Advance(PlanetOriginDuration.FromSeconds(1.0), parameters, cancelled.Token));
        Assert.AreEqual(0L, state.Epoch.Revision);
    }

    [TestMethod]
    public void Advance_HotSurface_ReturnsHeatToMantleWithoutExternalSource()
    {
        var start = CreateCanonical(interiorKelvin: 1_500.0);
        var next = start.Advance(PlanetOriginDuration.FromSeconds(1.0), ConductionOnly());
        var initialLedger = start.Epoch.CoupledEnergy!;
        var nextLedger = next.Epoch.CoupledEnergy!;
        var returned = nextLedger.CumulativeInteriorImportJoules;

        Assert.IsGreaterThan(0.0, returned);
        Assert.AreEqual(0.0, nextLedger.CumulativeInteriorExportJoules, 0.0);
        Assert.AreEqual(initialLedger.Interior.EnergyBudget.StoredEnergyJoules + returned,
            nextLedger.Interior.EnergyBudget.StoredEnergyJoules, returned * 1e-5);
        Assert.AreEqual(start.Field.TotalStoredEnthalpyJoules - returned, next.Field.TotalStoredEnthalpyJoules,
            start.Field.TotalStoredEnthalpyJoules * 1e-12);
        Assert.AreEqual(initialLedger.StoredEnergyJoules, nextLedger.StoredEnergyJoules, initialLedger.StoredEnergyJoules * 1e-12);
        Assert.AreEqual(0.0, next.Epoch.EnergyConservationResidualJoules, initialLedger.StoredEnergyJoules * 1e-12);
        Assert.AreEqual(0.0, nextLedger.Interior.EnergyBudget.ConservationResidualJoules, initialLedger.StoredEnergyJoules * 1e-12);
        Assert.IsTrue(next.Field.Cells.All(cell => cell.Resolve(Material).TemperatureKelvin < 1_600.0));
    }

    [TestMethod]
    public void Advance_MixedHotColdRegions_TracksBothDirectionsAndReplays()
    {
        var original = CreateCanonical(interiorKelvin: 1_600.0);
        var field = original.Field.RedistributeInitialEnergy(cell =>
            (1.0 + PlanetSurfaceGridGeometry.GetCenterDirection(cell.Id).X) * cell.ActiveRockMassKilograms,
            original.Field.TotalStoredEnthalpyJoules * 0.1);
        var start = new PlanetOriginCanonicalRockCells(original.Epoch, field);
        var next = start.Advance(PlanetOriginDuration.FromSeconds(1.0), ConductionOnly());
        var replay = start.Advance(PlanetOriginDuration.FromSeconds(1.0), ConductionOnly());
        var ledger = next.Epoch.CoupledEnergy!;

        Assert.IsGreaterThan(0.0, ledger.CumulativeInteriorExportJoules);
        Assert.IsGreaterThan(0.0, ledger.CumulativeInteriorImportJoules);
        Assert.AreEqual(start.Field.TotalStoredEnthalpyJoules + ledger.CumulativeInteriorExportJoules - ledger.CumulativeInteriorImportJoules,
            next.Field.TotalStoredEnthalpyJoules, start.Field.TotalStoredEnthalpyJoules * 1e-12);
        CollectionAssert.AreEqual(next.Field.Cells.ToArray(), replay.Field.Cells.ToArray());
        Assert.AreEqual(0.0, next.Epoch.EnergyConservationResidualJoules, ledger.InitialStoredEnergyJoules * 1e-12);
        Assert.AreEqual(0L, start.Epoch.Revision);
        Assert.AreEqual(0.0, start.Epoch.CoupledEnergy!.CumulativeInteriorImportJoules, 0.0);
    }

    [TestMethod]
    public void Advance_EquilibriumOrRejectedReturn_PreservesCanonicalSource()
    {
        var state = CreateCanonical(interiorKelvin: 1_600.0);
        var next = state.Advance(PlanetOriginDuration.FromSeconds(1.0), ConductionOnly());
        Assert.AreEqual(state.Field.TotalStoredEnthalpyJoules, next.Field.TotalStoredEnthalpyJoules, state.Field.TotalStoredEnthalpyJoules * 1e-12);
        var hot = CreateCanonical(interiorKelvin: 1_500.0);
        Assert.ThrowsExactly<InvalidOperationException>(() => hot.Advance(
            PlanetOriginDuration.FromSeconds(1.0), ConductionOnly() with { ConductivityWattsPerMeterKelvin = 1e30 }));
        Assert.AreEqual(0L, hot.Epoch.Revision);
        Assert.AreEqual(0.0, hot.Epoch.CoupledEnergy!.CumulativeInteriorImportJoules, 0.0);
    }

    [TestMethod]
    public void Advance_HeatStepOvershootsBeforeEnergyOverdraw_RejectsThermalInstability()
    {
        var state = CreateCanonical(interiorKelvin: 1_500.0);
        var parameters = ConductionOnly() with { ConductivityWattsPerMeterKelvin = 1e9 };
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => state.Advance(PlanetOriginDuration.FromSeconds(1.0), parameters));
        StringAssert.Contains(error.Message, "stability bound");
        Assert.AreEqual(0L, state.Epoch.Revision);
    }

    [TestMethod]
    public void Advance_SmallCommonMantle_RejectsAggregateInteriorCoupling()
    {
        var state = CreateCanonical(interiorMass: 1e8);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => state.Advance(PlanetOriginDuration.FromSeconds(1.0), ConductionOnly()));
        StringAssert.Contains(error.Message, "stability bound");
        Assert.AreEqual(0L, state.Epoch.Revision);
    }

    [TestMethod]
    public void Advance_SubdividedConduction_ConvergesAndStaysWithinInitialThermalBounds()
    {
        var initial = CreateCanonical(interiorKelvin: 1_500.0);
        var parameters = ConductionOnly() with { ConductivityWattsPerMeterKelvin = 1e5 };
        double TemperatureAt(int count)
        {
            var state = initial;
            for (var i = 0; i < count; i++)
                state = state.Advance(PlanetOriginDuration.FromSeconds(1.0 / count), parameters);
            var temperature = state.Field.Cells[0].Resolve(Material).TemperatureKelvin;
            Assert.IsTrue(temperature > 1_500.0 && temperature < 1_600.0);
            Assert.AreEqual(1_000_000L, state.Epoch.Clock.MicrosecondsIntoYear);
            Assert.AreEqual(0.0, state.Epoch.EnergyConservationResidualJoules, initial.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
            return temperature;
        }

        var coarse = TemperatureAt(1);
        var medium = TemperatureAt(2);
        var fine = TemperatureAt(4);
        var reference = TemperatureAt(1_000);
        Assert.IsLessThan(Math.Abs(coarse - reference), Math.Abs(medium - reference));
        Assert.IsLessThan(Math.Abs(medium - reference), Math.Abs(fine - reference));
    }

    private static PlanetOriginRegionalAirlessParameters ConductionOnly() =>
        new(new PlanetVector(1.0, 0.0, 0.0), 0.0, 0.0, 0.0, 3.0, 25.0);

    private static PlanetOriginCanonicalRockCells CreateCanonical(bool withVapor = false, double interiorKelvin = 1_750.0, double interiorMass = 4.0e24)
    {
        var mass = 1.0e19;
        var rock = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(
            interiorMass, 1_200.0, 400_000.0, 1_300.0, 1_900.0));
        var interior = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(rock.CalculateStoredEnergyJoules(interiorKelvin)), rock);
        var inventory = withVapor ? PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 1.0e15)
            : PlanetWaterInventory.Empty;
        var environment = new PlanetWaterEnvironmentalState(inventory, new PlanetEnvironmentalEnergyBudget(
            withVapor ? 1.0e15 * 2_000.0 * 500.0 : 0.0, 0.0,
            new PlanetRockEnthalpyModel(Material.ToRockProperties(mass)).CalculateStoredEnergyJoules(1_600.0)));
        var capacities = new PlanetEnvironmentHeatCapacityParameters(
            0.0, 1_000.0, 2_000.0, 4_200.0, mass, Material.SpecificHeatCapacityJoulesPerKilogramKelvin)
        {
            SurfaceRockPhaseMaterial = Material
        };
        var energy = new PlanetOriginEnergyLedger(interior, environment, capacities);
        var epoch = new PlanetOriginEpochState(energy);
        var field = PlanetOriginSurfaceRockPhaseField.FromCanonicalPhaseSurface(energy, new PlanetSurfaceGridLayout(2), Radius);
        return new PlanetOriginCanonicalRockCells(epoch, field);
    }
}
