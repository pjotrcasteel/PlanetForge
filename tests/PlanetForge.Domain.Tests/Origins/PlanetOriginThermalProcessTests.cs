using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginThermalProcessTests
{
    private static PlanetOriginThermalBoundaryParameters Boundary => new(
        RadiusMeters: 1.0,
        StellarIrradianceWattsPerSquareMeter: 0.0,
        BondAlbedo: 0.3,
        EffectiveInfraredEmissivity: 0.1,
        EffectiveEmissionTemperatureRatio: 1.0,
        InteriorHeatTransferWattsPerSquareMeterKelvin: 0.05);

    [TestMethod]
    public void Propose_HotRockAndSteam_TransfersInteriorHeatAndRecalculatesRadiation()
    {
        var original = CreateWorld(650.0, 300.0);
        var proposal = PlanetOriginThermalProcess.Propose(original.CoupledEnergy!, PlanetOriginDuration.FromSeconds(1), Boundary);
        var area = 4.0 * Math.PI;

        Assert.AreEqual(area * 0.05 * 350.0, proposal.InteriorExport.Watts, 1e-8);
        Assert.AreEqual(PlanetEnvironmentHeatReservoir.Atmosphere, proposal.InteriorExport.Destination);
        Assert.AreEqual(PlanetEnvironmentHeatReservoir.Atmosphere, proposal.Radiation.EmittingReservoir);
        Assert.AreEqual(area * 0.1 * PlanetRadiationBalanceModel.StefanBoltzmannWattsPerSquareMeterKelvinFourth *
            Math.Pow(300.0, 4.0), proposal.Radiation.Balance.EmittedThermalPowerWatts, 1e-8);
        Assert.AreEqual(0, proposal.PhaseChanges.Count);
        Assert.AreEqual(0, proposal.VolatileChanges.Count);
    }

    [TestMethod]
    public void AdvanceCoupledAdaptive_RockCoolsAndCrystallizesWithoutInventingWater()
    {
        var initial = CreateWorld(650.0, 300.0);
        var options = new PlanetOriginAdaptiveIntegrationOptions(
            PlanetOriginDuration.FromSeconds(20), PlanetOriginDuration.FromSeconds(1));
        var initialMelt = initial.Rock.MeltFraction;
        var initialVapor = initial.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor);
        var initialEnergy = initial.CoupledEnergy!.StoredEnergyJoules;

        var result = initial.AdvanceCoupledAdaptive(PlanetOriginDuration.FromSeconds(60), options,
            (candidate, interval) => PlanetOriginThermalProcess.Propose(candidate, interval, Boundary));

        Assert.IsLessThan(initial.Rock.TemperatureKelvin, result.Rock.TemperatureKelvin);
        Assert.IsLessThan(initialMelt, result.Rock.MeltFraction);
        Assert.IsLessThan(initialEnergy, result.CoupledEnergy!.StoredEnergyJoules);
        Assert.AreEqual(initialVapor, result.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(0.0, result.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(60_000_000L, result.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(1L, result.Revision);
        Assert.AreEqual(0.0, result.EnergyConservationResidualJoules, 1e-5);
        Assert.AreEqual(0.0, result.WaterConservationResidualKilograms, 0.0);
        Assert.AreEqual(0L, initial.Revision);
        Assert.AreEqual(initialMelt, initial.Rock.MeltFraction, 0.0);
    }

    [TestMethod]
    public void Propose_NoInteriorTemperatureGradient_DoesNotCreateInteriorExport()
    {
        var world = CreateWorld(300.0, 300.0);
        var proposal = PlanetOriginThermalProcess.Propose(world.CoupledEnergy!, PlanetOriginDuration.FromSeconds(2), Boundary);

        Assert.AreEqual(0.0, proposal.InteriorExport.Watts, 0.0);
        Assert.IsGreaterThan(0.0, proposal.Radiation.Balance.EmittedThermalPowerWatts);
    }

    [TestMethod]
    public void Propose_InvalidCalibrationAndZeroTime_AreRejected()
    {
        var energy = CreateWorld(650.0, 300.0).CoupledEnergy!;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginThermalProcess.Propose(energy, PlanetOriginDuration.FromSeconds(1),
                Boundary with { EffectiveEmissionTemperatureRatio = 1.1 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginThermalProcess.Propose(energy, PlanetOriginDuration.FromSeconds(1),
                Boundary with { InteriorHeatTransferWattsPerSquareMeterKelvin = double.NaN }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginThermalProcess.Propose(energy, new PlanetOriginDuration(0), Boundary));
    }

    private static PlanetOriginEpochState CreateWorld(double rockTemperatureKelvin, double atmosphereTemperatureKelvin)
    {
        var rockModel = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(
            100.0, 100.0, 10_000.0, 400.0, 800.0));
        var interior = new PlanetOriginThermalTimeline(
            PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(rockModel.CalculateStoredEnergyJoules(rockTemperatureKelvin)), rockModel);
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 5.0);
        var capacity = new PlanetEnvironmentHeatCapacityParameters(
            NoncondensableAtmosphereMassKilograms: 100.0,
            NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin: 1_000.0,
            VaporSpecificHeatJoulesPerKilogramKelvin: 2_000.0,
            LiquidWaterSpecificHeatJoulesPerKilogramKelvin: 4_000.0,
            SurfaceRockMassKilograms: 1_000.0,
            SurfaceRockSpecificHeatJoulesPerKilogramKelvin: 800.0);
        var atmosphereCapacity = 100.0 * 1_000.0 + 5.0 * 2_000.0;
        var environment = new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(
                atmosphereTemperatureKelvin * atmosphereCapacity, 0.0, 300.0 * 1_000.0 * 800.0));
        return new PlanetOriginEpochState(new PlanetOriginEnergyLedger(interior, environment, capacity));
    }
}
