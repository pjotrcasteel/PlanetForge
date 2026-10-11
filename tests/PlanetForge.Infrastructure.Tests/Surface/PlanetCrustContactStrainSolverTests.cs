using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetCrustContactStrainSolverTests
{
    [TestMethod]
    [DataRow(24061984)]
    [DataRow(346147916)]
    public void Evaluate_CanonicalPlateContacts_ConservesEachParcelMassWithBoundedStrain(int seed)
    {
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var neighborhood = PlanetCrustMaterialNeighborhood.Build(catalog);
        var snapshot = new PlanetCrustContactStrainSolver().Evaluate(
            catalog, history, neighborhood, PlanetGeologicalEpoch.Present, 0.5);

        Assert.AreEqual(catalog.World, snapshot.World);
        Assert.AreEqual(catalog.Parcels.Count, snapshot.Columns.Count);
        Assert.AreEqual(catalog.TotalReferenceMassKilograms, snapshot.TotalReferenceMassKilograms);
        Assert.IsGreaterThan(0, snapshot.Contacts.Count, "Real seeded plate boundaries should reach neighboring material.");
        Assert.IsTrue(snapshot.Contacts.All(contact => contact.First.Value < contact.Second.Value &&
            contact.ContactInfluence > 0.0 &&
            contact.Kind is not PlanetPlateBoundaryKind.Neutral and not PlanetPlateBoundaryKind.OutsideContact));
        Assert.IsTrue(snapshot.Columns.Any(column => Math.Abs(column.LogAreaStrain) > 1e-9));

        foreach (var column in snapshot.Columns)
        {
            var reference = column.State.Material;
            Assert.AreEqual(reference.ReferenceMassKilograms, column.MassKilograms);
            Assert.AreEqual(reference.Composition, column.State.Material.Composition);
            Assert.IsTrue(double.IsFinite(column.AreaSquareMeters) && double.IsFinite(column.ThicknessMeters));
            Assert.IsTrue(column.LogAreaStrain >= -PlanetCrustContactStrainSolver.MaximumAbsoluteLogAreaStrain &&
                column.LogAreaStrain <= PlanetCrustContactStrainSolver.MaximumAbsoluteLogAreaStrain);
            Assert.AreEqual(reference.ReferenceMassKilograms,
                column.AreaSquareMeters * column.ThicknessMeters * reference.ReferenceDensityKilogramsPerCubicMeter,
                reference.ReferenceMassKilograms * 1e-12);
            Assert.IsGreaterThan(0.0, column.AreaSquareMeters);
            Assert.IsGreaterThan(0.0, column.ThicknessMeters);
            Assert.IsGreaterThanOrEqualTo(0.0, column.ShearExposureMeters);
        }
    }

    [TestMethod]
    public void ComputeLogAreaContribution_SyntheticCollisionExtensionAndPureShear_HaveCorrectSigns()
    {
        var plates = new PlanetTectonicPlateKinematics().GetPlates(42);
        var collision = new PlanetPlateBoundarySample(plates[0].Id, plates[1].Id,
            PlanetPlateBoundaryKind.Convergent, 0, 1.0, 60_000, 0, 60_000);
        var extension = collision with { Kind = PlanetPlateBoundaryKind.Divergent,
            CompressionMetersPerMillionYears = -60_000 };
        var transform = collision with { Kind = PlanetPlateBoundaryKind.Transform,
            CompressionMetersPerMillionYears = 0, ShearMetersPerMillionYears = 60_000 };

        var shortening = PlanetCrustContactStrainSolver.ComputeLogAreaContribution(collision, 400_000, 0.5, 6);
        var spreading = PlanetCrustContactStrainSolver.ComputeLogAreaContribution(extension, 400_000, 0.5, 6);
        var sliding = PlanetCrustContactStrainSolver.ComputeLogAreaContribution(transform, 400_000, 0.5, 6);

        Assert.IsTrue(shortening < 0);
        Assert.IsTrue(spreading > 0);
        Assert.AreEqual(-shortening, spreading, 1e-12);
        Assert.AreEqual(0.0, sliding);
        Assert.AreEqual(0.0, PlanetCrustContactStrainSolver.ComputeLogAreaContribution(
            collision with { ContactInfluence = 0 }, 400_000, 0.5, 6));

        var catalog = PlanetCrustParcelCatalog.Create(42);
        var material = catalog.Parcels[0];
        var original = new PlanetCrustMaterialState(material, PlanetGeologicalEpoch.Present,
            material.InitialPlate, material.PresentDirection);
        var compressed = PlanetCrustContactStrainSolver.ApplyColumnStrain(original, shortening, 0);
        var extended = PlanetCrustContactStrainSolver.ApplyColumnStrain(original, spreading, 0);
        var sheared = PlanetCrustContactStrainSolver.ApplyColumnStrain(original, sliding, 30_000);
        Assert.IsTrue(compressed.AreaSquareMeters < material.ReferenceAreaSquareMeters);
        Assert.IsTrue(compressed.ThicknessMeters > material.ReferenceThicknessMeters);
        Assert.IsTrue(extended.AreaSquareMeters > material.ReferenceAreaSquareMeters);
        Assert.IsTrue(extended.ThicknessMeters < material.ReferenceThicknessMeters);
        Assert.AreEqual(material.ReferenceAreaSquareMeters, sheared.AreaSquareMeters);
        Assert.AreEqual(material.ReferenceThicknessMeters, sheared.ThicknessMeters,
            material.ReferenceThicknessMeters * 1e-12);
        Assert.AreEqual(30_000.0, sheared.ShearExposureMeters);
        Assert.AreEqual(material.ReferenceMassKilograms, compressed.MassKilograms);
        Assert.AreEqual(material.ReferenceMassKilograms, extended.MassKilograms);
        Assert.AreEqual(material.ReferenceMassKilograms, sheared.MassKilograms);
    }

    [TestMethod]
    public void Evaluate_ZeroDuration_LeavesAllMaterialColumnsAtReferenceState()
    {
        var catalog = PlanetCrustParcelCatalog.Create(24061984);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var graph = PlanetCrustMaterialNeighborhood.Build(catalog);
        var snapshot = new PlanetCrustContactStrainSolver().Evaluate(
            catalog, history, graph, new PlanetGeologicalEpoch(25_000_000), 0);

        foreach (var column in snapshot.Columns)
        {
            var material = column.State.Material;
            Assert.AreEqual(0.0, column.LogAreaStrain);
            Assert.AreEqual(0.0, column.ShearExposureMeters);
            Assert.AreEqual(material.ReferenceAreaSquareMeters, column.AreaSquareMeters);
            Assert.AreEqual(material.ReferenceThicknessMeters, column.ThicknessMeters,
                material.ReferenceThicknessMeters * 1e-12);
        }
    }

    [TestMethod]
    public void Evaluate_SameSeedEpochAndOwnershipHistory_ReplaysIdenticalContactsAndColumns()
    {
        const int seed = 24061984;
        var first = PlanetCrustParcelCatalog.Create(seed);
        var second = PlanetCrustParcelCatalog.Create(seed);
        var epoch = new PlanetGeologicalEpoch(15_000_000);
        var solver = new PlanetCrustContactStrainSolver();
        var left = solver.Evaluate(first, PlanetCrustOwnershipHistory.Create(first),
            PlanetCrustMaterialNeighborhood.Build(first), epoch, 0.25);
        var right = solver.Evaluate(second, PlanetCrustOwnershipHistory.Create(second),
            PlanetCrustMaterialNeighborhood.Build(second), epoch, 0.25);

        CollectionAssert.AreEqual(left.Contacts.ToArray(), right.Contacts.ToArray());
        Assert.AreEqual(left.Columns.Count, right.Columns.Count);
        for (var i = 0; i < left.Columns.Count; i++)
        {
            Assert.AreEqual(left.Columns[i].State.Material.Id, right.Columns[i].State.Material.Id);
            Assert.AreEqual(left.Columns[i].State.GeographicDirection, right.Columns[i].State.GeographicDirection);
            Assert.AreEqual(left.Columns[i].LogAreaStrain, right.Columns[i].LogAreaStrain);
            Assert.AreEqual(left.Columns[i].ThicknessMeters, right.Columns[i].ThicknessMeters);
            Assert.AreEqual(left.Columns[i].ShearExposureMeters, right.Columns[i].ShearExposureMeters);
        }
    }

    [TestMethod]
    public void Evaluate_AcceptedOwnershipTransfer_UsesTheNewFrameWithoutMutatingHistory()
    {
        var catalog = PlanetCrustParcelCatalog.Create(24061984);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var transfer = history.SuggestTransfers(new PlanetGeologicalEpoch(1_000_000), 16).First().Transfer;
        var accepted = history.WithTransfer(transfer);
        var epoch = new PlanetGeologicalEpoch(2_000_000);
        var snapshot = new PlanetCrustContactStrainSolver().Evaluate(
            catalog, accepted, PlanetCrustMaterialNeighborhood.Build(catalog), epoch, 0.1);
        var column = snapshot.Columns[(int)transfer.ParcelId.Value - 1];

        Assert.AreEqual(transfer.ToPlate, column.State.OwningPlate);
        Assert.AreEqual(transfer.FromPlate, history.GetState(transfer.ParcelId, epoch).OwningPlate);
        Assert.AreEqual(transfer.ParcelId, column.State.Material.Id);
        Assert.AreEqual(column.State.Material.ReferenceMassKilograms, column.MassKilograms);
        Assert.AreEqual(0, history.Transfers.Count);
    }

    [TestMethod]
    public void Evaluate_ForeignWorldInvalidStepOrNullInputs_RejectsUnsafeRequests()
    {
        var catalog = PlanetCrustParcelCatalog.Create(24061984);
        var other = PlanetCrustParcelCatalog.Create(346147916);
        var graph = PlanetCrustMaterialNeighborhood.Build(catalog);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var solver = new PlanetCrustContactStrainSolver();
        void Evaluate(double duration) =>
            solver.Evaluate(catalog, history, graph, PlanetGeologicalEpoch.Present, duration);

        Assert.ThrowsExactly<ArgumentException>(() => solver.Evaluate(other, history, graph, PlanetGeologicalEpoch.Present, 0.1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Evaluate(-0.1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Evaluate(double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Evaluate(double.PositiveInfinity));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Evaluate(2.0));
        Assert.ThrowsExactly<ArgumentNullException>(() => solver.Evaluate(null!, history, graph, PlanetGeologicalEpoch.Present, 0.1));
    }

    [TestMethod]
    public void Evaluate_GeologyResearch_DoesNotAlterTheCanonicalSurface()
    {
        const int seed = 24061984;
        var source = new ProceduralPlanetElevationSource();
        var direction = PlanetVector.Normalize(new PlanetVector(0.31, -0.21, 0.88));
        var baseline = source.SampleElevationMeters(direction, seed);
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var graph = PlanetCrustMaterialNeighborhood.Build(catalog);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var snapshot = new PlanetCrustContactStrainSolver().Evaluate(
            catalog, history, graph, PlanetGeologicalEpoch.Present, 0.5);

        Assert.AreEqual(baseline, source.SampleElevationMeters(direction, seed));
        Assert.AreEqual(0, history.Transfers.Count);
        Assert.AreEqual(snapshot.Columns.Count, catalog.Parcels.Count);
        Assert.AreEqual(snapshot.TotalReferenceMassKilograms, catalog.TotalReferenceMassKilograms);
    }
}
