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
        var diagnostic = PlanetOriginRockColumnResolutionSample.FromColumn(
            candidate.Columns.Columns[0], candidate.Columns.Material, candidate.Columns.Properties);
        Assert.AreEqual(8, diagnostic.LayerCount);
        Assert.AreEqual(1, diagnostic.FullySolidTopLayers);
        Assert.IsGreaterThan(0.0, diagnostic.UnresolvedCellWidthMeters);
        Assert.AreEqual(candidate.Columns.Columns[0].SolidSurfaceThicknessMeters(candidate.Columns.Material, candidate.Columns.Properties),
            diagnostic.FullySolidTopMeters, 1e-8);
        Assert.IsTrue(candidate.Columns.Columns.All(c => c.ThicknessMeters(candidate.Columns.Material, candidate.Columns.Properties) < c.ReferenceThicknessMeters));
        Assert.AreEqual(initial.Field.TotalActiveRockMassKilograms, candidate.Field.TotalActiveRockMassKilograms, initial.Field.TotalActiveRockMassKilograms * 1e-12);
        Assert.AreEqual(0.0, candidate.Epoch.EnergyConservationResidualJoules, candidate.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-11);
        Assert.AreEqual(0.0, candidate.Epoch.Water.PlanetaryWaterMassKilograms, 0.0);
        Assert.AreEqual(0L, initial.Epoch.Revision);
        var atlas = PlanetOriginSpatialSurfaceProjection.Create(candidate);
        Assert.IsTrue(atlas.HasDepthColumns);
        Assert.IsTrue(atlas.MeltFraction.All(f => f == 0.0f));
    }

    [TestMethod]
    public void CreateInitial_DepthRefinement_PreservesFundedMassEnthalpyAndInitialSurfaceState()
    {
        var baseline = PlanetRockColumnReference.CreateInitial(6_371_000.0);
        var baselineMass = baseline.Field.TotalActiveRockMassKilograms;
        var baselineEnergy = baseline.Field.TotalStoredEnthalpyJoules;
        var baselineSurface = baseline.Columns!.Columns[0].Layers[0].Resolve(baseline.Columns.Material);

        foreach (var layers in new[] { 8, 16, 32, 64 })
        {
            var candidate = PlanetRockColumnReference.CreateInitial(6_371_000.0, layers);
            Assert.AreEqual(layers, candidate.Columns!.Properties.LayerCount);
            Assert.AreEqual(baselineMass, candidate.Field.TotalActiveRockMassKilograms, baselineMass * 1e-12);
            Assert.AreEqual(baselineEnergy, candidate.Field.TotalStoredEnthalpyJoules, baselineEnergy * 1e-12);
            Assert.AreEqual(baseline.Epoch.Revision, candidate.Epoch.Revision);
            Assert.AreEqual(baseline.Epoch.Clock, candidate.Epoch.Clock);
            Assert.AreEqual(baselineSurface.MeltFraction,
                candidate.Columns.Columns[0].Layers[0].Resolve(candidate.Columns.Material).MeltFraction, 1e-10);
        }
    }

    [TestMethod]
    public void CreateInitial_UnsupportedDepthResolution_Rejects()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetRockColumnReference.CreateInitial(6_371_000.0, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetRockColumnReference.CreateInitial(6_371_000.0, 65));
    }

    private sealed class FlatElevation : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }
}
