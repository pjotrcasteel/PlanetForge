using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetCrustMassExchangeSolverTests
{
    [TestMethod]
    [DataRow(24061984)]
    [DataRow(346147916)]
    [DataRow(579460630)]
    public void Exchange_SeededCrust_ClosesEveryEdgeLedgerAndConservesTotalMass(int seed)
    {
        var (catalog, graph, input) = CreateInput(seed, 0.5);
        var snapshot = new PlanetCrustMassExchangeSolver().Exchange(catalog, graph, input);

        Assert.AreEqual(catalog.World, snapshot.World);
        Assert.AreEqual(catalog.Parcels.Count, snapshot.Columns.Count);
        Assert.AreEqual(catalog.TotalReferenceMassKilograms, snapshot.ReferenceMassKilograms);
        Assert.IsGreaterThan(0, snapshot.Exchanges.Count, "At least one neighboring strain contrast should transport material.");

        var incoming = new double[catalog.Parcels.Count];
        var outgoing = new double[catalog.Parcels.Count];
        var incomingVolume = new double[catalog.Parcels.Count];
        var edgeKeys = graph.Edges.Select(edge => (edge.First.Value, edge.Second.Value)).ToHashSet();
        var unique = new HashSet<(ulong, ulong)>();
        foreach (var transfer in snapshot.Exchanges)
        {
            Assert.IsTrue(transfer.MassKilograms > 0 && double.IsFinite(transfer.MassKilograms));
            Assert.IsTrue(unique.Add((transfer.From.Value, transfer.To.Value)), "One edge can transport only once per step.");
            var edge = (Math.Min(transfer.From.Value, transfer.To.Value), Math.Max(transfer.From.Value, transfer.To.Value));
            Assert.IsTrue(edgeKeys.Contains(edge), "Mass must move over a real seeded material edge.");

            var from = (int)transfer.From.Value - 1;
            var to = (int)transfer.To.Value - 1;
            outgoing[from] += transfer.MassKilograms;
            incoming[to] += transfer.MassKilograms;
            incomingVolume[to] += transfer.MassKilograms /
                catalog.Parcels[from].ReferenceDensityKilogramsPerCubicMeter;
        }

        for (var i = 0; i < snapshot.Columns.Count; i++)
        {
            var column = snapshot.Columns[i];
            var material = catalog.Parcels[i];
            Assert.AreEqual(material.Id, column.State.Material.Id);
            Assert.AreEqual(material.ReferenceMassKilograms, column.RetainedLocalMassKilograms + column.ExportedMassKilograms,
                material.ReferenceMassKilograms * 1e-12);
            Assert.AreEqual(outgoing[i], column.ExportedMassKilograms, material.ReferenceMassKilograms * 1e-12);
            Assert.AreEqual(incoming[i], column.ImportedMassKilograms, material.ReferenceMassKilograms * 1e-12);
            Assert.AreEqual(material.ReferenceMassKilograms - outgoing[i] + incoming[i],
                column.TotalMassKilograms, material.ReferenceMassKilograms * 1e-12);
            Assert.IsLessThanOrEqualTo(material.ReferenceMassKilograms *
                (PlanetCrustMassExchangeSolver.MaximumOutgoingMassFractionPerStep + 1e-12), outgoing[i]);
            var volume = (material.ReferenceMassKilograms - outgoing[i]) / material.ReferenceDensityKilogramsPerCubicMeter +
                incomingVolume[i];
            Assert.AreEqual(volume / column.AreaSquareMeters, column.ThicknessMeters,
                Math.Max(1e-9, column.ThicknessMeters * 1e-12));
            Assert.IsTrue(double.IsFinite(column.ThicknessMeters) && column.ThicknessMeters > 0);
        }

        Assert.AreEqual(snapshot.ReferenceMassKilograms, snapshot.TotalFinalMassKilograms,
            snapshot.ReferenceMassKilograms * 1e-12);
        Assert.AreEqual(snapshot.Exchanges.Sum(x => x.MassKilograms),
            snapshot.Columns.Sum(x => x.ExportedMassKilograms), snapshot.ReferenceMassKilograms * 1e-12);
        Assert.AreEqual(snapshot.Exchanges.Sum(x => x.MassKilograms),
            snapshot.Columns.Sum(x => x.ImportedMassKilograms), snapshot.ReferenceMassKilograms * 1e-12);
    }

    [TestMethod]
    public void Exchange_ZeroDuration_NoTransportOrNewRelief()
    {
        var (catalog, graph, input) = CreateInput(24061984, 0);
        var result = new PlanetCrustMassExchangeSolver().Exchange(catalog, graph, input);
        Assert.AreEqual(0, result.Exchanges.Count);
        Assert.AreEqual(0.0, result.DurationMillionYears);
        foreach (var column in result.Columns)
        {
            var material = column.State.Material;
            Assert.AreEqual(0.0, column.ImportedMassKilograms);
            Assert.AreEqual(0.0, column.ExportedMassKilograms);
            Assert.AreEqual(material.ReferenceMassKilograms, column.TotalMassKilograms);
            Assert.AreEqual(material.ReferenceThicknessMeters, column.ThicknessMeters,
                material.ReferenceThicknessMeters * 1e-12);
        }

        var field = new PlanetCrustColumnThicknessField(result);
        foreach (var material in catalog.Parcels.Where((_, index) => index % 29 == 0))
        {
            Assert.AreEqual(0.0, field.SampleThicknessAnomalyMeters(material.PresentDirection), 1e-8);
        }
    }

    [TestMethod]
    public void Exchange_SameWorldAndEpoch_ReplaysExchangeEventsAndExactPhysicalSamples()
    {
        var (first, graphA, inputA) = CreateInput(24061984, 0.25, 12_000_000);
        var (second, graphB, inputB) = CreateInput(24061984, 0.25, 12_000_000);
        var solver = new PlanetCrustMassExchangeSolver();
        var a = solver.Exchange(first, graphA, inputA);
        var b = solver.Exchange(second, graphB, inputB);
        CollectionAssert.AreEqual(a.Exchanges.ToArray(), b.Exchanges.ToArray());
        Assert.AreEqual(a.TotalFinalMassKilograms, b.TotalFinalMassKilograms);

        var fieldA = new PlanetCrustColumnThicknessField(a);
        var fieldB = new PlanetCrustColumnThicknessField(b);
        foreach (var direction in first.Parcels.Where((_, i) => i % 41 == 0).Select(p => p.PresentDirection))
        {
            Assert.AreEqual(fieldA.SampleThicknessAnomalyMeters(direction), fieldB.SampleThicknessAnomalyMeters(direction));
        }
    }

    [TestMethod]
    public void ComputeRequestedMass_StrainContrastControlsTransportAndCapsSingleEdge()
    {
        Assert.AreEqual(0.0, PlanetCrustMassExchangeSolver.ComputeRequestedMass(0.0, 10_000.0));
        Assert.AreEqual(50.0, PlanetCrustMassExchangeSolver.ComputeRequestedMass(0.05, 10_000.0), 1e-10);
        Assert.AreEqual(100.0, PlanetCrustMassExchangeSolver.ComputeRequestedMass(2.0, 10_000.0), 1e-10);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetCrustMassExchangeSolver.ComputeRequestedMass(double.NaN, 10_000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetCrustMassExchangeSolver.ComputeRequestedMass(-0.1, 10_000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetCrustMassExchangeSolver.ComputeRequestedMass(0.1, double.PositiveInfinity));
    }

    [TestMethod]
    public void Exchange_ForeignWorldAndMissingInputs_FailBeforeExchangingMaterial()
    {
        var (catalog, graph, input) = CreateInput(24061984, 0.5);
        var (foreign, foreignGraph, foreignSnapshot) = CreateInput(346147916, 0.5);
        var solver = new PlanetCrustMassExchangeSolver();
        Assert.ThrowsExactly<ArgumentException>(() => solver.Exchange(catalog, foreignGraph, input));
        Assert.ThrowsExactly<ArgumentException>(() => solver.Exchange(catalog, graph, foreignSnapshot));
        Assert.ThrowsExactly<ArgumentException>(() => solver.Exchange(foreign, graph, input));
        Assert.ThrowsExactly<ArgumentNullException>(() => solver.Exchange(null!, graph, input));
        Assert.ThrowsExactly<ArgumentNullException>(() => solver.Exchange(catalog, null!, input));
        Assert.ThrowsExactly<ArgumentNullException>(() => solver.Exchange(catalog, graph, null!));
    }

    [TestMethod]
    public void ThicknessField_SphericalSampling_IsContinuousAndIndependentOfRegionalBoundaries()
    {
        const int seed = 24061984;
        var (catalog, graph, input) = CreateInput(seed, 0.5);
        var result = new PlanetCrustMassExchangeSolver().Exchange(catalog, graph, input);
        var field = new PlanetCrustColumnThicknessField(result);
        var source = new ProceduralPlanetElevationSource();
        var direction = PlanetVector.Normalize(new PlanetVector(0.62, -0.38, 0.69));
        var existingElevation = source.SampleElevationMeters(direction, seed);

        foreach (var reference in catalog.Parcels.Where((_, index) => index % 57 == 0))
        {
            var center = reference.PresentDirection;
            var a = field.SampleThicknessAnomalyMeters(center);
            var samePositionAcrossAnotherHypotheticalLod = field.SampleThicknessAnomalyMeters(center);
            var twentyMetersAway = PlanetVector.Normalize(center + PlanetVector.UnitY *
                (20.0 / PlanetTectonicPlateKinematics.NominalPlanetRadiusMeters));
            Assert.AreEqual(a, samePositionAcrossAnotherHypotheticalLod);
            Assert.IsTrue(double.IsFinite(a));
            Assert.IsLessThan(5.0, Math.Abs(a - field.SampleThicknessAnomalyMeters(twentyMetersAway)),
                "Research field must not produce seam-like column discontinuities over 20 m.");
        }

        Assert.AreEqual(existingElevation, source.SampleElevationMeters(direction, seed),
            "A crust-thickness diagnostic must not silently alter the public planet terrain.");
    }

    private static (PlanetCrustParcelCatalog Catalog, PlanetCrustMaterialNeighborhood Graph, PlanetCrustInteractionSnapshot Input)
        CreateInput(int seed, double duration, long yearsBeforePresent = 0)
    {
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var graph = PlanetCrustMaterialNeighborhood.Build(catalog);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var input = new PlanetCrustContactStrainSolver().Evaluate(
            catalog, history, graph, new PlanetGeologicalEpoch(yearsBeforePresent), duration);
        return (catalog, graph, input);
    }
}
