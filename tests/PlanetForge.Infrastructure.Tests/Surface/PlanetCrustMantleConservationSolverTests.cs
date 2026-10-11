using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetCrustMantleConservationSolverTests
{
    [TestMethod]
    [DataRow(24061984)]
    [DataRow(346147916)]
    [DataRow(579460630)]
    public void Advance_CanonicalGeologicalEvents_PreserveCrustPlusMantleMassAndMaterialIdentity(int seed)
    {
        var (catalog, history, older, newer) = CreateWorld(seed);
        var solver = new PlanetCrustMantleConservationSolver();
        var starting = solver.CreateInitial(catalog, history, older);
        var events = solver.SuggestExchanges(starting, catalog, history, newer, 32);
        var after = solver.Advance(starting, catalog, history, newer, events);

        Assert.AreEqual(catalog.World, after.World);
        Assert.AreEqual(newer.Epoch, after.Epoch);
        Assert.AreEqual(events.Count, after.Events.Count);
        Assert.AreEqual(starting.InitialTotalMassKilograms, after.InitialTotalMassKilograms);
        Assert.AreEqual(starting.TotalMassKilograms, after.TotalMassKilograms,
            starting.TotalMassKilograms * 1e-12);
        Assert.AreEqual(starting.TotalCrustMassKilograms + starting.TotalExchangeableMantleMassKilograms,
            after.TotalMassKilograms, after.TotalMassKilograms * 1e-12);
        Assert.AreEqual(catalog.Parcels.Count, after.Columns.Count);

        for (var i = 0; i < after.Columns.Count; i++)
        {
            var original = starting.Columns[i];
            var evolved = after.Columns[i];
            Assert.AreEqual(original.Material, evolved.Material);
            Assert.AreEqual(original.TotalMassKilograms, evolved.TotalMassKilograms,
                original.TotalMassKilograms * 1e-13);
            Assert.AreEqual(history.GetState(evolved.Material.Id, newer.Epoch).OwningPlate, evolved.OwningPlate);
            Assert.IsGreaterThan(0.0, evolved.CrustMassKilograms);
            Assert.IsGreaterThanOrEqualTo(0.0, evolved.ExchangeableMantleMassKilograms);
            Assert.AreEqual(original.Material.ReferenceMassKilograms,
                evolved.Material.ReferenceMassKilograms);
            Assert.AreEqual(original.Material.Composition, evolved.Material.Composition);
        }

        foreach (var record in after.Events)
        {
            Assert.AreEqual(newer.Epoch, record.Epoch);
            Assert.IsGreaterThan(0.0, record.ContactInfluence);
            Assert.IsGreaterThan(0.0, record.Exchange.MassKilograms);
            Assert.AreEqual(PlanetCrustComposition.Oceanic,
                catalog.Parcels[(int)record.Exchange.MaterialId.Value - 1].Composition);
            if (record.Exchange.Process == PlanetCrustMantleProcess.Spreading)
            {
                Assert.IsTrue(record.CompressionMetersPerMillionYears < 0);
                Assert.IsTrue(record.BoundaryKind is PlanetPlateBoundaryKind.Divergent or PlanetPlateBoundaryKind.ObliqueDivergent);
            }
            else
            {
                Assert.IsTrue(record.CompressionMetersPerMillionYears > 0);
                Assert.IsTrue(record.BoundaryKind is PlanetPlateBoundaryKind.Convergent or PlanetPlateBoundaryKind.ObliqueConvergent);
            }
        }

        Assert.AreEqual(0, starting.Events.Count);
        Assert.AreEqual(older.Epoch, starting.Epoch);
        Assert.AreEqual(catalog.TotalReferenceMassKilograms, starting.TotalCrustMassKilograms);
        Assert.AreEqual(catalog.TotalReferenceMassKilograms *
            (1 + PlanetCrustMantleConservationSolver.InitialMantleBudgetMultiplier),
            starting.InitialTotalMassKilograms,
            starting.InitialTotalMassKilograms * 1e-12);
    }

    [TestMethod]
    public void SuggestExchanges_SeededPlateBoundaries_ProvideAuditableNonzeroSignedEvents()
    {
        var (catalog, history, older, newer) = CreateWorld(24061984);
        var solver = new PlanetCrustMantleConservationSolver();
        var state = solver.CreateInitial(catalog, history, older);
        var proposals = solver.SuggestExchanges(state, catalog, history, newer);
        Assert.IsGreaterThan(0, proposals.Count, "Seeded contact faces must propose a bounded oceanic exchange.");

        var pairKeys = new HashSet<(ulong First, ulong Second)>();
        var faceKeys = newer.Faces.Select(face => (face.First.Value, face.Second.Value)).ToHashSet();
        foreach (var proposal in proposals)
        {
            var key = (Math.Min(proposal.MaterialId.Value, proposal.NeighborId.Value),
                Math.Max(proposal.MaterialId.Value, proposal.NeighborId.Value));
            Assert.IsTrue(faceKeys.Contains(key));
            Assert.IsTrue(pairKeys.Add(key));
            Assert.IsTrue(proposal.MassKilograms > 0 && double.IsFinite(proposal.MassKilograms));
            Assert.IsLessThanOrEqualTo(
                state.Columns[(int)proposal.MaterialId.Value - 1].TotalMassKilograms *
                PlanetCrustMantleConservationSolver.MaximumTransferredFractionPerStep,
                proposal.MassKilograms);
        }

        var after = solver.Advance(state, catalog, history, newer, proposals);
        Assert.IsTrue(after.Columns.Where((column, i) => column.CrustMassKilograms != state.Columns[i].CrustMassKilograms).Any());
        Assert.AreEqual(proposals.Count, after.Events.Count);
    }

    [TestMethod]
    public void Advance_NoEvents_ChangesEpochWithoutChangingMaterialReservoirs()
    {
        var (catalog, history, older, newer) = CreateWorld(42);
        var solver = new PlanetCrustMantleConservationSolver();
        var state = solver.CreateInitial(catalog, history, older);
        var step = solver.Advance(state, catalog, history, newer, []);

        Assert.AreEqual(newer.Epoch, step.Epoch);
        Assert.AreEqual(0, step.Events.Count);
        foreach (var before in state.Columns)
        {
            var after = step.Columns[(int)before.Material.Id.Value - 1];
            Assert.AreEqual(before.CrustMassKilograms, after.CrustMassKilograms);
            Assert.AreEqual(before.ExchangeableMantleMassKilograms, after.ExchangeableMantleMassKilograms);
            Assert.AreEqual(before.TotalMassKilograms, after.TotalMassKilograms);
        }

        Assert.AreEqual(state.TotalMassKilograms, step.TotalMassKilograms);
    }

    [TestMethod]
    public void Advance_MultipleEpochSteps_ReplayIdenticallyAndKeepAllPreviousAuditEvents()
    {
        var catalog = PlanetCrustParcelCatalog.Create(24061984);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var twoMyr = PlanetCrustSphericalControlVolumes.BuildAtEpoch(catalog, history, new PlanetGeologicalEpoch(2_000_000));
        var oneMyr = PlanetCrustSphericalControlVolumes.BuildAtEpoch(catalog, history, new PlanetGeologicalEpoch(1_000_000));
        var present = PlanetCrustSphericalControlVolumes.Build(catalog);
        var solver = new PlanetCrustMantleConservationSolver();
        var initial = solver.CreateInitial(catalog, history, twoMyr);

        PlanetCrustMantleReservoirSnapshot Step(
            PlanetCrustMantleReservoirSnapshot start, PlanetCrustSphericalControlVolumes target) =>
            solver.Advance(start, catalog, history, target, solver.SuggestExchanges(start, catalog, history, target));

        var one = Step(initial, oneMyr);
        var presentState = Step(one, present);
        var replayOne = Step(initial, oneMyr);
        var replayPresent = Step(replayOne, present);

        CollectionAssert.AreEqual(one.Columns.ToArray(), replayOne.Columns.ToArray());
        CollectionAssert.AreEqual(presentState.Columns.ToArray(), replayPresent.Columns.ToArray());
        CollectionAssert.AreEqual(presentState.Events.ToArray(), replayPresent.Events.ToArray());
        Assert.AreEqual(present.Epoch, presentState.Epoch);
        Assert.AreEqual(initial.TotalMassKilograms, presentState.TotalMassKilograms,
            initial.TotalMassKilograms * 1e-12);
        Assert.IsGreaterThanOrEqualTo(one.Events.Count, presentState.Events.Count);
        CollectionAssert.AreEqual(one.Events.ToArray(), presentState.Events.Take(one.Events.Count).ToArray());
        Assert.AreEqual(initial.Events.Count, 0);
        Assert.AreEqual(initial.InitialTotalMassKilograms, presentState.InitialTotalMassKilograms);
    }

    [TestMethod]
    public void Advance_BadEventDuplicateFaceReverseChronologyAndForeignWorld_AreRejected()
    {
        var (catalog, history, older, newer) = CreateWorld(24061984);
        var solver = new PlanetCrustMantleConservationSolver();
        var starting = solver.CreateInitial(catalog, history, older);
        var events = solver.SuggestExchanges(starting, catalog, history, newer);
        Assert.IsGreaterThan(0, events.Count);
        var candidate = events[0];

        Assert.ThrowsExactly<ArgumentException>(() => solver.Advance(starting, catalog, history, newer,
            [candidate, candidate]));
        Assert.ThrowsExactly<ArgumentException>(() => solver.Advance(starting, catalog, history, newer,
            [candidate with { MassKilograms = double.NaN }]));
        Assert.ThrowsExactly<ArgumentException>(() => solver.Advance(starting, catalog, history, newer,
            [candidate with { MassKilograms = -1 }]));
        Assert.ThrowsExactly<ArgumentException>(() => solver.Advance(starting, catalog, history, newer,
            [candidate with { Process = candidate.Process == PlanetCrustMantleProcess.Spreading
                ? PlanetCrustMantleProcess.Subduction : PlanetCrustMantleProcess.Spreading }]));
        Assert.ThrowsExactly<ArgumentException>(() => solver.Advance(starting, catalog, history, newer,
            [candidate with { NeighborId = candidate.MaterialId }]));

        var donor = starting.Columns[(int)candidate.MaterialId.Value - 1];
        var reservoir = candidate.Process == PlanetCrustMantleProcess.Spreading
            ? donor.ExchangeableMantleMassKilograms : donor.CrustMassKilograms;
        Assert.ThrowsExactly<ArgumentException>(() => solver.Advance(starting, catalog, history, newer,
            [candidate with { MassKilograms = reservoir * 0.5 }]));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            solver.Advance(solver.Advance(starting, catalog, history, newer, []),
                catalog, history, older, []));
        var foreign = PlanetCrustParcelCatalog.Create(43);
        Assert.ThrowsExactly<ArgumentException>(() => solver.Advance(starting, foreign,
            PlanetCrustOwnershipHistory.Create(foreign), newer, []));
    }

    [TestMethod]
    public void Advance_ReferenceCatalogueAndCanonicalTerrain_DoNotChange()
    {
        const int seed = 24061984;
        var (catalog, history, older, newer) = CreateWorld(seed);
        var baselineMass = catalog.TotalReferenceMassKilograms;
        var source = new ProceduralPlanetElevationSource();
        var direction = PlanetVector.Normalize(new PlanetVector(0.23, 0.54, -0.81));
        var baselineElevation = source.SampleElevationMeters(direction, seed);
        var solver = new PlanetCrustMantleConservationSolver();
        var state = solver.CreateInitial(catalog, history, older);
        _ = solver.Advance(state, catalog, history, newer, solver.SuggestExchanges(state, catalog, history, newer));

        Assert.AreEqual(baselineMass, catalog.TotalReferenceMassKilograms);
        Assert.AreEqual(baselineMass, catalog.Parcels.Sum(material => material.ReferenceMassKilograms));
        Assert.AreEqual(baselineElevation, source.SampleElevationMeters(direction, seed));
        Assert.AreEqual(0, history.Transfers.Count);
    }

    private static (
        PlanetCrustParcelCatalog Catalog,
        PlanetCrustOwnershipHistory History,
        PlanetCrustSphericalControlVolumes Older,
        PlanetCrustSphericalControlVolumes Younger)
        CreateWorld(int seed)
    {
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var older = PlanetCrustSphericalControlVolumes.BuildAtEpoch(
            catalog, history, new PlanetGeologicalEpoch(1_000_000));
        return (catalog, history, older, PlanetCrustSphericalControlVolumes.Build(catalog));
    }
}
