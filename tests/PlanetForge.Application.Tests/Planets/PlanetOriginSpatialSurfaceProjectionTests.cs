using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;

namespace PlanetForge.Application.Tests.Planets;

[TestClass]
public sealed class PlanetOriginSpatialSurfaceProjectionTests
{
    [TestMethod]
    public void Create_IdenticalCanonicalEpoch_ProducesDeterministicFiniteBoundedAtlas()
    {
        var region = PlanetRegionalCoolingReference.CreateInitial(6_371_000.0);

        var first = PlanetOriginSpatialSurfaceProjection.Create(region);
        var replay = PlanetOriginSpatialSurfaceProjection.Create(region);

        Assert.AreEqual(region.Epoch.Revision, first.Revision);
        Assert.AreEqual(256, first.Width);
        Assert.AreEqual(128, first.Height);
        CollectionAssert.AreEqual(first.TemperatureKelvin, replay.TemperatureKelvin);
        CollectionAssert.AreEqual(first.MeltFraction, replay.MeltFraction);
        Assert.IsTrue(first.TemperatureKelvin.All(float.IsFinite));
        Assert.IsTrue(first.MeltFraction.All(value => float.IsFinite(value) && value >= 0.0f && value <= 1.0f));

        var physical = region.Field.Cells.Select(cell => cell.Resolve(region.Field.Material)).ToArray();
        var minimumTemperature = physical.Min(cell => cell.TemperatureKelvin);
        var maximumTemperature = physical.Max(cell => cell.TemperatureKelvin);
        Assert.IsTrue(first.TemperatureKelvin.All(value => value >= minimumTemperature - 0.001 && value <= maximumTemperature + 0.001));
    }

    [TestMethod]
    public void Create_RegionalContrast_ReconstructsIntermediateValuesWithoutChangingPhysicalEnergy()
    {
        var region = PlanetRegionalCoolingReference.CreateInitial(6_371_000.0);
        var originalEnergy = region.Field.Cells.Sum(cell => cell.StoredEnthalpyJoules);

        var projection = PlanetOriginSpatialSurfaceProjection.Create(region);

        var physical = region.Field.Cells.Select(cell => cell.Resolve(region.Field.Material).TemperatureKelvin).ToArray();
        Assert.IsTrue(projection.TemperatureKelvin.Any(value =>
            physical.All(temperature => Math.Abs(value - temperature) > 0.01)));
        Assert.AreEqual(originalEnergy, region.Field.Cells.Sum(cell => cell.StoredEnthalpyJoules), 0.0);
    }
}
