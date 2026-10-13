using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginEnergyLedgerTests
{
    private const double Year = PlanetThermalEnergyBudget.JulianYearSeconds;

    private static PlanetEnvironmentHeatCapacityParameters Parameters => new(
        NoncondensableAtmosphereMassKilograms: 10.0,
        NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin: 1_000.0,
        VaporSpecificHeatJoulesPerKilogramKelvin: 2_000.0,
        LiquidWaterSpecificHeatJoulesPerKilogramKelvin: 4_000.0,
        SurfaceRockMassKilograms: 10.0,
        SurfaceRockSpecificHeatJoulesPerKilogramKelvin: 800.0);

    [TestMethod]
    public void Advance_AllProcessesTogether_ClosesOneGlobalEnergyBudget()
    {
        var start = CreateLedger();
        var step = new PlanetOriginEnergyStep(1, 100.0 / Year, 50.0 / Year,
            new PlanetInteriorHeatExport(300.0 / Year, PlanetEnvironmentHeatReservoir.SurfaceRock),
            Radiation(absorbedJoulesPerYear: 1.0e6, emittedJoulesPerYear: 2.0e6,
                PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.Atmosphere),
            [
                new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 1.0, 2.0e6),
                    PlanetEnvironmentHeatReservoir.Atmosphere)
            ]);

        var next = start.Advance(step);

        Assert.AreEqual(1_850.0, next.Interior.EnergyBudget.StoredEnergyJoules, 1e-6);
        Assert.AreEqual(300.0, next.CumulativeInteriorExportJoules, 1e-6);
        Assert.AreEqual(8.0e6 + 300.0 + 1.0e6,
            next.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock), 1e-6);
        Assert.AreEqual(start.StoredEnergyJoules + 150.0 + 1.0e6 - 2.0e6, next.StoredEnergyJoules, 1e-6);
        Assert.AreEqual(0.0, next.ConservationResidualJoules, 1e-6);
        Assert.AreEqual(0.0, next.Environment.EnergyConservationResidualJoules, 1e-6,
            "The environment's own phase-change account must stay closed after boundary exchange.");
        Assert.AreEqual(0.0, next.Environment.Water.ConservationResidualKilograms, 0.0);
        Assert.AreEqual(start.Clock.YearsBeforePresent - 1, next.Clock.YearsBeforePresent);
    }

    [TestMethod]
    public void Advance_InteriorExportOnly_MovesHeatWithoutChangingPlanetTotal()
    {
        var start = CreateLedger();

        var next = start.Advance(new PlanetOriginEnergyStep(1, 0.0, 0.0,
            new PlanetInteriorHeatExport(500.0 / Year, PlanetEnvironmentHeatReservoir.Atmosphere),
            Radiation(0.0, 0.0, PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.Atmosphere),
            []));

        Assert.AreEqual(1_500.0, next.Interior.EnergyBudget.StoredEnergyJoules, 1e-6);
        Assert.AreEqual(6.0e6 + 500.0,
            next.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 1e-6);
        Assert.AreEqual(start.StoredEnergyJoules, next.StoredEnergyJoules, 1e-6,
            "Interior cooling into the environment is not a loss to space.");
        Assert.AreEqual(0.0, next.CumulativeEmittedInfraredEnergyJoules, 0.0);
        Assert.AreEqual(0.0, next.ConservationResidualJoules, 1e-6);
    }

    [TestMethod]
    public void Advance_OneLongStepAndTwoShortSteps_AgreeWithoutPhaseChanges()
    {
        var start = CreateLedger();
        PlanetOriginEnergyStep Step(long years) => new(years, 10.0 / Year, 5.0 / Year,
            new PlanetInteriorHeatExport(40.0 / Year, PlanetEnvironmentHeatReservoir.SurfaceOcean),
            Radiation(3.0e5, 1.0e5, PlanetEnvironmentHeatReservoir.SurfaceOcean, PlanetEnvironmentHeatReservoir.Atmosphere),
            []);

        var whole = start.Advance(Step(2));
        var halves = start.Advance(Step(1)).Advance(Step(1));

        Assert.AreEqual(whole.StoredEnergyJoules, halves.StoredEnergyJoules, 1e-6);
        Assert.AreEqual(whole.Interior.EnergyBudget.StoredEnergyJoules, halves.Interior.EnergyBudget.StoredEnergyJoules, 1e-6);
        foreach (var reservoir in Enum.GetValues<PlanetEnvironmentHeatReservoir>())
        {
            Assert.AreEqual(whole.Environment.Environment.GetEnergyJoules(reservoir),
                halves.Environment.Environment.GetEnergyJoules(reservoir), 1e-6);
        }

        Assert.AreEqual(0.0, halves.ConservationResidualJoules, 1e-6);
    }

    [TestMethod]
    public void Advance_EmissionExceedingEmittingStore_RejectsWholeStep()
    {
        var start = CreateLedger();

        Assert.ThrowsExactly<InvalidOperationException>(() => start.Advance(new PlanetOriginEnergyStep(1, 0.0, 0.0,
            new PlanetInteriorHeatExport(100.0 / Year, PlanetEnvironmentHeatReservoir.Atmosphere),
            Radiation(0.0, 7.0e6, PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.Atmosphere),
            [])));

        Assert.AreEqual(2_000.0, start.Interior.EnergyBudget.StoredEnergyJoules, 0.0);
        Assert.AreEqual(6.0e6, start.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
    }

    [TestMethod]
    public void Advance_InteriorExportExceedingInteriorHeat_IsRejected()
    {
        var start = CreateLedger();

        Assert.ThrowsExactly<InvalidOperationException>(() => start.Advance(new PlanetOriginEnergyStep(1, 0.0, 0.0,
            new PlanetInteriorHeatExport(5_000.0 / Year, PlanetEnvironmentHeatReservoir.SurfaceRock),
            Radiation(0.0, 0.0, PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.Atmosphere),
            [])));
    }

    [TestMethod]
    public void Advance_HeatIntoStoreWithoutMatter_IsRejected()
    {
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 5.0);
        var start = new PlanetOriginEnergyLedger(CreateInterior(),
            new PlanetWaterEnvironmentalState(water, new PlanetEnvironmentalEnergyBudget(6.0e6, 0.0, 8.0e6)),
            Parameters);

        Assert.ThrowsExactly<InvalidOperationException>(() => start.Advance(new PlanetOriginEnergyStep(1, 0.0, 0.0,
            new PlanetInteriorHeatExport(100.0 / Year, PlanetEnvironmentHeatReservoir.SurfaceOcean),
            Radiation(0.0, 0.0, PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.Atmosphere),
            [])), "There is no ocean to heat.");
    }

    [TestMethod]
    public void Advance_Cancelled_PublishesNothing()
    {
        var start = CreateLedger();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => start.Advance(new PlanetOriginEnergyStep(1, 0.0, 0.0,
            new PlanetInteriorHeatExport(0.0, PlanetEnvironmentHeatReservoir.Atmosphere),
            Radiation(0.0, 0.0, PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.Atmosphere),
            []), cancellation.Token));
        Assert.AreEqual(0.0, start.ConservationResidualJoules, 0.0);
    }

    [TestMethod]
    public void Constructor_GhostHeatInEmptyStore_IsRejected()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new PlanetOriginEnergyLedger(CreateInterior(),
            new PlanetWaterEnvironmentalState(PlanetWaterInventory.Empty,
                new PlanetEnvironmentalEnergyBudget(0.0, 0.0, 0.0, 1.0)),
            Parameters));
    }

    [TestMethod]
    public void Advance_EnvironmentalReturn_ClosesBothLedgersWithoutExternalHeating()
    {
        var start = CreateLedger();
        var next = start.Advance(ReturnStep(500.0));

        Assert.AreEqual(2_500.0, next.Interior.EnergyBudget.StoredEnergyJoules, 1e-6);
        Assert.AreEqual(8.0e6 - 500.0, next.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock), 1e-6);
        Assert.AreEqual(500.0, next.CumulativeInteriorImportJoules, 1e-6);
        Assert.AreEqual(start.StoredEnergyJoules, next.StoredEnergyJoules, 1e-6);
        Assert.AreEqual(0.0, next.Interior.EnergyBudget.ConservationResidualJoules, 1e-6);
        Assert.AreEqual(0.0, next.Environment.EnergyConservationResidualJoules, 1e-6);
        Assert.AreEqual(0.0, next.ConservationResidualJoules, 1e-6);
        Assert.AreEqual(0.0, next.CumulativeDeliveredMaterialEnergyJoules, 0.0);
        Assert.AreEqual(0.0, next.Interior.EnergyBudget.CumulativeImpactHeatJoules, 0.0);
        Assert.AreEqual(0.0, next.Interior.EnergyBudget.CumulativeMaterialInputJoules, 0.0);
    }

    [TestMethod]
    public void Advance_ReturnThenMaterialExchange_PreservesInternalHeatHistory()
    {
        var start = CreateLedger();
        var next = start.Advance(ReturnStep(500.0) with
        {
            VolatileChanges = [PlanetOriginVolatileExchange.DeliverToMagma(1.0, 100.0)]
        });
        Assert.AreEqual(500.0, next.CumulativeInteriorImportJoules, 1e-6);
        Assert.AreEqual(0.0, next.Interior.EnergyBudget.ConservationResidualJoules, 1e-6);
        Assert.AreEqual(start.StoredEnergyJoules + 100.0, next.StoredEnergyJoules, 1e-6);
    }

    [TestMethod]
    public void Advance_ReturnOverdrawAndInvalidPower_RejectWithoutChangingDonor()
    {
        var start = CreateLedger();
        Assert.ThrowsExactly<InvalidOperationException>(() => start.Advance(ReturnStep(9.0e6)));
        foreach (var power in new[] { -1.0, double.NaN, double.PositiveInfinity })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => start.Advance(ReturnStep(power)));
        Assert.AreEqual(8.0e6, start.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock), 0.0);
        Assert.AreEqual(2_000.0, start.Interior.EnergyBudget.StoredEnergyJoules, 0.0);
        Assert.AreEqual(0L, start.Clock.MicrosecondsIntoYear);
    }

    private static PlanetOriginEnergyStep ReturnStep(double joules) => new(
        PlanetOriginDuration.FromSeconds(1.0), 0.0, 0.0,
        new PlanetInteriorHeatExport(0.0, PlanetEnvironmentHeatReservoir.SurfaceRock),
        Radiation(0.0, 0.0, PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.SurfaceRock), [])
    {
        InteriorImport = new PlanetInteriorHeatImport(joules, PlanetEnvironmentHeatReservoir.SurfaceRock)
    };

    private static PlanetRadiativeExchange Radiation(
        double absorbedJoulesPerYear, double emittedJoulesPerYear,
        PlanetEnvironmentHeatReservoir absorbing, PlanetEnvironmentHeatReservoir emitting) =>
        new(new PlanetRadiativeBalance(1.0, 0.0, 0.0, absorbedJoulesPerYear / Year, emittedJoulesPerYear / Year),
            absorbing, emitting);

    private static PlanetOriginThermalTimeline CreateInterior() => new(
        PlanetOriginClock.EarthAccretionReference,
        new PlanetThermalEnergyBudget(2_000.0),
        new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(2.0, 10.0, 100.0, 100.0, 200.0)));

    private static PlanetOriginEnergyLedger CreateLedger()
    {
        var water = PlanetWaterInventory.Empty
            .Deliver(PlanetWaterReservoir.AtmosphericVapor, 5.0)
            .Deliver(PlanetWaterReservoir.SurfaceLiquid, 20.0);
        return new PlanetOriginEnergyLedger(CreateInterior(),
            new PlanetWaterEnvironmentalState(water, new PlanetEnvironmentalEnergyBudget(6.0e6, 24.0e6, 8.0e6)),
            Parameters);
    }
}
