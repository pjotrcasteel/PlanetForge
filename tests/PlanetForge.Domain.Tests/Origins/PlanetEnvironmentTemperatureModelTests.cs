using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetEnvironmentTemperatureModelTests
{
    private static PlanetEnvironmentHeatCapacityParameters Parameters => new(
        NoncondensableAtmosphereMassKilograms: 10.0,
        NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin: 1_000.0,
        VaporSpecificHeatJoulesPerKilogramKelvin: 2_000.0,
        LiquidWaterSpecificHeatJoulesPerKilogramKelvin: 4_000.0,
        SurfaceRockMassKilograms: 50.0,
        SurfaceRockSpecificHeatJoulesPerKilogramKelvin: 800.0);

    [TestMethod]
    public void Resolve_AtmosphereOceanAndRock_UsesExplicitMassWeightedHeatCapacities()
    {
        var water = PlanetWaterInventory.Empty
            .Deliver(PlanetWaterReservoir.AtmosphericVapor, 5.0)
            .Deliver(PlanetWaterReservoir.SurfaceLiquid, 20.0);
        var state = new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(6.0e6, 24.0e6, 20.0e6));

        var result = PlanetEnvironmentTemperatureModel.Resolve(state, Parameters);

        Assert.AreEqual(20_000.0, result.AtmosphereHeatCapacityJoulesPerKelvin, 0.0);
        Assert.AreEqual(80_000.0, result.OceanHeatCapacityJoulesPerKelvin, 0.0);
        Assert.AreEqual(40_000.0, result.SurfaceRockHeatCapacityJoulesPerKelvin, 0.0);
        Assert.AreEqual(300.0, result.AtmosphereTemperatureKelvin!.Value, 1e-10);
        Assert.AreEqual(300.0, result.OceanTemperatureKelvin!.Value, 1e-10);
        Assert.AreEqual(500.0, result.SurfaceRockTemperatureKelvin!.Value, 1e-10);
        Assert.AreEqual(0.0, state.EnergyConservationResidualJoules, 0.0);
    }

    [TestMethod]
    public void Resolve_CondensationChangesMassAndHeatCapacitiesWithoutCreatingEnvironmentalEnergy()
    {
        var water = PlanetWaterInventory.Empty
            .Deliver(PlanetWaterReservoir.AtmosphericVapor, 5.0)
            .Deliver(PlanetWaterReservoir.SurfaceLiquid, 20.0);
        var original = new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(6.0e6, 24.0e6, 20.0e6));

        var condensed = original.Apply(
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 2.0, 2.0e6),
                PlanetEnvironmentHeatReservoir.Atmosphere)
        ]);

        var temperatures = PlanetEnvironmentTemperatureModel.Resolve(condensed, Parameters);

        Assert.AreEqual(16_000.0, temperatures.AtmosphereHeatCapacityJoulesPerKelvin, 0.0);
        Assert.AreEqual(88_000.0, temperatures.OceanHeatCapacityJoulesPerKelvin, 0.0);
        Assert.AreEqual(625.0, temperatures.AtmosphereTemperatureKelvin!.Value, 1e-10);
        Assert.AreEqual(24.0e6 / 88_000.0, temperatures.OceanTemperatureKelvin!.Value, 1e-10);
        Assert.AreEqual(-4.0e6, condensed.RelativeLatentEnergyJoules, 0.0);
        Assert.AreEqual(0.0, condensed.EnergyConservationResidualJoules, 0.0);
        Assert.AreEqual(25.0, condensed.Water.PlanetaryWaterMassKilograms, 0.0);
        Assert.AreEqual(5.0, original.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        // This calculation diagnoses the states; it does NOT assert that
        // a 625-K atmosphere can physically retain the proposed condensate.
    }

    [TestMethod]
    public void Resolve_AbsentOceanWithZeroEnergy_ReportsUndefinedTemperature()
    {
        var state = new PlanetWaterEnvironmentalState(PlanetWaterInventory.Empty,
            new PlanetEnvironmentalEnergyBudget(0.0, 0.0, 0.0));

        var result = PlanetEnvironmentTemperatureModel.Resolve(state, Parameters);

        Assert.IsNull(result.OceanTemperatureKelvin);
        Assert.IsNotNull(result.AtmosphereTemperatureKelvin);
        Assert.AreEqual(0.0, result.AtmosphereTemperatureKelvin.Value, 0.0);
    }

    [TestMethod]
    public void Resolve_EmptyOceanWithStoredOceanEnergy_RejectsInconsistentWorld()
    {
        var state = new PlanetWaterEnvironmentalState(PlanetWaterInventory.Empty,
            new PlanetEnvironmentalEnergyBudget(0.0, 1.0e6, 0.0));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PlanetEnvironmentTemperatureModel.Resolve(state, Parameters));
    }

    [TestMethod]
    public void Resolve_NoAtmosphereAndNoRock_ReturnsUndefinedCorrespondingTemperatures()
    {
        var parameters = Parameters with
        {
            NoncondensableAtmosphereMassKilograms = 0.0,
            SurfaceRockMassKilograms = 0.0
        };
        var state = new PlanetWaterEnvironmentalState(PlanetWaterInventory.Empty,
            new PlanetEnvironmentalEnergyBudget(0.0, 0.0, 0.0));

        var result = PlanetEnvironmentTemperatureModel.Resolve(state, parameters);

        Assert.IsNull(result.AtmosphereTemperatureKelvin);
        Assert.IsNull(result.SurfaceRockTemperatureKelvin);
        Assert.IsNull(result.OceanTemperatureKelvin);
    }

    [TestMethod]
    public void Resolve_EarthScaleReferenceMasses_YieldFiniteTemperatures()
    {
        var parameters = new PlanetEnvironmentHeatCapacityParameters(5.0e18, 1_005.0, 1_950.0,
            4_000.0, 1.0e19, 840.0);
        var water = PlanetWaterInventory.Empty
            .Deliver(PlanetWaterReservoir.AtmosphericVapor, 1.0e18)
            .Deliver(PlanetWaterReservoir.SurfaceLiquid, 1.0e21);
        var atmosphereCapacity = 5.0e18 * 1_005.0 + 1.0e18 * 1_950.0;
        var state = new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(atmosphereCapacity * 300.0, 1.0e21 * 4_000.0 * 285.0,
                1.0e19 * 840.0 * 1_100.0));

        var result = PlanetEnvironmentTemperatureModel.Resolve(state, parameters);

        Assert.AreEqual(300.0, result.AtmosphereTemperatureKelvin!.Value, 1e-10);
        Assert.AreEqual(285.0, result.OceanTemperatureKelvin!.Value, 1e-10);
        Assert.AreEqual(1_100.0, result.SurfaceRockTemperatureKelvin!.Value, 1e-10);
        Assert.IsTrue(double.IsFinite(result.AtmosphereHeatCapacityJoulesPerKelvin));
        Assert.IsTrue(double.IsFinite(result.OceanHeatCapacityJoulesPerKelvin));
    }

    [TestMethod]
    public void Resolve_InvalidCalibrationsAndOverflow_AreRejectedWithoutChangingWorld()
    {
        var state = new PlanetWaterEnvironmentalState(
            PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 10.0),
            new PlanetEnvironmentalEnergyBudget(1_000.0, 0.0, 0.0));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetEnvironmentTemperatureModel.Resolve(state, Parameters with { SurfaceRockMassKilograms = -1.0 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetEnvironmentTemperatureModel.Resolve(state, Parameters with { VaporSpecificHeatJoulesPerKilogramKelvin = double.NaN }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetEnvironmentTemperatureModel.Resolve(state, Parameters with
            {
                NoncondensableAtmosphereMassKilograms = double.MaxValue,
                NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin = double.MaxValue
            }));
        Assert.AreEqual(10.0, state.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(1_000.0, state.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
    }
}
