using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginDurationTests
{
    [TestMethod]
    public void Advance_SubsecondStepsAcrossJulianYearBoundary_PreservesExactGeologicalTime()
    {
        var initial = new PlanetOriginClock(27_000_000, PlanetOriginDuration.MicrosecondsPerJulianYear - 750_000);

        var quarterSecond = initial.Advance(PlanetOriginDuration.FromSeconds(0.25));
        var nextYear = quarterSecond.Advance(PlanetOriginDuration.FromSeconds(0.5));

        Assert.AreEqual(27_000_000L, quarterSecond.YearsSinceSolarSystemFormation);
        Assert.AreEqual(PlanetOriginDuration.MicrosecondsPerJulianYear - 500_000, quarterSecond.MicrosecondsIntoYear);
        Assert.AreEqual(27_000_001L, nextYear.YearsSinceSolarSystemFormation);
        Assert.AreEqual(0L, nextYear.MicrosecondsIntoYear);
        Assert.AreEqual(27_000_000L, initial.YearsSinceSolarSystemFormation);
    }

    [TestMethod]
    public void Advance_TwoHalfDays_MatchOneDayExactlyAtBillionsOfYears()
    {
        var clock = PlanetOriginClock.EarthAccretionReference;
        var halves = clock.Advance(PlanetOriginDuration.FromSeconds(43_200))
            .Advance(PlanetOriginDuration.FromSeconds(43_200));
        var whole = clock.Advance(PlanetOriginDuration.FromSeconds(86_400));

        Assert.AreEqual(whole, halves);
        Assert.AreEqual(86_400_000_000L, whole.MicrosecondsIntoYear);
        Assert.AreEqual(4_540_000_000L, whole.YearsBeforePresent);
        Assert.AreEqual(whole.GeologicalEpoch, halves.GeologicalEpoch);
    }

    [TestMethod]
    public void AdvanceYears_AfterPartialYear_PreservesFractionalTimestamp()
    {
        var initial = PlanetOriginClock.EarthAccretionReference.Advance(PlanetOriginDuration.FromSeconds(3_600));
        var next = initial.AdvanceYears(1_000_000);

        Assert.AreEqual(28_000_000L, next.YearsSinceSolarSystemFormation);
        Assert.AreEqual(3_600_000_000L, next.MicrosecondsIntoYear);
        Assert.AreEqual(4_539_000_000L, next.YearsBeforePresent);
        Assert.AreEqual(3_600_000_000L, initial.MicrosecondsIntoYear);
    }

    [TestMethod]
    public void FromSeconds_ExactlyOneJulianYear_NormalizesToWholeYear()
    {
        var duration = PlanetOriginDuration.FromSeconds(PlanetThermalEnergyBudget.JulianYearSeconds);

        Assert.AreEqual(new PlanetOriginDuration(1), duration);
        Assert.AreEqual(PlanetThermalEnergyBudget.JulianYearSeconds, duration.TotalSeconds, 0.0);
        Assert.AreEqual(new PlanetOriginDuration(0), PlanetOriginDuration.FromSeconds(0));
    }

    [TestMethod]
    public void FromSeconds_InvalidOrUnrepresentableDuration_IsRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetOriginDuration.FromSeconds(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetOriginDuration.FromSeconds(double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetOriginDuration.FromSeconds(double.PositiveInfinity));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetOriginDuration.FromSeconds(PlanetThermalEnergyBudget.JulianYearSeconds + 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetOriginDuration.FromSeconds(1e-8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetOriginDuration(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetOriginDuration(0, PlanetOriginDuration.MicrosecondsPerJulianYear));
    }

    [TestMethod]
    public void Advance_BeyondPresentEvenByOneMicrosecond_IsRejectedWithoutChangingClock()
    {
        var boundary = new PlanetOriginClock(PlanetOriginClock.SolarSystemReferenceAgeYears - 1,
            PlanetOriginDuration.MicrosecondsPerJulianYear - 1);
        var present = boundary.Advance(PlanetOriginDuration.FromSeconds(0.000001));

        Assert.AreEqual(PlanetOriginClock.SolarSystemReferenceAgeYears, present.YearsSinceSolarSystemFormation);
        Assert.AreEqual(0L, present.MicrosecondsIntoYear);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => present.Advance(PlanetOriginDuration.FromSeconds(0.000001)));
        Assert.AreEqual(PlanetOriginDuration.MicrosecondsPerJulianYear - 1, boundary.MicrosecondsIntoYear);
    }
}
