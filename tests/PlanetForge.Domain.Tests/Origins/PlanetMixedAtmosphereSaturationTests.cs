using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetMixedAtmosphereSaturationTests
{
    private const double EarthRadiusMeters = 6_371_000.0;
    private const double EarthGravityMetersPerSecondSquared = 9.81;

    private static PlanetMixedAtmosphereParameters PureSteam => new(
        EarthRadiusMeters, EarthGravityMetersPerSecondSquared, 0.0, 0.02897);

    private static PlanetMixedAtmosphereParameters WithDryGas => PureSteam with
    {
        DryGasMassKilograms = 5.0e18
    };

    [TestMethod]
    public void Evaluate_PureSteam_ReproducesExistingHydrostaticSaturationLimit()
    {
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 5.0e17);
        var existing = PlanetWaterCondensationModel.Estimate(water, 300.0,
            EarthRadiusMeters, EarthGravityMetersPerSecondSquared);

        var result = PlanetMixedAtmosphereSaturation.Evaluate(water, PureSteam, 300.0);

        Assert.AreEqual(PlanetLiquidVaporSaturationRegime.LiquidVaporIapws, result.Regime);
        Assert.AreEqual(existing.VaporPressurePascals, result.WaterVaporPartialPressurePascals, 1e-8);
        Assert.AreEqual(existing.SaturationPressurePascals, result.WaterSaturationPressurePascals!.Value, 1e-8);
        Assert.AreEqual(existing.PotentialCondensateMassKilograms, result.PotentialCondensateMassKilograms!.Value, 5.0e3);
        Assert.AreEqual(0.0, result.DryGasPartialPressurePascals, 1e-8);
        Assert.AreEqual(water.PlanetaryWaterMassKilograms, 5.0e17, 0.0);
    }

    [TestMethod]
    public void Evaluate_DryGasMixing_UsesMoleFractionRatherThanNaiveWaterMassFraction()
    {
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 2.0e17);
        var pure = PlanetMixedAtmosphereSaturation.Evaluate(water, PureSteam, 300.0);
        var mixed = PlanetMixedAtmosphereSaturation.Evaluate(water, WithDryGas, 300.0);

        Assert.IsTrue(mixed.TotalSurfacePressurePascals > pure.TotalSurfacePressurePascals);
        Assert.IsTrue(mixed.WaterVaporPartialPressurePascals > pure.WaterVaporPartialPressurePascals);
        Assert.IsTrue(mixed.WaterVaporPartialPressurePascals < mixed.TotalSurfacePressurePascals);
        Assert.IsTrue(mixed.PotentialCondensateMassKilograms!.Value > pure.PotentialCondensateMassKilograms!.Value);
        Assert.AreEqual(mixed.TotalSurfacePressurePascals,
            mixed.WaterVaporPartialPressurePascals + mixed.DryGasPartialPressurePascals, 1e-9);
        Assert.AreEqual(2.0e17, water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
    }

    [TestMethod]
    public void Evaluate_CondensateProposal_WhenReevaluatedAtRemainingVapor_IsNearSaturation()
    {
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 5.0e17);
        var first = PlanetMixedAtmosphereSaturation.Evaluate(water, WithDryGas, 300.0);
        var condensation = first.PotentialCondensateMassKilograms!.Value;

        Assert.IsTrue(condensation > 0.0);
        Assert.IsTrue(condensation <= water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor));
        var after = water.Transfer(PlanetWaterReservoir.AtmosphericVapor, PlanetWaterReservoir.SurfaceLiquid, condensation);
        var next = PlanetMixedAtmosphereSaturation.Evaluate(after, WithDryGas, 300.0);

        Assert.AreEqual(first.WaterSaturationPressurePascals!.Value, next.WaterVaporPartialPressurePascals, 1e-7);
        Assert.AreEqual(water.PlanetaryWaterMassKilograms, after.PlanetaryWaterMassKilograms, 1e3);
        Assert.AreEqual(0.0, after.ConservationResidualKilograms, 1e3);
        Assert.AreEqual(0.0, water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0,
            "This is still a diagnostic; no condensation happened until an explicit water transfer was proposed.");
    }

    [TestMethod]
    public void Evaluate_DryAtmosphereWithoutVapor_CannotCondenseWater()
    {
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.HydratedMantle, 1.0e20);
        var result = PlanetMixedAtmosphereSaturation.Evaluate(water, WithDryGas, 300.0);

        Assert.IsTrue(result.TotalSurfacePressurePascals > 0.0);
        Assert.AreEqual(0.0, result.WaterVaporPartialPressurePascals, 0.0);
        Assert.AreEqual(0.0, result.PotentialCondensateMassKilograms!.Value, 0.0);
        Assert.AreEqual(0.0, water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
    }

    [TestMethod]
    public void Evaluate_OutsideIapwsWaterLiquidVaporRange_ReportsUnsupportedNotFakeRainOrIce()
    {
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 5.0e20);
        var cold = PlanetMixedAtmosphereSaturation.Evaluate(water, WithDryGas, 250.0);
        var supercritical = PlanetMixedAtmosphereSaturation.Evaluate(water, WithDryGas, 700.0);

        Assert.AreEqual(PlanetLiquidVaporSaturationRegime.BelowLiquidTriplePoint, cold.Regime);
        Assert.AreEqual(PlanetLiquidVaporSaturationRegime.AboveWaterCriticalTemperature, supercritical.Regime);
        Assert.IsNull(cold.WaterSaturationPressurePascals);
        Assert.IsNull(cold.PotentialCondensateMassKilograms);
        Assert.IsNull(supercritical.WaterSaturationPressurePascals);
        Assert.IsNull(supercritical.PotentialCondensateMassKilograms);
        Assert.AreEqual(5.0e20, water.PlanetaryWaterMassKilograms, 0.0);
    }

    [TestMethod]
    public void Evaluate_ChangingDryGasMolarMass_ChangesPartialPressureWithoutChangingInventory()
    {
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 2.0e17);
        var heavier = PlanetMixedAtmosphereSaturation.Evaluate(water,
            WithDryGas with { DryGasMolarMassKilogramsPerMole = 0.04401 }, 300.0);
        var lighter = PlanetMixedAtmosphereSaturation.Evaluate(water,
            WithDryGas with { DryGasMolarMassKilogramsPerMole = 0.002016 }, 300.0);

        Assert.IsTrue(heavier.WaterVaporPartialPressurePascals > lighter.WaterVaporPartialPressurePascals);
        Assert.AreEqual(heavier.TotalSurfacePressurePascals, lighter.TotalSurfacePressurePascals, 1e-9);
        Assert.AreEqual(0.0, water.ConservationResidualKilograms, 0.0);
    }

    [TestMethod]
    public void Evaluate_BadGeometryMolarMassAndTemperature_RejectsInputs()
    {
        var water = PlanetWaterInventory.Empty;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetMixedAtmosphereSaturation.Evaluate(water, WithDryGas with { PlanetRadiusMeters = 0.0 }, 300.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetMixedAtmosphereSaturation.Evaluate(water, WithDryGas with { DryGasMassKilograms = -1.0 }, 300.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetMixedAtmosphereSaturation.Evaluate(water, WithDryGas with { DryGasMolarMassKilogramsPerMole = 0.0 }, 300.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetMixedAtmosphereSaturation.Evaluate(water, WithDryGas with { SurfaceGravityMetersPerSecondSquared = double.NaN }, 300.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetMixedAtmosphereSaturation.Evaluate(water, WithDryGas, double.PositiveInfinity));
    }
}
