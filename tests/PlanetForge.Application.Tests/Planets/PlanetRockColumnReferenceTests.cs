using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Planets;

[TestClass]
public sealed class PlanetRockColumnReferenceTests
{
    [TestMethod]
    public void Advance_FourHours_FormsThermalSolidTopWhileDepthRemainsMoltenAndEnergyCloses()
    {
        var initial = PlanetRockColumnReference.CreateInitial(6_371_000.0);
        var source = new FlatElevation();
        var experience = new PlanetExperience(new PlanetSurfaceMeshCache(new PlanetSurfaceMeshBuilder(new PlanetSurfaceTileSampler(source))),
            new PlanetLocalSurfacePatchSampler(source), new PlanetLocalSurfaceMeshBuilder());
        var conditions = experience.CreateSnapshot();
        var candidate = initial;
        for (var hour = 0; hour < 4; hour++) candidate = PlanetRockColumnReference.Advance(candidate, conditions);
        Assert.AreEqual(initial.Epoch.Clock.MicrosecondsIntoYear + 14_400_000_000L, candidate.Epoch.Clock.MicrosecondsIntoYear);
        Assert.IsTrue(candidate.Columns!.Columns.All(c => c.Layers[0].Resolve(candidate.Columns.Material).MeltFraction == 0.0));
        Assert.IsTrue(candidate.Columns.Columns.All(c => c.Layers[^1].Resolve(candidate.Columns.Material).MeltFraction > 0.9));
        Assert.IsTrue(candidate.Columns.Columns.All(c => c.SolidSurfaceThicknessMeters(candidate.Columns.Material, candidate.Columns.Properties) > 0.0));
        Assert.IsTrue(candidate.Columns.Columns.All(c => c.ThicknessMeters(candidate.Columns.Material, candidate.Columns.Properties) < c.ReferenceThicknessMeters));
        Assert.AreEqual(initial.Field.TotalActiveRockMassKilograms, candidate.Field.TotalActiveRockMassKilograms, initial.Field.TotalActiveRockMassKilograms * 1e-12);
        Assert.AreEqual(0.0, candidate.Epoch.EnergyConservationResidualJoules, candidate.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-11);
        Assert.AreEqual(0.0, candidate.Epoch.Water.PlanetaryWaterMassKilograms, 0.0);
        Assert.AreEqual(0L, initial.Epoch.Revision);
        var atlas = PlanetOriginSpatialSurfaceProjection.Create(candidate);
        Assert.IsTrue(atlas.HasDepthColumns);
        Assert.IsTrue(atlas.MeltFraction.All(f => f == 0.0f));
    }

    private sealed class FlatElevation : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }
}
