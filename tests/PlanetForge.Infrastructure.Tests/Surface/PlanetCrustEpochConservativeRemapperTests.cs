using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetCrustEpochConservativeRemapperTests
{
    [TestMethod]
    [DataRow(24061984)]
    [DataRow(346147916)]
    [DataRow(579460630)]
    public void Remap_ChangingGeographicCells_ConservesUniformTracerAndClosesBothAreaPartitions(int seed)
    {
        var (catalog, history, previous, next) = CreateEpochs(seed, 1_000_000);
        const double kilogramsPerSquareMeter = 1_500.0;
        var source = previous.Cells.Select(cell => cell.GeographicAreaSquareMeters * kilogramsPerSquareMeter).ToArray();
        var remapped = new PlanetCrustEpochConservativeRemapper().Remap(catalog, history, previous, next, source);

        Assert.AreEqual(catalog.World, remapped.World);
        Assert.AreEqual(previous.Epoch, remapped.FromEpoch);
        Assert.AreEqual(next.Epoch, remapped.ToEpoch);
        Assert.AreEqual(source.Sum(), remapped.OriginalTracerKilograms, source.Sum() * 1e-13);
        Assert.AreEqual(source.Sum(), remapped.FinalTracerKilograms, source.Sum() * 1e-12);
        Assert.IsGreaterThan(catalog.Parcels.Count, remapped.Transfers.Count);
        Assert.IsLessThanOrEqualTo(PlanetCrustEpochConservativeRemapper.MaximumRelativeGeographicAreaError,
            remapped.WorstGeographicAreaMismatchFraction);

        var outgoing = new double[catalog.Parcels.Count];
        var incoming = new double[catalog.Parcels.Count];
        var sourceOverlaps = new double[catalog.Parcels.Count];
        var targetOverlaps = new double[catalog.Parcels.Count];
        foreach (var transfer in remapped.Transfers)
        {
            var from = (int)transfer.FromCell.Value - 1;
            var to = (int)transfer.ToCell.Value - 1;
            Assert.IsGreaterThan(0.0, transfer.OverlapAreaSquareMeters);
            Assert.IsGreaterThanOrEqualTo(0.0, transfer.TransferredTracerKilograms);
            outgoing[from] += transfer.TransferredTracerKilograms;
            incoming[to] += transfer.TransferredTracerKilograms;
            sourceOverlaps[from] += transfer.OverlapAreaSquareMeters;
            targetOverlaps[to] += transfer.OverlapAreaSquareMeters;
        }

        for (var i = 0; i < catalog.Parcels.Count; i++)
        {
            var area = next.Cells[i].GeographicAreaSquareMeters;
            Assert.AreEqual(catalog.Parcels[i].Id, remapped.Cells[i].CellId);
            Assert.AreEqual(area, remapped.Cells[i].GeographicAreaSquareMeters);
            Assert.AreEqual(source[i], outgoing[i], Math.Max(1e-2, source[i] * 1e-12));
            Assert.AreEqual(incoming[i], remapped.Cells[i].TracerKilograms, Math.Max(1e-2, incoming[i] * 1e-12));
            Assert.AreEqual(previous.Cells[i].GeographicAreaSquareMeters,
                sourceOverlaps[i], previous.Cells[i].GeographicAreaSquareMeters * 1e-4);
            Assert.AreEqual(area, targetOverlaps[i], area * 1e-4);
            Assert.AreEqual(kilogramsPerSquareMeter, remapped.Cells[i].TracerKilogramsPerSquareMeter,
                kilogramsPerSquareMeter * 5e-4);
        }
    }

    [TestMethod]
    public void Remap_IdenticalPartitions_IsExactIdentityWithoutMaterialMovement()
    {
        var catalog = PlanetCrustParcelCatalog.Create(24061984);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var geometry = PlanetCrustSphericalControlVolumes.Build(catalog);
        var regenerated = PlanetCrustSphericalControlVolumes.BuildAtEpoch(
            catalog, history, PlanetGeologicalEpoch.Present);
        var tracer = catalog.Parcels.Select((material, i) => i % 17 == 0
            ? material.ReferenceMassKilograms * 0.03 : 0.0).ToArray();

        var result = new PlanetCrustEpochConservativeRemapper().Remap(
            catalog, history, geometry, regenerated, tracer);
        Assert.AreEqual(0.0, result.WorstGeographicAreaMismatchFraction);
        Assert.AreEqual(catalog.Parcels.Count, result.Transfers.Count);
        for (var i = 0; i < tracer.Length; i++)
        {
            var transfer = result.Transfers[i];
            Assert.AreEqual(catalog.Parcels[i].Id, transfer.FromCell);
            Assert.AreEqual(transfer.FromCell, transfer.ToCell);
            Assert.AreEqual(tracer[i], transfer.TransferredTracerKilograms);
            Assert.AreEqual(tracer[i], result.Cells[i].TracerKilograms);
        }

        Assert.AreEqual(result.OriginalTracerKilograms, result.FinalTracerKilograms);
    }

    [TestMethod]
    public void Remap_LocalizedTracer_TransfersAcrossNewCellsWithoutModifyingCrustReferenceMass()
    {
        var (catalog, history, previous, next) = CreateEpochs(24061984, 40_000_000);
        var source = catalog.Parcels.Select((_, i) => i % 71 == 0 ? 1e12 : 0.0).ToArray();
        var originalMass = catalog.TotalReferenceMassKilograms;
        var remap = new PlanetCrustEpochConservativeRemapper().Remap(catalog, history, previous, next, source);

        Assert.AreEqual(source.Sum(), remap.FinalTracerKilograms, source.Sum() * 1e-12);
        Assert.IsTrue(remap.Transfers.Any(transfer =>
            transfer.FromCell != transfer.ToCell && transfer.TransferredTracerKilograms > 0.0));
        Assert.AreEqual(originalMass, catalog.TotalReferenceMassKilograms);
        Assert.AreEqual(originalMass, catalog.Parcels.Sum(material => material.ReferenceMassKilograms));
        Assert.AreEqual(0, history.Transfers.Count);
    }

    [TestMethod]
    public void Remap_SequentialEpochs_ReplaysDeterministicallyAndConservesPassiveTracer()
    {
        var catalog = PlanetCrustParcelCatalog.Create(24061984);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var first = PlanetCrustSphericalControlVolumes.Build(catalog);
        var second = PlanetCrustSphericalControlVolumes.BuildAtEpoch(
            catalog, history, new PlanetGeologicalEpoch(1_000_000));
        var third = PlanetCrustSphericalControlVolumes.BuildAtEpoch(
            catalog, history, new PlanetGeologicalEpoch(2_000_000));
        var tracer = first.Cells.Select((cell, i) =>
            cell.GeographicAreaSquareMeters * (i % 13 == 0 ? 3_000.0 : 300.0)).ToArray();
        var solver = new PlanetCrustEpochConservativeRemapper();
        var firstStep = solver.Remap(catalog, history, first, second, tracer);
        var secondStep = solver.Remap(catalog, history, second, third,
            firstStep.Cells.Select(cell => cell.TracerKilograms).ToArray());
        var replay = solver.Remap(catalog, history, first, second, tracer);

        CollectionAssert.AreEqual(firstStep.Transfers.ToArray(), replay.Transfers.ToArray());
        CollectionAssert.AreEqual(firstStep.Cells.ToArray(), replay.Cells.ToArray());
        Assert.AreEqual(firstStep.FinalTracerKilograms, secondStep.FinalTracerKilograms,
            firstStep.FinalTracerKilograms * 1e-12);
        Assert.AreEqual(tracer.Sum(), secondStep.FinalTracerKilograms, tracer.Sum() * 1e-12);
        Assert.AreEqual(second.Epoch, secondStep.FromEpoch);
        Assert.AreEqual(third.Epoch, secondStep.ToEpoch);
        Assert.AreEqual(catalog.TotalReferenceMassKilograms,
            catalog.Parcels.Sum(material => material.ReferenceMassKilograms));
    }

    [TestMethod]
    public void Remap_AlternateOwnerHistoryOrWrongWorldOrBadTracer_RejectsBeforeMovingMass()
    {
        var (catalog, history, previous, next) = CreateEpochs(24061984, 1_000_000);
        var tracer = previous.Cells.Select(cell => cell.GeographicAreaSquareMeters).ToArray();
        var other = PlanetCrustParcelCatalog.Create(42);
        var solver = new PlanetCrustEpochConservativeRemapper();

        Assert.ThrowsExactly<ArgumentException>(() => solver.Remap(catalog,
            PlanetCrustOwnershipHistory.Create(other), previous, next, tracer));
        Assert.ThrowsExactly<ArgumentException>(() => solver.Remap(catalog, history,
            next, previous, tracer));
        Assert.ThrowsExactly<ArgumentException>(() => solver.Remap(catalog, history,
            previous, next, tracer[..^1]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => solver.Remap(catalog, history,
            previous, next, tracer.Select((value, i) => i == 0 ? double.NaN : value).ToArray()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => solver.Remap(catalog, history,
            previous, next, tracer.Select((value, i) => i == 1 ? -0.1 : value).ToArray()));

        var accepted = history.WithTransfer(
            history.SuggestTransfers(new PlanetGeologicalEpoch(1_000_000)).First().Transfer);
        Assert.ThrowsExactly<ArgumentException>(() => solver.Remap(catalog, accepted, previous, next, tracer));
    }

    [TestMethod]
    public void Remap_NeverChangesTheLiveCanonicalElevationSource()
    {
        const int seed = 24061984;
        var (catalog, history, previous, next) = CreateEpochs(seed, 1_000_000);
        var source = new ProceduralPlanetElevationSource();
        var direction = PlanetVector.Normalize(new PlanetVector(-0.34, 0.59, 0.73));
        var baseline = source.SampleElevationMeters(direction, seed);
        _ = new PlanetCrustEpochConservativeRemapper().Remap(catalog, history,
            previous, next, previous.Cells.Select(cell => cell.GeographicAreaSquareMeters).ToArray());
        Assert.AreEqual(baseline, source.SampleElevationMeters(direction, seed));
    }

    private static (
        PlanetCrustParcelCatalog Catalog,
        PlanetCrustOwnershipHistory History,
        PlanetCrustSphericalControlVolumes Previous,
        PlanetCrustSphericalControlVolumes Next)
        CreateEpochs(int seed, long yearsBeforePresent)
    {
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        return (catalog, history, PlanetCrustSphericalControlVolumes.Build(catalog),
            PlanetCrustSphericalControlVolumes.BuildAtEpoch(catalog, history,
                new PlanetGeologicalEpoch(yearsBeforePresent)));
    }
}
