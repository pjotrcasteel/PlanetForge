using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetScenarioCatalogTests
{
    [TestMethod]
    public void IntendedDefault_EarthOrigin_IsNotFalselyMarkedAsPlayable()
    {
        var intended = PlanetScenarioCatalog.IntendedDefault;

        Assert.AreEqual(PlanetScenarioId.EarthOrigin, intended.Id);
        Assert.AreEqual(PlanetScenarioTimeBasis.YearsBeforePresent, intended.TimeBasis);
        Assert.IsFalse(intended.IsRuntimeAvailable,
            "Do not describe the artificial snowball mission as the already implemented Earth-origin simulator.");
    }

    [TestMethod]
    public void CurrentPlayable_FrozenClimateExperiment_UsesItsOwnMissionYearCounter()
    {
        var playable = PlanetScenarioCatalog.CurrentPlayable;

        Assert.AreEqual(PlanetScenarioId.FrozenClimateExperiment, playable.Id);
        Assert.AreEqual(PlanetScenarioTimeBasis.YearsSinceExperimentStart, playable.TimeBasis);
        Assert.IsTrue(playable.IsRuntimeAvailable);
        Assert.AreEqual(playable, PlanetScenarioCatalog.Get(playable.Id));
    }

    [TestMethod]
    public void Get_UnknownScenario_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetScenarioCatalog.Get((PlanetScenarioId)1234));
    }
}
