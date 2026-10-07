using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Domain.Tests.WorldGeneration;

[TestClass]
public sealed class PlanetGenerationVersionTests
{
    [TestMethod]
    public void Current_IsVersionNine()
    {
        Assert.AreEqual(9, PlanetGenerationVersion.Current.Value);
    }

    [TestMethod]
    public void Constructor_Zero_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetGenerationVersion(0));
    }
}