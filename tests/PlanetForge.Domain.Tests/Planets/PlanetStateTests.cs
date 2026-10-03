using PlanetForge.Domain.Planets;

namespace PlanetForge.Domain.Tests.Planets;

[TestClass]
public sealed class PlanetStateTests
{
    [TestMethod]
    public void ChangeSeaLevel_ClampsToSupportedRange()
    {
        var state = new PlanetState();

        state.ChangeSeaLevel(100);

        Assert.AreEqual(0.035, state.SeaLevel);
    }

    [TestMethod]
    public void ChangeAtmosphereDensity_ClampsToSupportedRange()
    {
        var state = new PlanetState();

        state.ChangeAtmosphereDensity(-100);

        Assert.AreEqual(0.0, state.AtmosphereDensity);
    }
}
