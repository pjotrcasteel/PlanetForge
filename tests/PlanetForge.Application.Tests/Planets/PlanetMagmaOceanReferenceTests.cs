using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Domain.Origins;

namespace PlanetForge.Application.Tests.Planets;

[TestClass]
public sealed class PlanetMagmaOceanReferenceTests
{
    private const double RadiusMeters = 6_371_000.0;

    [TestMethod]
    public void CreateInitial_EntireSurfaceMolten_IsFundedByCanonicalMaterialEnthalpy()
    {
        var initial = PlanetMagmaOceanReference.CreateInitial(RadiusMeters);
        var material = initial.Field.Material;
        var phases = initial.Field.Cells.Select(cell => cell.Resolve(material)).ToArray();
        var rockLedger = initial.Epoch.CoupledEnergy!;

        Assert.AreEqual(0L, initial.Epoch.Revision);
        Assert.AreEqual(1.0, phases.Min(phase => phase.MeltFraction), 1e-12);
        Assert.IsGreaterThan(material.LiquidusTemperatureKelvin, phases.Min(phase => phase.TemperatureKelvin));
        Assert.IsGreaterThan(0.0, phases.Max(phase => phase.TemperatureKelvin) -
            phases.Min(phase => phase.TemperatureKelvin));
        Assert.AreEqual(initial.Field.TotalStoredEnthalpyJoules,
            rockLedger.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock),
            initial.Field.TotalStoredEnthalpyJoules * 1e-12);
        Assert.AreEqual(0.0, initial.Epoch.EnergyConservationResidualJoules,
            rockLedger.InitialStoredEnergyJoules * 1e-12);
        Assert.AreEqual(0.0, initial.Epoch.Water.PlanetaryWaterMassKilograms, 0.0);
    }

    [TestMethod]
    public void CreateInitial_SameInput_ReplaysExactHeatDistributionAndSphericalProjection()
    {
        var first = PlanetMagmaOceanReference.CreateInitial(RadiusMeters);
        var replay = PlanetMagmaOceanReference.CreateInitial(RadiusMeters);
        var atlasFirst = PlanetOriginSpatialSurfaceProjection.Create(first);
        var atlasReplay = PlanetOriginSpatialSurfaceProjection.Create(replay);

        Assert.AreEqual(first.Field.TotalStoredEnthalpyJoules, replay.Field.TotalStoredEnthalpyJoules, 0.0);
        for (var i = 0; i < first.Field.Cells.Count; i++)
            Assert.AreEqual(first.Field.Cells[i].StoredEnthalpyJoules, replay.Field.Cells[i].StoredEnthalpyJoules, 0.0);
        CollectionAssert.AreEqual(atlasFirst.TemperatureKelvin, atlasReplay.TemperatureKelvin);
        CollectionAssert.AreEqual(atlasFirst.MeltFraction, atlasReplay.MeltFraction);
        Assert.IsTrue(atlasFirst.MeltFraction.All(value => value >= 0.999f && value <= 1.0f));
        Assert.IsTrue(atlasFirst.TemperatureKelvin.All(float.IsFinite));
    }

    [TestMethod]
    public void Advance_ConservedAirlessCooling_DecreasesStoredSurfaceEnthalpyWithoutCreatingWater()
    {
        var initial = PlanetMagmaOceanReference.CreateInitial(RadiusMeters);
        var conditions = new PlanetOriginRegionalAirlessParameters(
            new PlanetForge.Domain.Surface.PlanetVector(1.0, 0.0, 0.0), 1_361.0,
            0.15, 0.90, 0.0, 25.0);
        var advanced = initial;
        for (var index = 0; index < 12; index++)
            advanced = advanced.Advance(PlanetOriginDuration.FromSeconds(300.0), conditions);

        Assert.AreEqual(12L, advanced.Epoch.Revision);
        Assert.IsLessThan(initial.Field.TotalStoredEnthalpyJoules, advanced.Field.TotalStoredEnthalpyJoules);
        Assert.AreEqual(0.0, advanced.Epoch.EnergyConservationResidualJoules,
            advanced.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-11);
        Assert.AreEqual(0.0, advanced.Epoch.WaterConservationResidualKilograms, 0.0);
        Assert.AreEqual(0L, initial.Epoch.Revision);
    }

    [TestMethod]
    public void CreateInitial_ContrastReference_RemainsPartiallyMoltenAndDifferent()
    {
        var contrast = PlanetRegionalCoolingReference.CreateInitial(RadiusMeters);
        var molten = PlanetMagmaOceanReference.CreateInitial(RadiusMeters);

        Assert.IsLessThan(1.0, contrast.Field.Cells.Min(cell => cell.Resolve(contrast.Field.Material).MeltFraction));
        Assert.AreEqual(1.0, molten.Field.Cells.Min(cell => cell.Resolve(molten.Field.Material).MeltFraction), 1e-12);
        Assert.IsGreaterThan(contrast.Field.TotalStoredEnthalpyJoules, molten.Field.TotalStoredEnthalpyJoules);
    }
}
