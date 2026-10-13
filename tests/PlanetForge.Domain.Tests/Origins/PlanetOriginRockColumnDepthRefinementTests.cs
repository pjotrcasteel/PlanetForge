using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginRockColumnDepthRefinementTests
{
    [TestMethod]
    public void Screen_RecordedFourHourDepthRefinement_RejectsUnresolvedFront()
    {
        // Independent 2026-10-13 eight/sixteen/thirty-two/sixty-four layer
        // measurements, at 4 h with a 5 s cap. They are evidence of a defect,
        // not golden expected physics. Record both thermal and spatial errors.
        PlanetOriginRockColumnResolutionSample[] samples =
        [
            new(8, 0.81689, 1.63378, 1043.61, 1),
            new(16, 0.40845, 0.81690, 846.49, 1),
            new(32, 0.20422, 0.40844, 786.51, 1),
            new(64, 0.10211, 0.20422, 820.28, 1)
        ];
        var screen = PlanetOriginRockColumnDepthRefinement.Screen(samples);
        Assert.IsFalse(screen.MeetsScreeningCriteria);
        Assert.IsGreaterThan(0.1, screen.FinestFrontDifferenceMeters);
        Assert.IsGreaterThan(20.0, screen.FinestSurfaceTemperatureDifferenceKelvin);
        Assert.IsGreaterThan(0.1, screen.FinestUnresolvedCellWidthMeters);
    }

    [TestMethod]
    public void Screen_RefinedMultiCellProfile_CanSatisfyNecessaryConditions()
    {
        PlanetOriginRockColumnResolutionSample[] samples =
        [
            new(8, 0.49, 0.6, 950, 2),
            new(16, 0.55, 0.60, 972, 5),
            new(32, 0.578, 0.592, 977, 10),
            new(64, 0.583, 0.591, 979, 20)
        ];
        var screen = PlanetOriginRockColumnDepthRefinement.Screen(samples);
        Assert.IsTrue(screen.MeetsScreeningCriteria);
        Assert.IsLessThan(0.02, screen.FinestFrontDifferenceMeters);
        Assert.IsLessThan(10.0, screen.FinestSurfaceTemperatureDifferenceKelvin);
    }

    [TestMethod]
    public void FrontBounds_RecordedGridSequence_EnclosesUnresolvedPhysicalFront()
    {
        PlanetOriginRockColumnResolutionSample[] samples =
        [
            new(8, 0.81689, 1.63378, 1043.61, 1),
            new(16, 0.40845, 0.81690, 846.49, 1),
            new(32, 0.20422, 0.40844, 786.51, 1),
            new(64, 0.10211, 0.20422, 820.28, 1)
        ];
        foreach (var sample in samples)
        {
            var (minimum, maximum) = sample.FrontBoundsMeters;
            Assert.AreEqual(sample.FullySolidTopMeters, minimum);
            Assert.AreEqual(sample.FirstUnresolvedCellBottomMeters, maximum);
            Assert.IsGreaterThan(minimum, maximum);
            Assert.IsTrue(sample.UnresolvedFrontMidpointMeters >= minimum);
            Assert.IsTrue(sample.UnresolvedFrontMidpointMeters <= maximum);
        }
        // A cross-resolution overlap is NOT guaranteed: this is precisely
        // why a single fully solid cell cannot be claimed as a converged crust.
        Assert.IsGreaterThan(samples[^1].FrontBoundsMeters.MaximumMeters,
            samples[0].FrontBoundsMeters.MinimumMeters);
    }

    [TestMethod]
    public void Screen_NarrowFinestCellsButDisjointCoarseFrontIntervals_Rejects()
    {
        PlanetOriginRockColumnResolutionSample[] samples =
        [
            new(8, 0.10, 0.12, 900, 3),
            new(16, 0.10, 0.12, 900, 4),
            new(32, 0.50, 0.51, 900, 8),
            new(64, 0.50, 0.51, 900, 16)
        ];

        var screen = PlanetOriginRockColumnDepthRefinement.Screen(samples);
        Assert.IsFalse(screen.MeetsScreeningCriteria,
            "A matching finest-grid pair cannot hide a contradictory coarser front interval.");
        Assert.AreEqual(0.0, screen.FinestFrontDifferenceMeters);
        Assert.AreEqual(0.0, screen.FinestSurfaceTemperatureDifferenceKelvin);
    }

    [TestMethod]
    public void Screen_OutOfOrderOrNonPhysicalSamples_Rejects()
    {
        PlanetOriginRockColumnResolutionSample[] samples =
        [
            new(8, 0.1, 0.2, 1000, 1),
            new(16, 0.1, 0.2, 1000, 1),
            new(24, 0.1, 0.2, 1000, 1),
            new(64, 0.1, 0.2, 1000, 1)
        ];
        Assert.ThrowsExactly<ArgumentException>(() => PlanetOriginRockColumnDepthRefinement.Screen(samples));
        samples[2] = new(32, 0.4, 0.3, 1000, 1);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetOriginRockColumnDepthRefinement.Screen(samples));
    }
}
