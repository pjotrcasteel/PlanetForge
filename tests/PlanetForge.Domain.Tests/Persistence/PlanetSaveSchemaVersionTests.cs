using PlanetForge.Domain.Persistence;

namespace PlanetForge.Domain.Tests.Persistence;

[TestClass]
public sealed class PlanetSaveSchemaVersionTests
{
    [TestMethod]
    public void Current_StartsAtVersionOne()
    {
        Assert.AreEqual(1, PlanetSaveSchemaVersion.Current.Value);
    }

    [TestMethod]
    public void Constructor_Zero_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetSaveSchemaVersion(0));
    }
}