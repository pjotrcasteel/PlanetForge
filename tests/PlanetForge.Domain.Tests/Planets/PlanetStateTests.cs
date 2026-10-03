using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Physics;
using PlanetForge.Domain.Planets;

namespace PlanetForge.Domain.Tests.Planets;

[TestClass]
public sealed class PlanetStateTests
{
    [TestMethod]
    public void Constructor_UsesEarthReferenceSystemsByDefault()
    {
        var state = new PlanetState();

        Assert.AreEqual(EarthReference.MassKilograms, state.PhysicalParameters.MassKilograms);
        Assert.AreEqual(EarthAtmosphereReference.TotalMassKilograms, state.AtmosphereParameters.MassKilograms);
        Assert.AreEqual(EarthWaterReference.TotalHydrosphereMassKilograms, state.WaterParameters.TotalMassKilograms);
    }

    [TestMethod]
    public void ResetEarthReference_RestoresAllCoupledSystems()
    {
        var state = new PlanetState();
        state.SetPhysicalParameters(state.PhysicalParameters with { BondAlbedo = 0.1 });
        state.SetAtmosphereParameters(state.AtmosphereParameters with { CarbonDioxidePartsPerMillion = 1_000.0 });
        state.SetWaterParameters(new WaterParameters(0.0));

        state.ResetEarthReference();

        Assert.AreEqual(EarthReference.BondAlbedo, state.PhysicalParameters.BondAlbedo);
        Assert.AreEqual(EarthAtmosphereReference.PreindustrialCarbonDioxidePartsPerMillion, state.AtmosphereParameters.CarbonDioxidePartsPerMillion);
        Assert.AreEqual(EarthWaterReference.TotalHydrosphereMassKilograms, state.WaterParameters.TotalMassKilograms);
    }
}
