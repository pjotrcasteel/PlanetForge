using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetRegionalGeologyOverlayTests
{
    private const double PlanetRadiusMeters = 6_371_000.0;
    private const double CellSpacingMeters = 2_000.0;
    private const int Size = 40;

    [TestMethod]
    public void SampleDeltaMeters_InteriorCell_UsesActualEvolvedElevation()
    {
        var original = CreateOriginal();
        var evolved = PlanetRegionalGeologyEvolution.Advance(original, 6);
        var anchor = PlanetVector.Normalize(new PlanetVector(0.35, 0.6, 0.72));
        var region = PlanetRegionalGeologyOverlay.Create(original, evolved, anchor, PlanetRadiusMeters);
        var candidate = FindInteriorChange(original, evolved);
        var direction = DirectionAt(anchor, candidate.X, candidate.Y);

        Assert.IsGreaterThan(0.1, Math.Abs(candidate.Change));
        Assert.AreEqual(candidate.Change, region.SampleDeltaMeters(direction, original.Seed), 0.002);
        Assert.AreEqual(0.0, region.SampleDeltaMeters(direction, original.Seed + 1));
    }

    [TestMethod]
    public void SampleDeltaMeters_SameWorldDirection_AgreesAcrossOrbitalAndLocalSampleResolutions()
    {
        var original = CreateOriginal();
        var evolved = PlanetRegionalGeologyEvolution.Advance(original, 5);
        var anchor = PlanetVector.Normalize(new PlanetVector(-0.67, 0.55, 0.50));
        var region = PlanetRegionalGeologyOverlay.Create(original, evolved, anchor, PlanetRadiusMeters);
        var source = new PlanetRegionalEvolvedElevationSource(new FlatElevationSource(), region);
        var cell = FindInteriorChange(original, evolved);
        var direction = DirectionAt(anchor, cell.X, cell.Y);

        var orbital = source.SampleElevationMeters(direction, original.Seed);
        var local = source.SampleElevationMeters(direction * 7.0, original.Seed);
        Assert.AreEqual(orbital, local, 0.00001);
        Assert.AreEqual(750.0, source.SampleElevationMeters(direction, original.Seed - 1), 0.00001);
        Assert.AreEqual(750.0 + region.SampleDeltaMeters(direction, original.Seed), orbital, 0.00001);
    }

    [TestMethod]
    public void SampleDeltaMeters_OutsideRegionAndAtBoundary_HasNoVisibleDiscontinuity()
    {
        var original = CreateOriginal();
        var evolved = PlanetRegionalGeologyEvolution.Advance(original, 5);
        var anchor = PlanetVector.UnitZ;
        var region = PlanetRegionalGeologyOverlay.Create(original, evolved, anchor, PlanetRadiusMeters);

        Assert.AreEqual(0.0, region.SampleDeltaMeters(PlanetVector.UnitX, original.Seed));
        Assert.AreEqual(0.0, region.SampleDeltaMeters(PlanetVector.UnitZ * -1.0, original.Seed));
        Assert.AreEqual(0.0, region.SampleDeltaMeters(DirectionAt(anchor, -0.2, 20.0), original.Seed));
        Assert.AreEqual(0.0, region.SampleDeltaMeters(DirectionAt(anchor, 39.2, 20.0), original.Seed));
        Assert.AreEqual(0.0, region.SampleDeltaMeters(DirectionAt(anchor, 0.0, 20.0), original.Seed));
        Assert.IsLessThan(0.002, Math.Abs(region.SampleDeltaMeters(DirectionAt(anchor, 0.001, 20.0), original.Seed)));
    }

    [TestMethod]
    public void SampleDeltaMeters_PolarCapCoordinates_DoNotIntroduceLongitudeSeams()
    {
        var original = CreateOriginal();
        var evolved = PlanetRegionalGeologyEvolution.Advance(original, 5);
        var region = PlanetRegionalGeologyOverlay.Create(original, evolved, PlanetVector.UnitY, PlanetRadiusMeters);
        var left = DirectionAt(PlanetVector.UnitY, 19.40, 18.20);
        var right = DirectionAt(PlanetVector.UnitY, 19.40001, 18.20);

        Assert.IsTrue(double.IsFinite(region.SampleDeltaMeters(left, original.Seed)));
        Assert.IsLessThan(0.01, Math.Abs(region.SampleDeltaMeters(left, original.Seed) -
            region.SampleDeltaMeters(right, original.Seed)));
    }

    [TestMethod]
    public void Create_MutatingSnapshotAfterCreation_DoesNotChangeSampledWorld()
    {
        var original = CreateOriginal();
        var evolved = PlanetRegionalGeologyEvolution.Advance(original, 6);
        var anchor = PlanetVector.UnitY;
        var cell = FindInteriorChange(original, evolved);
        var direction = DirectionAt(anchor, cell.X, cell.Y);
        var region = PlanetRegionalGeologyOverlay.Create(original, evolved, anchor, PlanetRadiusMeters);
        var expected = region.SampleDeltaMeters(direction, original.Seed);

        evolved.ElevationMeters[cell.Y * Size + cell.X] += 3000;
        original.ElevationMeters[cell.Y * Size + cell.X] -= 3000;

        Assert.AreEqual(expected, region.SampleDeltaMeters(direction, original.Seed), 0.00001);
    }

    [TestMethod]
    public void Create_MismatchedRegionOrSeed_RejectsInvalidEvolution()
    {
        var original = CreateOriginal();
        var evolved = PlanetRegionalGeologyEvolution.Advance(original, 2);

        Assert.ThrowsExactly<ArgumentException>(() => PlanetRegionalGeologyOverlay.Create(
            original, evolved with { RegionKey = "different" }, PlanetVector.UnitZ, PlanetRadiusMeters));
        Assert.ThrowsExactly<ArgumentException>(() => PlanetRegionalGeologyOverlay.Create(
            original, evolved with { Seed = original.Seed + 1 }, PlanetVector.UnitZ, PlanetRadiusMeters));
        Assert.ThrowsExactly<ArgumentException>(() => PlanetRegionalGeologyOverlay.Create(
            evolved, original, PlanetVector.UnitZ, PlanetRadiusMeters));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetRegionalGeologyOverlay.Create(
            original, evolved, PlanetVector.UnitZ, -1.0));
    }

    private static PlanetRegionalGeologySnapshot CreateOriginal()
    {
        var values = new float[Size * Size];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var uplift = 3400 * Math.Exp(-Math.Pow((x - Size * 0.62) / (Size * 0.18), 2.0));
                values[y * Size + x] = (float)(uplift + 320 * Math.Sin(y * 0.35 + x * 0.24) - 17 * y - 150);
            }
        }

        return PlanetRegionalGeologyEvolution.Initialize(24061984, "faceZ/region12", Size, Size, CellSpacingMeters, values);
    }

    private static (int X, int Y, double Change) FindInteriorChange(
        PlanetRegionalGeologySnapshot original, PlanetRegionalGeologySnapshot evolved)
    {
        return (from y in Enumerable.Range(5, Size - 10)
                from x in Enumerable.Range(5, Size - 10)
                let delta = evolved.ElevationMeters[y * Size + x] - original.ElevationMeters[y * Size + x]
                orderby Math.Abs(delta) descending
                select (X: x, Y: y, Change: (double)delta)).First();
    }

    private static PlanetVector DirectionAt(PlanetVector anchor, double x, double y)
    {
        var frame = PlanetLocalFrame.Create(anchor, PlanetRadiusMeters, 0);
        var east = (x - (Size - 1) * 0.5) * CellSpacingMeters;
        var north = (y - (Size - 1) * 0.5) * CellSpacingMeters;
        return PlanetVector.Normalize((anchor * PlanetRadiusMeters) + (frame.East * east) + (frame.North * north));
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 750.0;
    }
}
