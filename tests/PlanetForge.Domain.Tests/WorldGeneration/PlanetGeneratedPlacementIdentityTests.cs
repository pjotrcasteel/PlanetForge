using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Domain.Tests.WorldGeneration;

[TestClass]
public sealed class PlanetGeneratedPlacementIdentityTests
{
    [TestMethod]
    public void Create_SameInputs_ReturnsSameIdentity()
    {
        var world = PlanetWorldIdentity.CreateCurrent(42);
        var address = PlanetSurfaceAddressing.Encode(PlanetVector.UnitZ);

        var first = PlanetGeneratedPlacementIdentity.Create(world, address, "tree", 7);
        var second = PlanetGeneratedPlacementIdentity.Create(world, address, "tree", 7);

        Assert.AreEqual(first, second);
        Assert.AreEqual(32, first.Value.Length);
    }

    [TestMethod]
    public void Create_DifferentPlacementInputs_ReturnDifferentIdentities()
    {
        var world = PlanetWorldIdentity.CreateCurrent(42);
        var address = PlanetSurfaceAddressing.Encode(PlanetVector.UnitZ);
        var movedAddress = PlanetSurfaceAddressing.Encode(PlanetSurfaceNavigator.Move(PlanetVector.UnitZ, 20.0, 0.0, 6_371_000.0));
        var baseline = PlanetGeneratedPlacementIdentity.Create(world, address, "tree", 0);

        Assert.AreNotEqual(baseline, PlanetGeneratedPlacementIdentity.Create(PlanetWorldIdentity.CreateCurrent(43), address, "tree", 0));
        Assert.AreNotEqual(baseline, PlanetGeneratedPlacementIdentity.Create(new PlanetWorldIdentity(42, new PlanetGenerationVersion(2)), address, "tree", 0));
        Assert.AreNotEqual(baseline, PlanetGeneratedPlacementIdentity.Create(world, movedAddress, "tree", 0));
        Assert.AreNotEqual(baseline, PlanetGeneratedPlacementIdentity.Create(world, address, "rock", 0));
        Assert.AreNotEqual(baseline, PlanetGeneratedPlacementIdentity.Create(world, address, "tree", 1));
    }

    [TestMethod]
    public void Create_InvalidLayer_Throws()
    {
        var world = PlanetWorldIdentity.CreateCurrent(42);
        var address = PlanetSurfaceAddressing.Encode(PlanetVector.UnitZ);

        Assert.ThrowsExactly<ArgumentException>(() => PlanetGeneratedPlacementIdentity.Create(world, address, " ", 0));
    }

    [TestMethod]
    public void Create_NegativeSlot_Throws()
    {
        var world = PlanetWorldIdentity.CreateCurrent(42);
        var address = PlanetSurfaceAddressing.Encode(PlanetVector.UnitZ);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetGeneratedPlacementIdentity.Create(world, address, "tree", -1));
    }
}