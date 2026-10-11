using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetCrustOwnershipHistoryTests
{
    [TestMethod]
    public void GetState_WithoutTransfers_ReproducesCanonicalCrustKinematics()
    {
        var catalog = PlanetCrustParcelCatalog.Create(24061984);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        Assert.AreEqual(catalog.World, history.World);
        Assert.AreEqual(catalog.TotalReferenceMassKilograms, history.TotalReferenceMassKilograms);
        Assert.AreEqual(0, history.Transfers.Count);

        foreach (var epoch in new[] { PlanetGeologicalEpoch.Present, new PlanetGeologicalEpoch(60_000_000) })
        {
            foreach (var material in catalog.Parcels.Where((_, index) => index % 17 == 0))
            {
                var expected = catalog.PositionAt(material.Id, epoch);
                var actual = history.GetState(material.Id, epoch);
                Assert.AreSame(material, actual.Material);
                Assert.AreEqual(material.InitialPlate, actual.OwningPlate);
                Assert.AreEqual(expected.GeographicDirection.X, actual.GeographicDirection.X, 1e-12);
                Assert.AreEqual(expected.GeographicDirection.Y, actual.GeographicDirection.Y, 1e-12);
                Assert.AreEqual(expected.GeographicDirection.Z, actual.GeographicDirection.Z, 1e-12);
            }
        }
    }

    [TestMethod]
    public void WithTransfer_AtVerifiedBoundary_PreservesContinuousPositionAndOriginalMaterialIdentity()
    {
        var catalog = PlanetCrustParcelCatalog.Create(24061984);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var epoch = new PlanetGeologicalEpoch(1_000_000);
        var transfer = FindValidTransfer(catalog, epoch);
        var before = history.GetState(transfer.ParcelId, epoch);
        var changed = history.WithTransfer(transfer);
        var atBoundary = changed.GetState(transfer.ParcelId, epoch);
        var older = changed.GetState(transfer.ParcelId, new PlanetGeologicalEpoch(60_000_000));
        var untouchedPresent = changed.GetState(transfer.ParcelId, PlanetGeologicalEpoch.Present);

        Assert.AreSame(before.Material, atBoundary.Material);
        Assert.AreEqual(before.GeographicDirection, atBoundary.GeographicDirection);
        Assert.AreEqual(transfer.FromPlate, before.OwningPlate);
        Assert.AreEqual(transfer.ToPlate, atBoundary.OwningPlate);
        Assert.AreEqual(transfer.ToPlate, older.OwningPlate);
        Assert.AreEqual(transfer.FromPlate, untouchedPresent.OwningPlate);
        Assert.AreEqual(before.Material.ReferenceMassKilograms, older.Material.ReferenceMassKilograms);
        Assert.AreEqual(before.Material.Composition, older.Material.Composition);
        Assert.AreEqual(1.0, older.GeographicDirection.Length, 1e-12);

        var evidence = changed.Transfers.Single();
        Assert.AreEqual(transfer, evidence.Transfer);
        Assert.IsTrue(evidence.ContactInfluence > 0.0);
        Assert.AreNotEqual(PlanetPlateBoundaryKind.OutsideContact, evidence.BoundaryKind);
        Assert.AreNotEqual(PlanetPlateBoundaryKind.Neutral, evidence.BoundaryKind);

        // A boundary event changes a reference frame, not a geographic position.
        var oneYearLater = changed.GetState(transfer.ParcelId, new PlanetGeologicalEpoch(epoch.YearsBeforePresent + 1));
        Assert.IsLessThan(1e-7, (oneYearLater.GeographicDirection - atBoundary.GeographicDirection).Length);
    }

    [TestMethod]
    public void WithTransfer_ImmutableReplay_LeavesPreviousSnapshotsAndOtherMaterialUntouched()
    {
        var catalog = PlanetCrustParcelCatalog.Create(24061984);
        var original = PlanetCrustOwnershipHistory.Create(catalog);
        var transfer = FindValidTransfer(catalog, new PlanetGeologicalEpoch(1_000_000));
        var changed = original.WithTransfer(transfer);
        var replay = PlanetCrustOwnershipHistory.Create(catalog).WithTransfer(transfer);
        var epoch = new PlanetGeologicalEpoch(35_000_000);

        Assert.AreEqual(0, original.Transfers.Count);
        Assert.AreEqual(1, changed.Transfers.Count);
        Assert.AreEqual(changed.Transfers[0], replay.Transfers[0]);
        Assert.AreEqual(original.GetState(transfer.ParcelId, epoch), PlanetCrustOwnershipHistory.Create(catalog).GetState(transfer.ParcelId, epoch));
        Assert.AreEqual(changed.GetState(transfer.ParcelId, epoch), replay.GetState(transfer.ParcelId, epoch));

        var unrelated = catalog.Parcels.First(parcel => parcel.Id != transfer.ParcelId);
        Assert.AreEqual(original.GetState(unrelated.Id, epoch), changed.GetState(unrelated.Id, epoch));
    }

    [TestMethod]
    public void WithTransfer_OutsideProxyContact_RejectsFabricatedOwnershipChanges()
    {
        var catalog = PlanetCrustParcelCatalog.Create(24061984);
        var epoch = new PlanetGeologicalEpoch(1_000_000);
        var boundaries = new PlanetTectonicBoundaryKinematics();
        var material = catalog.Parcels.First(parcel =>
            !boundaries.Sample(catalog.PositionAt(parcel.Id, epoch).GeographicDirection, catalog.World.Seed, epoch).IsAtContact);
        var other = (material.InitialPlate.Index + 1) % 18;
        var transfer = new PlanetCrustPlateTransfer(catalog.World, material.Id, epoch, material.InitialPlate,
            new PlanetTectonicPlateId(catalog.World, other));

        var history = PlanetCrustOwnershipHistory.Create(catalog);
        Assert.ThrowsExactly<InvalidOperationException>(() => history.WithTransfer(transfer));
        Assert.AreEqual(0, history.Transfers.Count);
    }

    [TestMethod]
    public void WithTransfer_StaleOrDuplicateChronologyAndIncorrectOwner_AreRejected()
    {
        var catalog = PlanetCrustParcelCatalog.Create(24061984);
        var eventEpoch = new PlanetGeologicalEpoch(1_000_000);
        var transfer = FindValidTransfer(catalog, eventEpoch);
        var history = PlanetCrustOwnershipHistory.Create(catalog).WithTransfer(transfer);
        Assert.ThrowsExactly<ArgumentException>(() => history.WithTransfer(transfer));

        var stale = transfer with { Epoch = new PlanetGeologicalEpoch(500_000) };
        Assert.ThrowsExactly<ArgumentException>(() => history.WithTransfer(stale));

        var wrongOwner = transfer with { Epoch = new PlanetGeologicalEpoch(2_000_000), FromPlate = transfer.ToPlate,
            ToPlate = transfer.FromPlate };
        // This is a potential legitimate reverse transfer in principle; the
        // boundary-proxy gate must decide whether it is valid, not this test.
        // Instead, supply a source not owning the material.
        wrongOwner = wrongOwner with { FromPlate = transfer.FromPlate, ToPlate = transfer.ToPlate };
        Assert.ThrowsExactly<InvalidOperationException>(() => history.WithTransfer(wrongOwner));
    }

    [TestMethod]
    public void WithTransfer_CrossWorldUnknownParcelOrPresentEpoch_RejectsUnsafeInput()
    {
        var catalog = PlanetCrustParcelCatalog.Create(24061984);
        var transfer = FindValidTransfer(catalog, new PlanetGeologicalEpoch(1_000_000));
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var foreignWorld = PlanetWorldIdentity.CreateCurrent(catalog.World.Seed + 1);

        Assert.ThrowsExactly<ArgumentException>(() => history.WithTransfer(transfer with { World = foreignWorld }));
        Assert.ThrowsExactly<ArgumentException>(() => history.WithTransfer(transfer with {
            ToPlate = new PlanetTectonicPlateId(foreignWorld, transfer.ToPlate.Index) }));
        Assert.ThrowsExactly<ArgumentException>(() => history.WithTransfer(transfer with { FromPlate = transfer.ToPlate }));
        Assert.ThrowsExactly<ArgumentException>(() => history.WithTransfer(transfer with { Epoch = PlanetGeologicalEpoch.Present }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => history.WithTransfer(transfer with {
            ParcelId = new PlanetCrustParcelId((ulong)catalog.Parcels.Count + 1) }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => history.GetState(default, PlanetGeologicalEpoch.Present));
    }

    [TestMethod]
    public void FindNear_HistoricalRegion_CanObserveNewOwnershipWithoutChangingCanonicalPlanet()
    {
        const int seed = 24061984;
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var epoch = new PlanetGeologicalEpoch(1_000_000);
        var transfer = FindValidTransfer(catalog, epoch);
        var surface = new ProceduralPlanetElevationSource();
        var point = PlanetVector.Normalize(new PlanetVector(0.23, 0.65, -0.72));
        var height = surface.SampleElevationMeters(point, seed);

        var changed = PlanetCrustOwnershipHistory.Create(catalog).WithTransfer(transfer);
        var location = changed.GetState(transfer.ParcelId, epoch);
        var nearby = changed.FindNear(location.GeographicDirection, epoch, 1e-6);

        Assert.IsTrue(nearby.Any(item => item.Material.Id == transfer.ParcelId && item.OwningPlate == transfer.ToPlate));
        Assert.AreEqual(catalog.TotalReferenceMassKilograms, changed.TotalReferenceMassKilograms);
        Assert.AreEqual(height, surface.SampleElevationMeters(point, seed));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => changed.FindNear(point, epoch, double.NaN));
    }

    private static PlanetCrustPlateTransfer FindValidTransfer(PlanetCrustParcelCatalog catalog, PlanetGeologicalEpoch epoch)
    {
        var boundaries = new PlanetTectonicBoundaryKinematics();
        foreach (var material in catalog.Parcels)
        {
            var atEpoch = catalog.PositionAt(material.Id, epoch);
            var sample = boundaries.Sample(atEpoch.GeographicDirection, catalog.World.Seed, epoch);
            if (!sample.IsAtContact || sample.Kind is PlanetPlateBoundaryKind.OutsideContact or PlanetPlateBoundaryKind.Neutral)
            {
                continue;
            }

            var owner = material.InitialPlate;
            var other = sample.FirstPlate == owner ? sample.SecondPlate :
                sample.SecondPlate == owner ? sample.FirstPlate : (PlanetTectonicPlateId?)null;
            if (other is not null)
            {
                return new PlanetCrustPlateTransfer(catalog.World, material.Id, epoch, owner, other.Value);
            }
        }

        throw new AssertFailedException("Fixture must contain a real canonical near-boundary material sample.");
    }
}
