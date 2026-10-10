using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Domain.Tests.Surface;

[TestClass]
public sealed class PlanetGeologicalIdentityTests
{
    [TestMethod]
    public void GeologicalRegion_FixedTile_RemainsSameAddressAcrossEpochs()
    {
        var region = PlanetGeologicalRegionId.CreateCurrent(24061984, new PlanetTileId(CubeFace.PositiveZ, 5, 14, 18));
        var past = new PlanetGeologicalEpoch(75_000_000);
        var present = PlanetGeologicalEpoch.Present;

        Assert.AreNotEqual(past, present);
        Assert.AreEqual(75_000_000L, past.YearsBeforePresent);
        Assert.AreEqual(region, PlanetGeologicalRegionId.CreateCurrent(24061984,
            new PlanetTileId(CubeFace.PositiveZ, 5, 14, 18)));
        Assert.AreEqual("G18:S24061984:F4:L5:X14:Y18", region.ToString());
        Assert.AreEqual(region.CenterDirection, region.CenterDirection);
        Assert.AreEqual(1.0, region.CenterDirection.Length, 1e-12);
    }

    [TestMethod]
    public void GeologicalRegion_DifferentPlanetOrTile_HasDifferentIdentity()
    {
        var location = new PlanetTileId(CubeFace.NegativeX, 8, 19, 55);
        var original = PlanetGeologicalRegionId.CreateCurrent(42, location);
        var changedSeed = PlanetGeologicalRegionId.CreateCurrent(43, location);
        var changedGenerator = new PlanetGeologicalRegionId(new PlanetWorldIdentity(42,
            new PlanetGenerationVersion(PlanetGenerationVersion.Current.Value + 1)), location);
        var changedTile = PlanetGeologicalRegionId.CreateCurrent(42, new PlanetTileId(CubeFace.NegativeX, 8, 20, 55));

        Assert.AreNotEqual(original, changedSeed);
        Assert.AreNotEqual(original, changedGenerator);
        Assert.AreNotEqual(original, changedTile);
    }

    [TestMethod]
    public void CrustParcel_IndependentOfFixedRegion_PreservesMaterialIdentity()
    {
        var parcel = new PlanetCrustParcelId(4512);
        var regionBefore = PlanetGeologicalRegionId.CreateCurrent(7, new PlanetTileId(CubeFace.PositiveZ, 5, 2, 3));
        var regionAfter = PlanetGeologicalRegionId.CreateCurrent(7, new PlanetTileId(CubeFace.PositiveZ, 5, 3, 3));

        Assert.AreEqual(new PlanetCrustParcelId(4512), parcel);
        Assert.AreNotEqual(regionBefore, regionAfter);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetCrustParcelId(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetGeologicalEpoch(-1));
    }
}
