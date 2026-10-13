using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Planets;

[TestClass]
public sealed class PlanetOriginSpatialSurfaceProjectionTests
{
    [TestMethod]
    public void Create_RadiatingInterface_ProjectsSkinPhaseInsteadOfWarmerVolumeMelt()
    {
        var region = PlanetRockColumnReference.CreateResolvedInitial(PlanetFormationInitialHeat.Create(6371000.0, 24061984));
        var projection = PlanetOriginSpatialSurfaceProjection.Create(region);
        Assert.IsTrue(region.Field.Cells.All(cell => region.ResolveSurfacePhase(cell.Id).MeltFraction == 1.0));
        Assert.IsTrue(projection.MeltFraction.Min() < 0.95f);
        foreach (var cell in region.Field.Cells)
        {
            var phase = region.ResolveRadiatingSurfacePhase(cell.Id);
            var material = region.Field.Material;
            var expectedMelt = Math.Clamp((phase.TemperatureKelvin - material.SolidusTemperatureKelvin) /
                (material.LiquidusTemperatureKelvin - material.SolidusTemperatureKelvin), 0.0, 1.0);
            Assert.AreEqual(expectedMelt, phase.MeltFraction, 1e-12);
            Assert.IsTrue(phase.TemperatureKelvin <= region.ResolveSurfacePhase(cell.Id).TemperatureKelvin);
        }
    }

    [TestMethod]
    public void Create_DepthResolvedCooling_ProjectsRadiatingTopInsteadOfColumnMean()
    {
        var initial = PlanetMagmaOceanReference.CreateInitial(6_371_000.0);
        var columns = PlanetOriginRockColumnField.Partition(initial.Field, new(8, 3_000.0, 2_800.0, 0.0));
        var world = new PlanetOriginCanonicalRockCells(initial.Epoch, columns.Aggregate, columns);
        var cooled = world.Advance(PlanetOriginDuration.FromSeconds(300.0),
            new(PlanetVector.UnitX, 0.0, 0.0, 0.9, 0.0, 25.0));
        cooled = cooled.Advance(PlanetOriginDuration.FromSeconds(300.0),
            new(PlanetVector.UnitX, 0.0, 0.0, 0.9, 0.0, 25.0));
        var projection = PlanetOriginSpatialSurfaceProjection.Create(cooled);
        var top = cooled.Field.Cells.Select(c => cooled.ResolveSurfacePhase(c.Id).TemperatureKelvin).ToArray();
        var mean = cooled.Field.Cells.Select(c => c.Resolve(cooled.Field.Material).TemperatureKelvin).ToArray();
        Assert.IsTrue(projection.TemperatureKelvin.All(t => t >= top.Min() - 0.001 && t <= top.Max() + 0.001));
        Assert.IsTrue(projection.TemperatureKelvin.All(t => t < mean.Min()),
            "The read-only atlas must resolve surface enthalpy, not the hotter depth-averaged state.");
        Assert.AreEqual(initial.Field.TotalActiveRockMassKilograms, cooled.Field.TotalActiveRockMassKilograms,
            initial.Field.TotalActiveRockMassKilograms * 1e-12);
    }

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
