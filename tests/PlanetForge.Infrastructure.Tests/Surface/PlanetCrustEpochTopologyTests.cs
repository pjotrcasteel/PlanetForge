using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetCrustEpochTopologyTests
{
    [TestMethod]
    [DataRow(24061984)]
    [DataRow(346147916)]
    [DataRow(579460630)]
    public void BuildAtEpoch_CanonicalMovingMaterials_ClosesSphereWithReciprocalFaces(int seed)
    {
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var epoch = new PlanetGeologicalEpoch(1_000_000);
        var moved = PlanetCrustSphericalControlVolumes.BuildAtEpoch(catalog, history, epoch);
        Assert.AreEqual(catalog.World, moved.World);
        Assert.AreEqual(epoch, moved.Epoch);
        Assert.AreEqual(catalog.Parcels.Count, moved.Cells.Count);
        Assert.AreEqual(PlanetCrustSphericalControlVolumes.PlanetSurfaceAreaSquareMeters,
            moved.GeographicAreaSquareMeters, moved.GeographicAreaSquareMeters * 1e-8);
        Assert.IsGreaterThan(2000, moved.Faces.Count);
        Assert.IsLessThan(4000, moved.Faces.Count);

        foreach (var face in moved.Faces.Where((_, index) => index % 13 == 0))
        {
            var one = moved.GetCell(face.First);
            var two = moved.GetCell(face.Second);
            Assert.IsTrue(ContainsEdge(one, face));
            Assert.IsTrue(ContainsEdge(two, face));
            var midpoint = PlanetVector.Normalize(face.Start + face.End);
            var first = history.GetState(face.First, epoch).GeographicDirection;
            var second = history.GetState(face.Second, epoch).GeographicDirection;
            Assert.AreEqual(PlanetVector.Dot(midpoint, first), PlanetVector.Dot(midpoint, second), 1e-8);
        }

        foreach (var material in catalog.Parcels.Where((_, index) => index % 17 == 0))
        {
            var direction = history.GetState(material.Id, epoch).GeographicDirection;
            Assert.AreEqual(material.Id, moved.Locate(direction).MaterialId);
            Assert.AreEqual(material.Id, moved.GetCell(material.Id).MaterialId);
        }
    }

    [TestMethod]
    public void BuildAtEpoch_Present_ErasesNoReferenceGeometry()
    {
        var catalog = PlanetCrustParcelCatalog.Create(42);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var reference = PlanetCrustSphericalControlVolumes.Build(catalog);
        var present = PlanetCrustSphericalControlVolumes.BuildAtEpoch(catalog, history, PlanetGeologicalEpoch.Present);

        Assert.AreEqual(PlanetGeologicalEpoch.Present, present.Epoch);
        Assert.AreEqual(reference.GeographicAreaSquareMeters, present.GeographicAreaSquareMeters);
        CollectionAssert.AreEqual(reference.Faces.ToArray(), present.Faces.ToArray());
        for (var i = 0; i < reference.Cells.Count; i++)
        {
            Assert.AreEqual(reference.Cells[i].GeographicAreaSquareMeters, present.Cells[i].GeographicAreaSquareMeters);
            CollectionAssert.AreEqual(reference.Cells[i].BoundaryVertices.ToArray(), present.Cells[i].BoundaryVertices.ToArray());
        }
    }

    [TestMethod]
    public void Compare_GeologicalEpochs_DetectsMovingFaceTopologyWithoutChangingMaterialMass()
    {
        const int seed = 24061984;
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var present = PlanetCrustSphericalControlVolumes.Build(catalog);
        var earlier = PlanetCrustSphericalControlVolumes.BuildAtEpoch(
            catalog, history, new PlanetGeologicalEpoch(40_000_000));
        var transition = PlanetCrustEpochTopologyTransition.Compare(catalog, history, present, earlier);

        Assert.AreEqual(catalog.World, transition.World);
        Assert.AreEqual(present.Epoch, transition.FromEpoch);
        Assert.AreEqual(earlier.Epoch, transition.ToEpoch);
        Assert.AreEqual(catalog.Parcels.Count, transition.CellChanges.Count);
        Assert.AreEqual(catalog.TotalReferenceMassKilograms, transition.TotalReferenceMassKilograms);
        Assert.AreEqual(PlanetCrustSphericalControlVolumes.PlanetSurfaceAreaSquareMeters,
            transition.OldGeographicAreaSquareMeters, transition.OldGeographicAreaSquareMeters * 1e-8);
        Assert.AreEqual(PlanetCrustSphericalControlVolumes.PlanetSurfaceAreaSquareMeters,
            transition.NewGeographicAreaSquareMeters, transition.NewGeographicAreaSquareMeters * 1e-8);

        Assert.IsGreaterThan(0, transition.AppearedFaceCount + transition.DisappearedFaceCount);
        Assert.AreEqual(present.Faces.Count + transition.AppearedFaceCount - transition.DisappearedFaceCount,
            earlier.Faces.Count);
        Assert.IsTrue(transition.CellChanges.Any(change => change.MaterialTravelMeters > 10_000));
        Assert.IsTrue(transition.CellChanges.Any(change =>
            Math.Abs(change.CurrentGeographicAreaSquareMeters - change.PreviousGeographicAreaSquareMeters) > 1e9));

        foreach (var change in transition.CellChanges)
        {
            var material = catalog.Parcels[(int)change.MaterialId.Value - 1];
            Assert.AreEqual(material.ReferenceMassKilograms, change.ReferenceMassKilograms);
            Assert.AreEqual(material.InitialPlate, change.FromPlate);
            Assert.AreEqual(material.InitialPlate, change.ToPlate);
            Assert.AreEqual(material.ReferenceMassKilograms / change.CurrentGeographicAreaSquareMeters,
                change.CurrentMassPerGeographicSquareMeter);
            Assert.AreEqual(material.ReferenceMassKilograms / change.PreviousGeographicAreaSquareMeters,
                change.PreviousMassPerGeographicSquareMeter);
            Assert.IsGreaterThan(0.0, change.CurrentGeographicAreaSquareMeters);
            Assert.IsGreaterThanOrEqualTo(0.0, change.MaterialTravelMeters);
        }

        Assert.AreEqual(transition.CellChanges.Sum(change => change.ReferenceMassKilograms),
            catalog.TotalReferenceMassKilograms);
    }

    [TestMethod]
    public void Compare_SameWorldAndEpoch_RebuildsDeterministicallyWithoutInventingTopologyEvents()
    {
        var catalog = PlanetCrustParcelCatalog.Create(42);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var epoch = new PlanetGeologicalEpoch(2_000_000);
        var first = PlanetCrustSphericalControlVolumes.BuildAtEpoch(catalog, history, epoch);
        var second = PlanetCrustSphericalControlVolumes.BuildAtEpoch(catalog, history, epoch);
        var transition = PlanetCrustEpochTopologyTransition.Compare(catalog, history, first, second);

        CollectionAssert.AreEqual(first.Faces.ToArray(), second.Faces.ToArray());
        Assert.AreEqual(0, transition.AppearedFaceCount);
        Assert.AreEqual(0, transition.DisappearedFaceCount);
        Assert.IsTrue(transition.CellChanges.All(change =>
            change.PreviousGeographicAreaSquareMeters == change.CurrentGeographicAreaSquareMeters &&
            change.MaterialTravelMeters < 1e-5));
        Assert.IsTrue(transition.FaceChanges.All(face =>
            face.Kind == PlanetCrustEpochFaceChangeKind.Persisted &&
            face.CurrentLengthMeters == face.PreviousLengthMeters));
    }

    [TestMethod]
    public void BuildAtEpoch_AcceptedPlateTransfer_UsesUpdatedMaterialFrameWithoutChangingIdOrMass()
    {
        const int seed = 24061984;
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var original = PlanetCrustOwnershipHistory.Create(catalog);
        var accepted = original.WithTransfer(
            original.SuggestTransfers(new PlanetGeologicalEpoch(1_000_000)).First().Transfer);
        var epoch = new PlanetGeologicalEpoch(2_000_000);
        var before = PlanetCrustSphericalControlVolumes.BuildAtEpoch(catalog, original, epoch);
        var after = PlanetCrustSphericalControlVolumes.BuildAtEpoch(catalog, accepted, epoch);
        var comparison = PlanetCrustEpochTopologyTransition.Compare(catalog, accepted, before, after);
        var moved = accepted.Transfers[0].Transfer.ParcelId;

        Assert.AreEqual(moved, after.Locate(accepted.GetState(moved, epoch).GeographicDirection).MaterialId);
        Assert.AreEqual(catalog.Parcels[(int)moved.Value - 1].ReferenceMassKilograms,
            comparison.CellChanges[(int)moved.Value - 1].ReferenceMassKilograms);
        Assert.AreEqual(0, original.Transfers.Count);
        Assert.AreEqual(1, accepted.Transfers.Count);
        Assert.AreEqual(catalog.TotalReferenceMassKilograms, comparison.TotalReferenceMassKilograms);
        Assert.AreEqual(before.GeographicAreaSquareMeters, after.GeographicAreaSquareMeters,
            after.GeographicAreaSquareMeters * 1e-8);
    }

    [TestMethod]
    public void FaceFlux_CannotUseAnotherGeologicalEpochEvenWithMatchingMaterialWorld()
    {
        const int seed = 24061984;
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var neighbors = PlanetCrustMaterialNeighborhood.Build(catalog);
        var now = PlanetCrustSphericalControlVolumes.Build(catalog);
        var pastEpoch = new PlanetGeologicalEpoch(1_000_000);
        var past = PlanetCrustSphericalControlVolumes.BuildAtEpoch(catalog, history, pastEpoch);
        var strain = new PlanetCrustContactStrainSolver().Evaluate(catalog, history, neighbors, pastEpoch, 0.1);
        var material = new PlanetCrustMassExchangeSolver().Exchange(catalog, neighbors, strain);
        var ledger = new PlanetCrustSphericalFaceFluxLedger();

        Assert.ThrowsExactly<ArgumentException>(() => ledger.Apply(material, now, []));
        var safe = ledger.Apply(material, past, []);
        Assert.AreEqual(pastEpoch, safe.Epoch);
        Assert.AreEqual(safe.InitialMassKilograms, safe.FinalMassKilograms);
    }

    [TestMethod]
    public void BuildAtEpoch_AndCompare_CrossWorldOrReverseTime_RejectUntrustedGeometry()
    {
        var catalog = PlanetCrustParcelCatalog.Create(42);
        var other = PlanetCrustParcelCatalog.Create(43);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var present = PlanetCrustSphericalControlVolumes.Build(catalog);
        var past = PlanetCrustSphericalControlVolumes.BuildAtEpoch(catalog, history, new PlanetGeologicalEpoch(1_000_000));

        Assert.ThrowsExactly<ArgumentException>(() => PlanetCrustSphericalControlVolumes.BuildAtEpoch(
            catalog, PlanetCrustOwnershipHistory.Create(other), past.Epoch));
        Assert.ThrowsExactly<ArgumentException>(() => PlanetCrustEpochTopologyTransition.Compare(
            catalog, history, past, present));
        Assert.ThrowsExactly<ArgumentException>(() => PlanetCrustEpochTopologyTransition.Compare(
            catalog, history, present, PlanetCrustSphericalControlVolumes.Build(other)));
    }

    private static bool ContainsEdge(PlanetCrustSphericalCell cell, PlanetCrustSphericalFace edge)
    {
        for (var i = 0; i < cell.BoundaryVertices.Count; i++)
        {
            var start = cell.BoundaryVertices[i];
            var end = cell.BoundaryVertices[(i + 1) % cell.BoundaryVertices.Count];
            if ((start - edge.Start).Length < 1e-7 && (end - edge.End).Length < 1e-7 ||
                (start - edge.End).Length < 1e-7 && (end - edge.Start).Length < 1e-7)
            {
                return true;
            }
        }

        return false;
    }
}
