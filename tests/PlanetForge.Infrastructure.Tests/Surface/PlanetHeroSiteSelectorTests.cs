using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetHeroSiteSelectorTests
{
    [TestMethod]
    public void Select_RuggedReal128KmBedrock_BeatsSmoothSteepEscarpment()
    {
        var source = new DistinctMountainAndSlopeSource();
        var candidates = new[]
        {
            new PlanetHeroSiteSelector.Candidate(PlanetVector.UnitX, 0.9),
            new PlanetHeroSiteSelector.Candidate(PlanetVector.UnitZ, 0.8)
        };

        var result = PlanetHeroSiteSelector.Select(source, 24061984, candidates);
        Assert.AreEqual(PlanetVector.UnitZ, result.Center);
        Assert.IsGreaterThan(100.0, result.ReliefMeters);
        Assert.IsGreaterThan(20.0, result.NonPlanarReliefMeters);

        var replay = PlanetHeroSiteSelector.Select(source, 24061984, candidates);
        Assert.AreEqual(result, replay, "Geographical focus must never depend on camera state or random draws.");
    }

    [TestMethod]
    public void Select_FlatCandidates_RemainFiniteAndDoNotInventMountains()
    {
        var result = PlanetHeroSiteSelector.Select(new FlatBedrockSource(), 42,
            [new PlanetHeroSiteSelector.Candidate(PlanetVector.UnitZ, 0.9)]);
        Assert.AreEqual(0.0, result.ReliefMeters);
        Assert.AreEqual(0.0, result.NonPlanarReliefMeters);
    }

    [TestMethod]
    public void Select_EmptyCandidatesOrCancelledSampling_FailsWithoutPretendingToSelectRegion()
    {
        var source = new FlatBedrockSource();
        Assert.ThrowsExactly<ArgumentException>(() => PlanetHeroSiteSelector.Select(source, 42,
            Array.Empty<PlanetHeroSiteSelector.Candidate>()));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => PlanetHeroSiteSelector.Select(source, 42,
            [new PlanetHeroSiteSelector.Candidate(PlanetVector.UnitZ, 0.8)],
            cancellationToken: cancelled.Token));
    }

    private sealed class DistinctMountainAndSlopeSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) =>
            direction.Z > 0.9
                ? 2_000.0 + 190.0 * Math.Sin(direction.X * 270.0) * Math.Cos(direction.Y * 220.0)
                : 2_000.0 + direction.Y * 1_500.0;
    }

    private sealed class FlatBedrockSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 2_000.0;
    }
}
