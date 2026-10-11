using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetCrustAccumulatedDeformationTests
{
    [TestMethod]
    [DataRow(24061984)]
    [DataRow(346147916)]
    [DataRow(579460630)]
    public void Simulate_ForwardEpochs_ConservesCrustMantleMassAndClosesPressureBalance(int seed)
    {
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var sequence = new PlanetCrustAccumulatedDeformation().Simulate(catalog, history, 2);

        Assert.HasCount(3, sequence);
        Assert.AreEqual(2_000_000L, sequence[0].Epoch.YearsBeforePresent);
        Assert.AreEqual(1_000_000L, sequence[1].Epoch.YearsBeforePresent);
        Assert.AreEqual(0L, sequence[2].Epoch.YearsBeforePresent);
        Assert.AreEqual(0, sequence[0].AcceptedEvents);
        Assert.IsTrue(sequence[0].Columns.All(column => column.AccumulatedLogAreaStrain == 0.0));
        Assert.IsTrue(sequence[0].Columns.All(column => Math.Abs(column.SurfaceDisplacementMeters) <= 1e-8));

        foreach (var snapshot in sequence)
        {
            Assert.AreEqual(catalog.World, snapshot.World);
            Assert.HasCount(catalog.Parcels.Count, snapshot.Columns);
            Assert.AreEqual(sequence[0].ConservedReferenceBudgetKilograms,
                snapshot.ConservedReferenceBudgetKilograms);
            Assert.AreEqual(sequence[0].TotalMaterialBudgetKilograms, snapshot.TotalMaterialBudgetKilograms,
                snapshot.TotalMaterialBudgetKilograms * 1e-12);
            Assert.IsTrue(double.IsFinite(snapshot.MaximumAbsoluteSurfaceDisplacementMeters));

            foreach (var column in snapshot.Columns)
            {
                var reference = catalog.Parcels[(int)column.MaterialId.Value - 1];
                Assert.AreEqual(reference.Composition, column.Composition);
                Assert.AreEqual(history.GetState(column.MaterialId, snapshot.Epoch).OwningPlate, column.OwningPlate);
                Assert.AreEqual(reference.ReferenceMassKilograms * (1.0 +
                    PlanetCrustMantleConservationSolver.InitialMantleBudgetMultiplier),
                    column.CrustMassKilograms + column.ExchangeableMantleMassKilograms,
                    reference.ReferenceMassKilograms * 1e-12);
                Assert.IsGreaterThan(0.0, column.CrustThicknessMeters);
                Assert.IsGreaterThan(0.0, column.CrustMassKilograms);
                Assert.IsGreaterThanOrEqualTo(0.0, column.ExchangeableMantleMassKilograms);
                Assert.IsTrue(double.IsFinite(column.SurfaceDisplacementMeters));
                Assert.IsTrue(double.IsFinite(column.MantleRootChangeMeters));
                Assert.IsLessThan(1e-6, Math.Abs(column.PressureBalanceResidualKilogramsPerSquareMeter));
                Assert.IsLessThanOrEqualTo(0.30 + 1e-12, Math.Abs(column.AccumulatedLogAreaStrain));
            }
        }

        Assert.IsTrue(sequence[2].Columns.Any(column =>
            column.AccumulatedLogAreaStrain != 0 || column.CrustMassKilograms !=
            catalog.Parcels[(int)column.MaterialId.Value - 1].ReferenceMassKilograms),
            "At least one material column should record real accumulated geological change.");
        Assert.IsGreaterThanOrEqualTo(sequence[1].AcceptedEvents, sequence[2].AcceptedEvents);
    }

    [TestMethod]
    public void Simulate_RepeatedSeedTimeline_IsDeterministicWithoutMutatingInputs()
    {
        var catalog = PlanetCrustParcelCatalog.Create(24061984);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var solver = new PlanetCrustAccumulatedDeformation();
        var first = solver.Simulate(catalog, history, 2);
        var replay = solver.Simulate(catalog, history, 2);

        for (var step = 0; step < first.Count; step++)
        {
            Assert.AreEqual(first[step].Epoch, replay[step].Epoch);
            Assert.AreEqual(first[step].AcceptedEvents, replay[step].AcceptedEvents);
            CollectionAssert.AreEqual(first[step].Columns.ToArray(), replay[step].Columns.ToArray());
        }

        Assert.AreEqual(0, history.Transfers.Count);
        Assert.AreEqual(catalog.TotalReferenceMassKilograms, catalog.Parcels.Sum(p => p.ReferenceMassKilograms));
    }

    [TestMethod]
    public void Simulate_ZeroSteps_ProducesOnlyReferenceState()
    {
        var catalog = PlanetCrustParcelCatalog.Create(42);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var sequence = new PlanetCrustAccumulatedDeformation().Simulate(catalog, history, 0);
        Assert.HasCount(1, sequence);
        Assert.AreEqual(PlanetGeologicalEpoch.Present, sequence[0].Epoch);
        Assert.AreEqual(0, sequence[0].AcceptedEvents);
        foreach (var column in sequence[0].Columns)
        {
            var reference = catalog.Parcels[(int)column.MaterialId.Value - 1];
            Assert.AreEqual(0.0, column.AccumulatedLogAreaStrain);
            Assert.AreEqual(0.0, column.AccumulatedShearMeters);
            Assert.AreEqual(reference.ReferenceThicknessMeters, column.CrustThicknessMeters,
                reference.ReferenceThicknessMeters * 1e-12);
            Assert.AreEqual(0.0, column.SurfaceDisplacementMeters, 1e-8);
            Assert.AreEqual(0.0, column.MantleRootChangeMeters, 1e-8);
        }
    }

    [TestMethod]
    public void Simulate_RejectsForeignHistoryAndUnboundedResearchSteps()
    {
        var catalog = PlanetCrustParcelCatalog.Create(42);
        var other = PlanetCrustParcelCatalog.Create(43);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var solver = new PlanetCrustAccumulatedDeformation();

        Assert.ThrowsExactly<ArgumentException>(() => solver.Simulate(catalog,
            PlanetCrustOwnershipHistory.Create(other), 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => solver.Simulate(catalog, history, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => solver.Simulate(catalog, history,
            PlanetCrustAccumulatedDeformation.MaximumResearchSteps + 1));
        Assert.ThrowsExactly<ArgumentNullException>(() => solver.Simulate(null!, history, 1));
    }

    [TestMethod]
    public void Simulate_ResearchOnly_DoesNotChangePlanetElevationOrSaveAddressing()
    {
        const int seed = 24061984;
        var baseline = new ProceduralPlanetElevationSource();
        var point = PlanetVector.Normalize(new PlanetVector(0.41, -0.52, 0.75));
        var height = baseline.SampleElevationMeters(point, seed);
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var history = PlanetCrustOwnershipHistory.Create(catalog);

        var output = new PlanetCrustAccumulatedDeformation().Simulate(catalog, history, 2);
        Assert.AreEqual(height, baseline.SampleElevationMeters(point, seed));
        Assert.AreEqual(0, history.Transfers.Count);
        Assert.AreEqual(catalog.World, output[0].World);
    }
}
