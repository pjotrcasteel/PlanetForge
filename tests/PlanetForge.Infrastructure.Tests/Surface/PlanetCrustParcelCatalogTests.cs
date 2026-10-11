using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetCrustParcelCatalogTests
{
    [TestMethod]
    [DataRow(24061984)]
    [DataRow(346147916)]
    [DataRow(579460630)]
    public void Create_CanonicalSeed_ProducesUniqueWorldScopedCrustWithConservedReferenceArea(int seed)
    {
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var world = PlanetWorldIdentity.CreateCurrent(seed);
        Assert.AreEqual(world, catalog.World);
        Assert.AreEqual(PlanetCrustParcelCatalog.ReferenceParcelCount, catalog.Parcels.Count);
        Assert.AreEqual(catalog.Parcels.Count, catalog.Parcels.Select(material => material.Id).Distinct().Count());
        Assert.AreEqual(18, new PlanetTectonicPlateKinematics().PlateCount);

        var area = 4.0 * Math.PI * Math.Pow(PlanetTectonicPlateKinematics.NominalPlanetRadiusMeters, 2);
        Assert.AreEqual(area, catalog.Parcels.Sum(material => material.ReferenceAreaSquareMeters), area * 1e-12);
        Assert.AreEqual(catalog.TotalReferenceMassKilograms,
            catalog.Parcels.Sum(material => material.ReferenceMassKilograms));
        Assert.IsTrue(catalog.Parcels.Any(material => material.Composition == PlanetCrustComposition.Continental));
        Assert.IsTrue(catalog.Parcels.Any(material => material.Composition == PlanetCrustComposition.Oceanic));

        foreach (var parcel in catalog.Parcels)
        {
            Assert.AreEqual(world, parcel.World);
            Assert.AreEqual(world, parcel.InitialPlate.World);
            Assert.IsTrue(parcel.InitialPlate.Index is >= 0 and < 18);
            Assert.AreEqual(1.0, parcel.PresentDirection.Length, 1e-12);
            Assert.IsTrue(double.IsFinite(parcel.ReferenceMassKilograms) && parcel.ReferenceMassKilograms > 0.0);
            Assert.AreEqual(parcel.ReferenceAreaSquareMeters * parcel.ReferenceThicknessMeters *
                parcel.ReferenceDensityKilogramsPerCubicMeter, parcel.ReferenceMassKilograms);
        }
    }

    [TestMethod]
    public void Create_SameSeedAndGenerator_RebuildsSameMaterialAfterRestart()
    {
        var first = PlanetCrustParcelCatalog.Create(24061984);
        var restored = PlanetCrustParcelCatalog.Create(24061984);
        var different = PlanetCrustParcelCatalog.Create(24061985);
        Assert.AreEqual(first.TotalReferenceMassKilograms, restored.TotalReferenceMassKilograms);

        for (var index = 0; index < first.Parcels.Count; index++)
        {
            var a = first.Parcels[index];
            var b = restored.Parcels[index];
            Assert.AreEqual(a.Id, b.Id);
            Assert.AreEqual(a.World, b.World);
            Assert.AreEqual(a.InitialPlate, b.InitialPlate);
            Assert.AreEqual(a.PresentDirection, b.PresentDirection);
            Assert.AreEqual(a.Composition, b.Composition);
            Assert.AreEqual(a.ReferenceAreaSquareMeters, b.ReferenceAreaSquareMeters);
            Assert.AreEqual(a.ReferenceThicknessMeters, b.ReferenceThicknessMeters);
            Assert.AreEqual(a.ReferenceDensityKilogramsPerCubicMeter, b.ReferenceDensityKilogramsPerCubicMeter);
            Assert.AreEqual(a.ReferenceMassKilograms, b.ReferenceMassKilograms);
        }

        Assert.AreNotEqual(first.Parcels[0].World, different.Parcels[0].World);
        Assert.AreNotEqual(first.Parcels[0].PresentDirection, different.Parcels[0].PresentDirection);
    }

    [TestMethod]
    public void PositionAt_GeologicalEpoch_RetainsMaterialIdentityAndInvertsPlateRotation()
    {
        var catalog = PlanetCrustParcelCatalog.Create(42);
        var plates = new PlanetTectonicPlateKinematics();
        foreach (var years in new long[] { 0, 1_000_000, 35_000_000, 180_000_000 })
        {
            var epoch = new PlanetGeologicalEpoch(years);
            foreach (var parcel in catalog.Parcels.Where((_, i) => i % 11 == 0))
            {
                var position = catalog.PositionAt(parcel.Id, epoch);
                var plate = plates.GetPlate(42, parcel.InitialPlate.Index);
                var reconstructed = plate.MapToPresent(position.GeographicDirection, epoch);
                Assert.AreSame(parcel, position.Material);
                Assert.AreEqual(epoch, position.Epoch);
                Assert.AreEqual(1.0, position.GeographicDirection.Length, 1e-12);
                Assert.AreEqual(parcel.PresentDirection.X, reconstructed.X, 1e-12);
                Assert.AreEqual(parcel.PresentDirection.Y, reconstructed.Y, 1e-12);
                Assert.AreEqual(parcel.PresentDirection.Z, reconstructed.Z, 1e-12);
                if (years == 0)
                {
                    Assert.AreEqual(parcel.PresentDirection, position.GeographicDirection);
                }
            }
        }
    }

    [TestMethod]
    public void FindNear_FixedGeographyAcrossEpochs_ReturnsMovingMaterialWithoutReassigningItsId()
    {
        var catalog = PlanetCrustParcelCatalog.Create(24061984);
        var parcel = catalog.Parcels[400];
        var past = new PlanetGeologicalEpoch(120_000_000);
        var historicLocation = catalog.PositionAt(parcel.Id, past).GeographicDirection;
        Assert.IsGreaterThan(0.02, (historicLocation - parcel.PresentDirection).Length);

        var pastAtNewLocation = catalog.FindNear(historicLocation, past, 1e-6);
        var presentAtOldLocation = catalog.FindNear(parcel.PresentDirection, PlanetGeologicalEpoch.Present, 1e-6);
        Assert.IsTrue(pastAtNewLocation.Any(position => position.Material.Id == parcel.Id));
        Assert.IsTrue(presentAtOldLocation.Any(position => position.Material.Id == parcel.Id));
        Assert.IsFalse(catalog.FindNear(parcel.PresentDirection, past, 1e-6).Any(position => position.Material.Id == parcel.Id));
    }

    [TestMethod]
    public void PositionAt_AllEpochs_ConservesReferenceMaterialMassAndLeavesCanonicalTerrainUnchanged()
    {
        const int seed = 24061984;
        var surface = new ProceduralPlanetElevationSource();
        var point = PlanetVector.Normalize(new PlanetVector(0.23, 0.65, -0.72));
        var oldHeight = surface.SampleElevationMeters(point, seed);
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        foreach (var epoch in new[] { PlanetGeologicalEpoch.Present, new PlanetGeologicalEpoch(60_000_000) })
        {
            var sum = 0.0;
            foreach (var material in catalog.Parcels)
            {
                var location = catalog.PositionAt(material.Id, epoch);
                Assert.AreEqual(material.Id, location.Material.Id);
                Assert.AreEqual(material.Composition, location.Material.Composition);
                sum += location.Material.ReferenceMassKilograms;
            }

            Assert.AreEqual(catalog.TotalReferenceMassKilograms, sum);
        }

        Assert.AreEqual(oldHeight, surface.SampleElevationMeters(point, seed));
    }

    [TestMethod]
    public void Material_InvalidWorldOwnershipAndNonPhysicalMass_AreRejected()
    {
        var world = PlanetWorldIdentity.CreateCurrent(7);
        var plate = new PlanetTectonicPlateId(world, 3);
        var mismatched = new PlanetTectonicPlateId(PlanetWorldIdentity.CreateCurrent(8), 3);
        PlanetCrustMaterialParcel Create(PlanetTectonicPlateId owner, double area = 100) =>
            new(world, new PlanetCrustParcelId(1), owner, PlanetVector.UnitY,
                PlanetCrustComposition.Oceanic, area, 8_000, 3_000);

        Assert.ThrowsExactly<ArgumentException>(() => Create(mismatched));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Create(plate, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Create(plate, double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Create(plate, double.MaxValue));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlanetCrustMaterialParcel(world, default, plate, PlanetVector.UnitY,
                PlanetCrustComposition.Oceanic, 100, 8_000, 3_000));
    }

    [TestMethod]
    public void PositionAt_InvalidParcelAndObservationRadius_RejectsUnknownMaterial()
    {
        var catalog = PlanetCrustParcelCatalog.Create(42);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => catalog.PositionAt(default, PlanetGeologicalEpoch.Present));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => catalog.PositionAt(
            new PlanetCrustParcelId((ulong)PlanetCrustParcelCatalog.ReferenceParcelCount + 1), PlanetGeologicalEpoch.Present));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => catalog.FindNear(PlanetVector.UnitX, PlanetGeologicalEpoch.Present, double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => catalog.FindNear(PlanetVector.UnitX, PlanetGeologicalEpoch.Present, Math.PI + 0.1));
    }
}
