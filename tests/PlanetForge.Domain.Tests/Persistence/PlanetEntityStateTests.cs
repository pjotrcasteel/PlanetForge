using PlanetForge.Domain.Persistence;
using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Domain.Tests.Persistence;

[TestClass]
public sealed class PlanetEntityStateTests
{
    [TestMethod]
    public void MoveTo_PreservesEntityIdentityAndChangesLocation()
    {
        var world = PlanetWorldIdentity.CreateCurrent(42);
        var startAddress = PlanetSurfaceAddressing.Encode(PlanetVector.UnitZ);
        var placementId = PlanetGeneratedPlacementIdentity.Create(world, startAddress, "gazelle", 3);
        var entityId = PlanetEntityId.FromGeneratedPlacement(placementId);
        var entity = new PlanetEntityState(entityId, "gazelle", startAddress);
        var destination = PlanetSurfaceAddressing.Encode(PlanetSurfaceNavigator.Move(PlanetVector.UnitZ, 2_500.0, 750.0, 6_371_000.0));

        var moved = entity.MoveTo(destination);

        Assert.AreEqual(entity.Id, moved.Id);
        Assert.AreEqual(entity.Kind, moved.Kind);
        Assert.AreNotEqual(entity.CurrentSurfaceAddress, moved.CurrentSurfaceAddress);
        Assert.AreEqual(destination, moved.CurrentSurfaceAddress);
    }

    [TestMethod]
    public void Constructor_BlankKind_Throws()
    {
        var address = PlanetSurfaceAddressing.Encode(PlanetVector.UnitZ);

        Assert.ThrowsExactly<ArgumentException>(() => new PlanetEntityState(new PlanetEntityId("entity-1"), " ", address));
    }
}