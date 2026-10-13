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

    private static PlanetOriginCanonicalRockCells CreateCanonical(bool withVapor = false)
    {
        var mass = 1.0e19;
        var rock = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(
            4.0e24, 1_200.0, 400_000.0, 1_300.0, 1_900.0));
        var interior = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(rock.CalculateStoredEnergyJoules(1_750.0)), rock);
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
