using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginAirlessThermalProcessTests
{
    private static PlanetOriginAirlessThermalParameters Parameters => new(
        new PlanetOriginAirlessRadiationParameters(1.0, 1361.0, 0.3, 0.8),
        new PlanetOriginRockConductionParameters(4.0, 100.0));

    [TestMethod]
    public void Propose_DryHotRock_UsesFourierConductionAndPhysicalSurfaceRadiation()
    {
        var epoch = CreateWorld();
        var step = PlanetOriginAirlessThermalProcess.Propose(
            epoch.CoupledEnergy!, PlanetOriginDuration.FromSeconds(1.0), Parameters);
        var area = 4.0 * Math.PI;
        var expectedConduction = area * 4.0 * (1_500.0 - 800.0) / 100.0;
        var expectedInfrared = area * 0.8 * PlanetRadiationBalanceModel.StefanBoltzmannWattsPerSquareMeterKelvinFourth *
            Math.Pow(800.0, 4.0);
        var expectedStarlight = Math.PI * 1361.0 * (1.0 - 0.3);

        Assert.AreEqual(expectedConduction, step.InteriorExport.Watts, 1e-9);
        Assert.AreEqual(PlanetEnvironmentHeatReservoir.SurfaceRock, step.InteriorExport.Destination);
        Assert.AreEqual(expectedInfrared, step.Radiation.Balance.EmittedThermalPowerWatts, 1e-7);
        Assert.AreEqual(expectedStarlight, step.Radiation.Balance.AbsorbedStellarPowerWatts, 1e-9);
        Assert.AreEqual(PlanetEnvironmentHeatReservoir.SurfaceRock, step.Radiation.AbsorbingReservoir);
        Assert.AreEqual(PlanetEnvironmentHeatReservoir.SurfaceRock, step.Radiation.EmittingReservoir);
        Assert.AreEqual(0, step.PhaseChanges.Count);
        Assert.AreEqual(0, step.VolatileChanges.Count);
    }

    [TestMethod]
    public void AdvanceCoupledAdaptive_AirlessCooling_CrystallizesBulkRockAndPreservesTotalEnergy()
    {
        var initial = CreateWorld();
        var options = new PlanetOriginAdaptiveIntegrationOptions(
            PlanetOriginDuration.FromSeconds(10), PlanetOriginDuration.FromSeconds(1));
        var oldRockTemperature = initial.Rock.TemperatureKelvin;
        var oldMelt = initial.Rock.MeltFraction;
        var oldSurfaceTemperature = initial.Temperatures!.Value.SurfaceRockTemperatureKelvin!.Value;

        var result = initial.AdvanceCoupledAdaptive(PlanetOriginDuration.FromSeconds(60), options,
            (candidate, interval) => PlanetOriginAirlessThermalProcess.Propose(candidate, interval, Parameters));

        Assert.IsLessThan(oldRockTemperature, result.Rock.TemperatureKelvin);
        Assert.IsLessThan(oldMelt, result.Rock.MeltFraction);
        Assert.IsLessThan(oldSurfaceTemperature, result.Temperatures!.Value.SurfaceRockTemperatureKelvin!.Value);
        Assert.AreEqual(60_000_000L, result.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(1L, result.Revision);
        Assert.AreEqual(0.0, result.Water.PlanetaryWaterMassKilograms, 0.0);
        Assert.AreEqual(0.0, result.WaterConservationResidualKilograms, 0.0);
        Assert.AreEqual(0.0, result.EnergyConservationResidualJoules, 1e-4);
        Assert.AreEqual(0L, initial.Revision);
        Assert.AreEqual(oldMelt, initial.Rock.MeltFraction, 0.0);
    }

    [TestMethod]
    public void Propose_AtmosphereOrSurfaceWater_IsRejectedRatherThanPretendingVacuum()
    {
        var original = CreateWorld();
        var dryGas = CreateWorld(dryGasMassKilograms: 5.0);
        var steam = CreateWorld(steamMassKilograms: 1.0);
        var ocean = CreateWorld(liquidMassKilograms: 1.0);
        var ice = CreateWorld(iceMassKilograms: 1.0);

        Assert.ThrowsExactly<InvalidOperationException>(() => Propose(dryGas));
        Assert.ThrowsExactly<InvalidOperationException>(() => Propose(steam));
        Assert.ThrowsExactly<InvalidOperationException>(() => Propose(ocean));
        Assert.ThrowsExactly<InvalidOperationException>(() => Propose(ice));
        Assert.AreEqual(0.0, original.Water.PlanetaryWaterMassKilograms, 0.0);
    }

    [TestMethod]
    public void Propose_SurfaceHotterThanInterior_RejectsUnsupportedReverseHeatFlow()
    {
        var hotSurface = CreateWorld(surfaceTemperatureKelvin: 1_700.0);

        Assert.ThrowsExactly<InvalidOperationException>(() => Propose(hotSurface));
    }

    [TestMethod]
    public void Propose_ZeroTimeOrInvalidBoundary_IsRejectedBeforeEnergyAdvancement()
    {
        var energy = CreateWorld().CoupledEnergy!;

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginAirlessThermalProcess.Propose(energy, new PlanetOriginDuration(0), Parameters));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginAirlessThermalProcess.Propose(energy, PlanetOriginDuration.FromSeconds(1),
                Parameters with { Radiation = Parameters.Radiation with { SurfaceInfraredEmissivity = 1.2 } }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginAirlessThermalProcess.Propose(energy, PlanetOriginDuration.FromSeconds(1),
                Parameters with { Conduction = Parameters.Conduction with { EffectiveBoundaryThicknessMeters = 0.0 } }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginAirlessThermalProcess.Propose(energy, PlanetOriginDuration.FromSeconds(1),
                Parameters with { Conduction = Parameters.Conduction with { ConductivityWattsPerMeterKelvin = double.NaN } }));
    }

    private static PlanetOriginEnergyStep Propose(PlanetOriginEpochState epoch) =>
        PlanetOriginAirlessThermalProcess.Propose(epoch.CoupledEnergy!, PlanetOriginDuration.FromSeconds(1), Parameters);

    private static PlanetOriginEpochState CreateWorld(
        double dryGasMassKilograms = 0.0,
        double steamMassKilograms = 0.0,
        double liquidMassKilograms = 0.0,
        double iceMassKilograms = 0.0,
        double surfaceTemperatureKelvin = 800.0)
    {
        var rock = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(
            1_000.0, 1_000.0, 400_000.0, 1_200.0, 1_800.0));
        var timeline = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(rock.CalculateStoredEnergyJoules(1_500.0)), rock);
        var water = PlanetWaterInventory.Empty
            .Deliver(PlanetWaterReservoir.AtmosphericVapor, steamMassKilograms)
            .Deliver(PlanetWaterReservoir.SurfaceLiquid, liquidMassKilograms)
            .Deliver(PlanetWaterReservoir.SurfaceIce, iceMassKilograms);
        var heatCapacity = new PlanetEnvironmentHeatCapacityParameters(
            dryGasMassKilograms, 1_000.0, 2_000.0, 4_200.0, 10.0, 850.0);
        var environment = new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(
                0.0, 0.0, surfaceTemperatureKelvin * 10.0 * 850.0));
        return new PlanetOriginEpochState(new PlanetOriginEnergyLedger(timeline, environment, heatCapacity));
    }
}
