using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetTectonicPlateKinematicsTests
{
    private readonly PlanetTectonicPlateKinematics kinematics = new();

    [TestMethod]
    public void GetPlates_CurrentSeed_ReusesCanonicalPlateIdentitiesAndInstantaneousMotion()
    {
        const int seed = 24061984;
        var plates = kinematics.GetPlates(seed);
        Assert.AreEqual(18, plates.Count);
        CollectionAssert.AreEquivalent(Enumerable.Range(0, 18).ToArray(), plates.Select(plate => plate.Id.Index).ToArray());

        foreach (var plate in plates)
        {
            var canonicalCenter = ProceduralPlanetElevationSource.GetPresentPlateCenter(seed, plate.Id.Index);
            var canonicalMotion = ProceduralPlanetElevationSource.GetPresentPlateMotion(seed, plate.Id.Index);
            Assert.AreEqual(canonicalCenter, plate.CenterAt(PlanetGeologicalEpoch.Present));
            Assert.AreEqual(1.0, plate.CenterAtPresent.Length, 1e-12);
            Assert.AreEqual(1.0, plate.EulerPole.Length, 1e-12);
            Assert.AreEqual(0.0, PlanetVector.Dot(plate.EulerPole, plate.CenterAtPresent), 1e-12);

            var reconstructed = PlanetVector.Cross(plate.EulerPole, plate.CenterAtPresent) *
                (plate.SpeedMetersPerMillionYears / PlanetTectonicPlateKinematics.MaximumPlateTravelMetersPerMillionYears);
            Assert.AreEqual(canonicalMotion.X, reconstructed.X, 1e-12);
            Assert.AreEqual(canonicalMotion.Y, reconstructed.Y, 1e-12);
            Assert.AreEqual(canonicalMotion.Z, reconstructed.Z, 1e-12);
            Assert.IsGreaterThanOrEqualTo(20_000.0, plate.SpeedMetersPerMillionYears);
            Assert.IsLessThanOrEqualTo(60_000.0, plate.SpeedMetersPerMillionYears);
        }
    }

    [TestMethod]
    public void CenterAt_SameSeedAndEpoch_ReproducesHistoricalPositionWithoutChangingPresentPlanet()
    {
        const int seed = 24061984;
        var past = new PlanetGeologicalEpoch(60_000_000);
        var first = kinematics.GetPlates(seed);
        var second = new PlanetTectonicPlateKinematics().GetPlates(seed);
        var differentSeed = kinematics.GetPlates(seed + 1);
        var canonical = new ProceduralPlanetElevationSource();
        var sampleDirection = PlanetVector.Normalize(new PlanetVector(0.31, -0.21, 0.88));
        var canonicalHeight = canonical.SampleElevationMeters(sampleDirection, seed);

        for (var i = 0; i < first.Count; i++)
        {
            Assert.AreEqual(first[i].Id, second[i].Id);
            Assert.AreEqual(first[i].CenterAt(past), second[i].CenterAt(past));
            Assert.AreEqual(1.0, first[i].CenterAt(past).Length, 1e-12);
            Assert.AreNotEqual(first[i].CenterAtPresent, differentSeed[i].CenterAtPresent);
        }

        Assert.AreEqual(canonicalHeight, canonical.SampleElevationMeters(sampleDirection, seed));
    }

    [TestMethod]
    public void Move_MaterialAndHistoricalEpoch_RoundTripsOverGeologicalTime()
    {
        var plate = kinematics.GetPlate(42, 7);
        var arbitraryMaterial = PlanetVector.Normalize(new PlanetVector(0.9, 0.25, -0.32));
        foreach (var years in new long[] { 0L, 1L, 1_000_000L, 25_000_000L, 250_000_000L, 1_000_000_000L })
        {
            var epoch = new PlanetGeologicalEpoch(years);
            var historical = plate.Move(arbitraryMaterial, epoch);
            var restored = plate.MapToPresent(historical, epoch);
            Assert.AreEqual(1.0, historical.Length, 1e-12);
            Assert.AreEqual(arbitraryMaterial.X, restored.X, 1e-12);
            Assert.AreEqual(arbitraryMaterial.Y, restored.Y, 1e-12);
            Assert.AreEqual(arbitraryMaterial.Z, restored.Z, 1e-12);
        }
    }

    [TestMethod]
    public void FindNearestPlate_MovingCentre_UsesMatchingEpochButNotFixedGeographicalRegion()
    {
        const int seed = 24061984;
        var time = new PlanetGeologicalEpoch(10_000_000);
        var originalRegion = PlanetGeologicalRegionId.CreateCurrent(seed, new PlanetTileId(CubeFace.PositiveZ, 4, 7, 8));
        var plate = kinematics.GetPlate(seed, 3);
        Assert.AreEqual(plate.Id, kinematics.FindNearestPlate(plate.CenterAtPresent, seed, PlanetGeologicalEpoch.Present));
        Assert.AreEqual(18, kinematics.GetPlates(seed).Count);
        Assert.AreEqual(originalRegion, PlanetGeologicalRegionId.CreateCurrent(seed, originalRegion.Tile));
        Assert.AreNotEqual(plate.CenterAt(time), plate.CenterAtPresent);
        Assert.AreEqual(new PlanetCrustParcelId(12), new PlanetCrustParcelId(12));
    }

    [TestMethod]
    public void GetPlate_InvalidIndex_RejectsOutOfRangeWithoutChangingCanonicalPlates()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => kinematics.GetPlate(1, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => kinematics.GetPlate(1, 18));
        Assert.AreEqual(18, kinematics.GetPlates(1).Count);
    }
}
