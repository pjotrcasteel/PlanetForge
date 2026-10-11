using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetWaterVaporSaturationTests
{
    private const double EarthRadiusMeters = 6_371_000.0;
    private const double EarthSurfaceGravityMetersPerSecondSquared = 9.81;

    [TestMethod]
    public void CalculateSaturationPressurePascals_IapwsReferencePoints_MatchValidatedCurve()
    {
        Assert.AreEqual(611.657, PlanetWaterVaporSaturation.CalculateSaturationPressurePascals(273.16), 0.01);
        Assert.AreEqual(3_536.718, PlanetWaterVaporSaturation.CalculateSaturationPressurePascals(300.0), 0.5);
        Assert.AreEqual(101_418.0, PlanetWaterVaporSaturation.CalculateSaturationPressurePascals(373.15), 10.0);
        Assert.AreEqual(2_639_223.0, PlanetWaterVaporSaturation.CalculateSaturationPressurePascals(500.0), 500.0);
        Assert.AreEqual(22_064_000.0, PlanetWaterVaporSaturation.CalculateSaturationPressurePascals(647.096), 1e-6);
    }

    [TestMethod]
    public void CalculateSaturationPressurePascals_WarmingTowardCriticalPoint_IncreasesCapacity()
    {
        var temperatures = new[] { 273.16, 300.0, 350.0, 373.15, 450.0, 500.0, 600.0, 647.096 };
        var previous = 0.0;
        foreach (var temperature in temperatures)
        {
            var pressure = PlanetWaterVaporSaturation.CalculateSaturationPressurePascals(temperature);
            Assert.IsTrue(pressure > previous, "Saturated vapour pressure should rise monotonically toward the critical point.");
            previous = pressure;
        }
    }

    [TestMethod]
    public void Estimate_HotSteamAtmosphere_CanHoldWaterThatWouldCondenseAtLowerTemperature()
    {
        var inventory = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 5.0e20);
        var hot = PlanetWaterCondensationModel.Estimate(inventory, 600.0, EarthRadiusMeters, EarthSurfaceGravityMetersPerSecondSquared);
        var cooled = PlanetWaterCondensationModel.Estimate(inventory, 373.15, EarthRadiusMeters, EarthSurfaceGravityMetersPerSecondSquared);

        Assert.AreEqual(0.0, hot.PotentialCondensateMassKilograms, 0.0);
        Assert.IsTrue(cooled.PotentialCondensateMassKilograms > 4.0e20);
        Assert.IsTrue(cooled.SaturationPressurePascals < hot.SaturationPressurePascals);
        Assert.AreEqual(0.0, inventory.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0,
            "A diagnostic saturation estimate must not spontaneously materialize an ocean.");
    }

    [TestMethod]
    public void Estimate_DryWorld_HasNoSpontaneousCondensation()
    {
        var dry = PlanetWaterInventory.Empty;

        var estimate = PlanetWaterCondensationModel.Estimate(dry, 300.0, EarthRadiusMeters, EarthSurfaceGravityMetersPerSecondSquared);

        Assert.AreEqual(0.0, estimate.VaporPressurePascals, 0.0);
        Assert.AreEqual(0.0, estimate.PotentialCondensateMassKilograms, 0.0);
        Assert.AreEqual(0.0, dry.PlanetaryWaterMassKilograms, 0.0);
    }

    [TestMethod]
    public void Estimate_ExplicitVaporToOceanTransfer_ConservesDeliveredWater()
    {
        var original = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 1.0e21);
        var potential = PlanetWaterCondensationModel.Estimate(original, 373.15, EarthRadiusMeters, EarthSurfaceGravityMetersPerSecondSquared);
        var afterCondensation = original.Transfer(PlanetWaterReservoir.AtmosphericVapor,
            PlanetWaterReservoir.SurfaceLiquid, potential.PotentialCondensateMassKilograms);

        Assert.AreEqual(original.PlanetaryWaterMassKilograms, afterCondensation.PlanetaryWaterMassKilograms, 1e7);
        Assert.AreEqual(0.0, afterCondensation.ConservationResidualKilograms, 1e7);
        Assert.AreEqual(0.0, original.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(potential.PotentialCondensateMassKilograms,
            afterCondensation.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 1e7);
    }

    [TestMethod]
    public void CalculateSaturationPressurePascals_OutsideLiquidVaporBoundary_RejectsUnsupportedRegime()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetWaterVaporSaturation.CalculateSaturationPressurePascals(273.15));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetWaterVaporSaturation.CalculateSaturationPressurePascals(647.097));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetWaterVaporSaturation.CalculateSaturationPressurePascals(double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetWaterVaporSaturation.CalculateSaturationPressurePascals(double.PositiveInfinity));
    }

    [TestMethod]
    public void Estimate_ImpossiblePlanetGeometry_RejectsCalculation()
    {
        var water = PlanetWaterInventory.Empty;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetWaterCondensationModel.Estimate(water, 300.0, 0.0, EarthSurfaceGravityMetersPerSecondSquared));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetWaterCondensationModel.Estimate(water, 300.0, EarthRadiusMeters, -1.0));
    }
}
