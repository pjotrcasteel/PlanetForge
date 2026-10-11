using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetTectonicBoundaryKinematicsTests
{
    private readonly PlanetTectonicBoundaryKinematics boundaries = new();

    [TestMethod]
    public void Evaluate_OpposingTangentVelocities_ClassifiesConvergenceAndDivergence()
    {
        var converging = Evaluate(PlanetVector.UnitY * -1, PlanetVector.UnitY);
        var diverging = Evaluate(PlanetVector.UnitY, PlanetVector.UnitY * -1);

        Assert.AreEqual(PlanetPlateBoundaryKind.Convergent, converging.Kind);
        Assert.AreEqual(PlanetPlateBoundaryKind.Divergent, diverging.Kind);
        Assert.IsGreaterThan(100_000.0, converging.CompressionMetersPerMillionYears);
        Assert.IsLessThan(-100_000.0, diverging.CompressionMetersPerMillionYears);
        Assert.AreEqual(0.0, converging.ShearMetersPerMillionYears, 1e-8);
        Assert.AreEqual(1.0, converging.ContactInfluence, 1e-12);
        Assert.AreEqual(0.0, converging.AngularDistanceRadians, 1e-12);
    }

    [TestMethod]
    public void Evaluate_LateralRelativeMotion_ProducesTransformAndObliqueContacts()
    {
        var transform = Evaluate(PlanetVector.UnitX, PlanetVector.UnitX * -1);
        var oblique = Evaluate(
            PlanetVector.Normalize(PlanetVector.UnitX - PlanetVector.UnitY),
            PlanetVector.Normalize(PlanetVector.UnitY - PlanetVector.UnitX));

        Assert.AreEqual(PlanetPlateBoundaryKind.Transform, transform.Kind);
        Assert.AreEqual(0.0, transform.CompressionMetersPerMillionYears, 1e-8);
        Assert.IsGreaterThan(100_000.0, Math.Abs(transform.ShearMetersPerMillionYears));
        Assert.AreEqual(PlanetPlateBoundaryKind.ObliqueConvergent, oblique.Kind);
        Assert.IsGreaterThan(0.0, oblique.CompressionMetersPerMillionYears);
        Assert.IsGreaterThan(0.0, Math.Abs(oblique.ShearMetersPerMillionYears));
    }

    [TestMethod]
    public void Evaluate_SwappedPlateArguments_UsesCanonicalPairOrderAndIdenticalSignedMotion()
    {
        var first = MakePlate(3, 1, PlanetVector.UnitY * -1);
        var second = MakePlate(3, 2, PlanetVector.UnitY);
        var left = PlanetTectonicBoundaryKinematics.Evaluate(PlanetVector.UnitZ, first, second, PlanetGeologicalEpoch.Present);
        var right = PlanetTectonicBoundaryKinematics.Evaluate(PlanetVector.UnitZ, second, first, PlanetGeologicalEpoch.Present);

        Assert.AreEqual(left, right);
        Assert.AreEqual(1, left.FirstPlate.Index);
        Assert.AreEqual(2, left.SecondPlate.Index);
    }

    [TestMethod]
    public void Evaluate_OutOfContact_ReturnsNoMotionAndNeverInventsAContact()
    {
        var first = MakePlate(3, 1, PlanetVector.UnitY * -1);
        var second = MakePlate(3, 2, PlanetVector.UnitY);
        var farAway = PlanetVector.Normalize(new PlanetVector(0.08, 0.0, 1.0));
        var outside = PlanetTectonicBoundaryKinematics.Evaluate(farAway, first, second, PlanetGeologicalEpoch.Present);
        var inside = PlanetTectonicBoundaryKinematics.Evaluate(PlanetVector.UnitZ, first, second, PlanetGeologicalEpoch.Present);

        Assert.AreEqual(PlanetPlateBoundaryKind.OutsideContact, outside.Kind);
        Assert.IsFalse(outside.IsAtContact);
        Assert.AreEqual(0.0, outside.CompressionMetersPerMillionYears);
        Assert.IsTrue(inside.IsAtContact);
    }

    [TestMethod]
    public void Evaluate_IdenticalMotionAndCoincidentCentres_AreHandledWithoutFabricatedStress()
    {
        var first = MakePlate(5, 1, PlanetVector.UnitY);
        var second = MakePlate(5, 2, PlanetVector.UnitY);
        var neutral = PlanetTectonicBoundaryKinematics.Evaluate(PlanetVector.UnitZ, first, second, PlanetGeologicalEpoch.Present);
        var coincident = second with { CenterAtPresent = first.CenterAtPresent };
        var degenerate = PlanetTectonicBoundaryKinematics.Evaluate(PlanetVector.UnitZ, first, coincident, PlanetGeologicalEpoch.Present);

        Assert.AreEqual(PlanetPlateBoundaryKind.Neutral, neutral.Kind);
        Assert.AreEqual(0.0, neutral.RelativeSpeedMetersPerMillionYears, 1e-10);
        Assert.AreEqual(PlanetPlateBoundaryKind.OutsideContact, degenerate.Kind);
        Assert.IsFalse(degenerate.IsAtContact);
    }

    [TestMethod]
    public void Evaluate_DifferentGeneratedWorldOrSamePlate_RejectsInvalidOwnership()
    {
        var first = MakePlate(5, 1, PlanetVector.UnitY);
        var otherWorld = MakePlate(6, 2, PlanetVector.UnitY);
        Assert.ThrowsExactly<ArgumentException>(() => PlanetTectonicBoundaryKinematics.Evaluate(
            PlanetVector.UnitZ, first, otherWorld, PlanetGeologicalEpoch.Present));
        Assert.ThrowsExactly<ArgumentException>(() => PlanetTectonicBoundaryKinematics.Evaluate(
            PlanetVector.UnitZ, first, first, PlanetGeologicalEpoch.Present));
    }

    [TestMethod]
    public void Sample_SeedAndEpoch_AreDeterministicAndDoNotChangeExistingSurface()
    {
        const int seed = 24061984;
        var existingTerrain = new ProceduralPlanetElevationSource();
        var samplePoint = PlanetVector.Normalize(new PlanetVector(0.31, -0.21, 0.88));
        var previousHeight = existingTerrain.SampleElevationMeters(samplePoint, seed);

        foreach (var epoch in new[] { PlanetGeologicalEpoch.Present, new PlanetGeologicalEpoch(35_000_000) })
        {
            foreach (var direction in SampleDirections(128))
            {
                var current = boundaries.Sample(direction, seed, epoch);
                var replay = new PlanetTectonicBoundaryKinematics().Sample(direction, seed, epoch);
                Assert.AreEqual(current, replay);
                Assert.IsTrue(current.FirstPlate.Index < current.SecondPlate.Index);
                Assert.IsTrue(double.IsFinite(current.AngularDistanceRadians));
                Assert.IsTrue(double.IsFinite(current.ContactInfluence));
                Assert.IsTrue(double.IsFinite(current.CompressionMetersPerMillionYears));
                Assert.IsTrue(double.IsFinite(current.ShearMetersPerMillionYears));
                Assert.IsTrue(current.ContactInfluence >= 0.0 && current.ContactInfluence <= 1.0);
            }
        }

        Assert.AreEqual(previousHeight, existingTerrain.SampleElevationMeters(samplePoint, seed));
    }

    private static PlanetPlateBoundarySample Evaluate(PlanetVector firstPole, PlanetVector secondPole) =>
        PlanetTectonicBoundaryKinematics.Evaluate(PlanetVector.UnitZ,
            MakePlate(3, 1, firstPole), MakePlate(3, 2, secondPole), PlanetGeologicalEpoch.Present);

    private static PlanetTectonicPlateState MakePlate(int seed, int index, PlanetVector pole)
    {
        const double angle = 0.2;
        var center = new PlanetVector((index == 1 ? 1.0 : -1.0) * Math.Sin(angle), 0.0, Math.Cos(angle));
        return new PlanetTectonicPlateState(new PlanetTectonicPlateId(PlanetWorldIdentity.CreateCurrent(seed), index),
            center, PlanetVector.Normalize(pole),
            60_000.0 / PlanetTectonicPlateKinematics.NominalPlanetRadiusMeters, 60_000.0);
    }

    private static IEnumerable<PlanetVector> SampleDirections(int count)
    {
        var goldenAngle = Math.PI * (3.0 - Math.Sqrt(5.0));
        for (var i = 0; i < count; i++)
        {
            var y = 1.0 - 2.0 * ((i + 0.5) / count);
            var radius = Math.Sqrt(1.0 - y * y);
            yield return new PlanetVector(radius * Math.Cos(goldenAngle * i), y, radius * Math.Sin(goldenAngle * i));
        }
    }
}
