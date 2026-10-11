using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginClockTests
{
    [TestMethod]
    public void EarthAccretionReference_UsesAbsoluteGeologicalChronologyInsteadOfGameplayYears()
    {
        var clock = PlanetOriginClock.EarthAccretionReference;

        Assert.AreEqual(27_000_000L, clock.YearsSinceSolarSystemFormation);
        Assert.AreEqual(4_540_000_000L, clock.YearsBeforePresent);
        Assert.AreEqual(clock.YearsBeforePresent, clock.GeologicalEpoch.YearsBeforePresent);
    }

    [TestMethod]
    public void AdvanceYears_AcrossMillionsOfYears_PreservesAbsoluteChronology()
    {
        var before = PlanetOriginClock.EarthAccretionReference;
        var after = before.AdvanceYears(150_000_000);

        Assert.AreEqual(177_000_000L, after.YearsSinceSolarSystemFormation);
        Assert.AreEqual(4_390_000_000L, after.YearsBeforePresent);
        Assert.AreEqual(4_540_000_000L, before.YearsBeforePresent);
    }

    [TestMethod]
    public void Present_MapsToZeroYearsBeforePresent()
    {
        var present = new PlanetOriginClock(PlanetOriginClock.SolarSystemReferenceAgeYears);

        Assert.AreEqual(0L, present.YearsBeforePresent);
        Assert.AreEqual(0L, present.GeologicalEpoch.YearsBeforePresent);
        Assert.AreEqual(PlanetOriginClock.SolarSystemReferenceAgeYears,
            PlanetOriginClock.SolarSystemFormation.AdvanceYears(PlanetOriginClock.SolarSystemReferenceAgeYears).YearsSinceSolarSystemFormation);
    }

    [TestMethod]
    public void AdvanceYears_BackwardsOrPastPresent_IsRejected()
    {
        var earth = PlanetOriginClock.EarthAccretionReference;

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => earth.AdvanceYears(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => earth.AdvanceYears(PlanetOriginClock.SolarSystemReferenceAgeYears));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetOriginClock(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetOriginClock(PlanetOriginClock.SolarSystemReferenceAgeYears + 1));
    }
}
