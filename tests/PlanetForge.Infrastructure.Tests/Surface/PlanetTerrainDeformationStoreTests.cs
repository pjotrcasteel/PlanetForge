using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetTerrainDeformationStoreTests
{
    [TestMethod]
    public void Apply_ChannelStamp_LowersCenterAndFadesOutsideValley()
    {
        var store = new PlanetTerrainDeformationStore();
        var center = PlanetVector.Normalize(new PlanetVector(1.0, 0.2, -0.1));
        var deformation = new PlanetTerrainDeformation(center, 0.002, 0.008, 80.0, 30.0);

        store.Apply(42, [deformation]);

        Assert.AreEqual(-110.0, store.SampleElevationDeltaMeters(center, 42), 0.000001);
        Assert.AreEqual(0.0, store.SampleElevationDeltaMeters(center * -1.0, 42), 0.000001);
        Assert.AreEqual(1, store.GetRevision(42));
    }

    [TestMethod]
    public void Apply_DeeperOverlappingStamp_PreservesMaximumIncision()
    {
        var store = new PlanetTerrainDeformationStore();
        var center = PlanetVector.UnitX;
        store.Apply(42, [new PlanetTerrainDeformation(center, 0.002, 0.008, 40.0, 10.0)]);
        store.Apply(42, [new PlanetTerrainDeformation(center, 0.002, 0.008, 90.0, 25.0)]);

        Assert.AreEqual(-115.0, store.SampleElevationDeltaMeters(center, 42), 0.000001);
        Assert.AreEqual(2, store.GetRevision(42));
    }

    [TestMethod]
    public void ApplyDeposition_DeltaStamp_RaisesCenterAndFadesOutsideApron()
    {
        var store = new PlanetTerrainDeformationStore();
        var center = PlanetVector.Normalize(new PlanetVector(0.8, -0.2, 0.4));
        var deposition = new PlanetTerrainDeposition(center, 0.002, 0.008, 70.0, 20.0);

        store.ApplyDeposition(42, [deposition]);

        Assert.AreEqual(90.0, store.SampleElevationDeltaMeters(center, 42), 0.000001);
        Assert.AreEqual(0.0, store.SampleElevationDeltaMeters(center * -1.0, 42), 0.000001);
        Assert.AreEqual(1, store.GetRevision(42));
    }

    [TestMethod]
    public void ErosionAndDeposition_Overlap_ComposeIntoCanonicalElevationDelta()
    {
        var store = new PlanetTerrainDeformationStore();
        var center = PlanetVector.UnitX;
        store.Apply(42, [new PlanetTerrainDeformation(center, 0.002, 0.008, 80.0, 30.0)]);
        store.ApplyDeposition(42, [new PlanetTerrainDeposition(center, 0.002, 0.008, 40.0, 10.0)]);

        Assert.AreEqual(-60.0, store.SampleElevationDeltaMeters(center, 42), 0.000001);
        Assert.AreEqual(2, store.GetRevision(42));
    }

    [TestMethod]
    public void ExportAndRestore_PreservesTerrainEvolutionAndRevision()
    {
        var source = new PlanetTerrainDeformationStore();
        var erosionCenter = PlanetVector.Normalize(new PlanetVector(0.7, 0.2, -0.4));
        var depositionCenter = PlanetVector.Normalize(new PlanetVector(-0.3, 0.4, 0.8));
        source.Apply(42, [new PlanetTerrainDeformation(erosionCenter, 0.002, 0.008, 80.0, 30.0)]);
        source.ApplyDeposition(42, [new PlanetTerrainDeposition(depositionCenter, 0.003, 0.010, 60.0, 20.0)]);
        var expectedErosionDelta = source.SampleElevationDeltaMeters(erosionCenter, 42);
        var expectedDepositionDelta = source.SampleElevationDeltaMeters(depositionCenter, 42);
        var state = source.Export(42);

        var restored = new PlanetTerrainDeformationStore();
        restored.Restore(42, state);

        Assert.AreEqual(source.GetRevision(42), restored.GetRevision(42));
        Assert.AreEqual(expectedErosionDelta, restored.SampleElevationDeltaMeters(erosionCenter, 42), 0.000001);
        Assert.AreEqual(expectedDepositionDelta, restored.SampleElevationDeltaMeters(depositionCenter, 42), 0.000001);
        Assert.HasCount(1, state.Erosions);
        Assert.HasCount(1, state.Depositions);
    }

    [TestMethod]
    public void Clear_RemovesSeedTerrainDeformations()
    {
        var store = new PlanetTerrainDeformationStore();
        store.Apply(42, [new PlanetTerrainDeformation(PlanetVector.UnitX, 0.002, 0.008, 80.0, 30.0)]);

        store.Clear(42);

        Assert.AreEqual(0.0, store.SampleElevationDeltaMeters(PlanetVector.UnitX, 42), 0.000001);
        Assert.AreEqual(0, store.GetRevision(42));
    }

    [TestMethod]
    public void ProceduralElevationSource_AppliedDeformation_ChangesCanonicalElevation()
    {
        var store = new PlanetTerrainDeformationStore();
        var direction = PlanetVector.Normalize(new PlanetVector(0.31, -0.47, 0.83));
        var baseline = new ProceduralPlanetElevationSource().SampleElevationMeters(direction, 42);
        store.Apply(42, [new PlanetTerrainDeformation(direction, 0.002, 0.008, 80.0, 30.0)]);
        var deformed = new ProceduralPlanetElevationSource(store).SampleElevationMeters(direction, 42);

        Assert.AreEqual(baseline - 110.0, deformed, 0.000001);
    }
}
