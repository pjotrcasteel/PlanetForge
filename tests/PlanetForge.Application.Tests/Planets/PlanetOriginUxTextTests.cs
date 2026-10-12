using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;

namespace PlanetForge.Application.Tests.Planets;

[TestClass]
public sealed class PlanetOriginUxTextTests
{
    [TestMethod]
    public void GeologicalTime_EarthAccretionReference_ShowsAbsoluteAgeNotExperimentYearZero()
    {
        var origin = Origin();

        Assert.AreEqual("4.540 Ga AGO", PlanetOriginUxText.GeologicalTime(origin));
        Assert.AreEqual("EARTH ORIGIN · PHYSICAL MODEL", PlanetOriginUxText.ScenarioLabel);
    }

    [TestMethod]
    public void StateLabel_HotSteamWorld_DescribesPartialMeltAndSteamWithoutClaimingCrustOrRain()
    {
        var text = PlanetOriginUxText.StateLabel(Origin());

        Assert.AreEqual("PARTLY MOLTEN INTERIOR · STEAM PRESENT", text);
        Assert.IsFalse(text.Contains("CRUST", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("OCEAN", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("RAIN", StringComparison.Ordinal));
    }

    [TestMethod]
    public void StateLabel_LiquidInventory_DoesNotClaimStableOceansOrFirstLife()
    {
        var text = PlanetOriginUxText.StateLabel(Origin() with
        {
            BulkRockMeltFraction = 0.0,
            SurfaceLiquidMassKilograms = 2.0e18
        });

        Assert.AreEqual("SOLID INTERIOR · BULK MODEL · SURFACE LIQUID PRESENT", text);
        Assert.IsFalse(text.Contains("OCEAN", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("LIFE", StringComparison.Ordinal));
    }

    [TestMethod]
    public void StateLabel_SteamFreeIceWorld_ReportsIceInsteadOfDrySurface()
    {
        var text = PlanetOriginUxText.StateLabel(Origin() with
        {
            BulkRockMeltFraction = 1.0,
            AtmosphericSteamMassKilograms = 0.0,
            SurfaceIceMassKilograms = 1.0e16
        });

        Assert.AreEqual("MOLTEN INTERIOR · SURFACE ICE PRESENT", text);
    }

    [TestMethod]
    public void StateLabel_DryInterior_ReportsAbsentSurfaceWaterWithoutInventingEvents()
    {
        var text = PlanetOriginUxText.StateLabel(Origin() with
        {
            BulkRockMeltFraction = 0.0,
            AtmosphericSteamMassKilograms = 0.0
        });

        Assert.AreEqual("SOLID INTERIOR · BULK MODEL · NO SURFACE WATER", text);
    }

    private static PlanetOriginSurfaceSnapshot Origin() => new(
        Revision: 0,
        YearsBeforePresent: 4_540_000_000,
        BulkRockTemperatureKelvin: 1_500.0,
        BulkRockMeltFraction: 0.5,
        NearSurfaceRockTemperatureKelvin: 1_100.0,
        AtmosphereTemperatureKelvin: 800.0,
        AtmosphericSteamMassKilograms: 5.0e16,
        SurfaceLiquidMassKilograms: 0.0,
        SurfaceIceMassKilograms: 0.0,
        DryGasMassKilograms: 1.0e17,
        SurfacePressurePascals: 1.0e6,
        SurfaceWaterLiquidFraction: 0.0,
        SurfaceWaterVaporFraction: 1.0,
        SurfaceWaterIceFraction: 0.0,
        AtmosphericSteamMassFraction: 1.0 / 3.0);
}
