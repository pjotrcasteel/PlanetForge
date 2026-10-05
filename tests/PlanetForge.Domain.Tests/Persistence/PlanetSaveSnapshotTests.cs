using PlanetForge.Domain.Persistence;
using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Domain.Tests.Persistence;

[TestClass]
public sealed class PlanetSaveSnapshotTests
{
    [TestMethod]
    public void Constructor_CopiesEntityCollection()
    {
        var entities = new List<PlanetEntityState>
        {
            CreateEntity("entity-1", PlanetVector.UnitZ),
        };
        var snapshot = new PlanetSaveSnapshot(PlanetSaveHeader.CreateCurrent(42), entities);

        entities.Add(CreateEntity("entity-2", PlanetVector.UnitX));

        Assert.AreEqual(1, snapshot.Entities.Count);
        Assert.AreEqual("entity-1", snapshot.Entities[0].Id.Value);
    }

    [TestMethod]
    public void Constructor_DuplicateEntityId_Throws()
    {
        var entities = new[]
        {
            CreateEntity("entity-1", PlanetVector.UnitZ),
            CreateEntity("entity-1", PlanetVector.UnitX),
        };

        Assert.ThrowsExactly<ArgumentException>(() => new PlanetSaveSnapshot(PlanetSaveHeader.CreateCurrent(42), entities));
    }

    [TestMethod]
    public void CreateCurrentHeader_UsesCurrentSaveAndGenerationVersions()
    {
        var header = PlanetSaveHeader.CreateCurrent(42);

        Assert.AreEqual(1, header.SchemaVersion.Value);
        Assert.AreEqual(PlanetGenerationVersion.Current.Value, header.World.GenerationVersion.Value);
        Assert.AreEqual(42, header.World.Seed);
    }

    private static PlanetEntityState CreateEntity(string id, PlanetVector direction) =>
        new(new PlanetEntityId(id), "test", PlanetSurfaceAddressing.Encode(direction));
}