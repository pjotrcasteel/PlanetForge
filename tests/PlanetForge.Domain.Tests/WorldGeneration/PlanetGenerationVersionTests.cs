using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Domain.Tests.WorldGeneration;

[TestClass]
public sealed class PlanetGenerationVersionTests
{
    [TestMethod]
    public void Current_IsVersionEleven()
    {
        Assert.AreEqual(11, PlanetGenerationVersion.Current.Value);
    }

    [TestMethod]
    public void Constructor_Zero_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetGenerationVersion(0));
    }
}