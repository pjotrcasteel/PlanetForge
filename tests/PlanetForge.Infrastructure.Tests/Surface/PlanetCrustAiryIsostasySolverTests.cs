using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetCrustAiryIsostasySolverTests
{
    [TestMethod]
    [DataRow(24061984)]
    [DataRow(346147916)]
    [DataRow(579460630)]
    public void Evaluate_ActualWorldMassExchange_ClosesColumnPressureBalanceAndConservesMaterial(int seed)
    {
        var deformation = CreateDeformation(seed, 0.5);
        var solution = new PlanetCrustAiryIsostasySolver().Evaluate(deformation);

        Assert.AreEqual(deformation.World, solution.World);
        Assert.AreEqual(deformation.Epoch, solution.Epoch);
        Assert.AreEqual(deformation.Columns.Count, solution.Columns.Count);
        Assert.AreEqual(deformation.ReferenceMassKilograms, solution.OriginalMaterialMassKilograms);
        Assert.AreEqual(deformation.TotalFinalMassKilograms, solution.BalancedMaterialMassKilograms);
        Assert.AreEqual(solution.OriginalMaterialMassKilograms, solution.BalancedMaterialMassKilograms,
            solution.OriginalMaterialMassKilograms * 1e-12);
        Assert.IsTrue(solution.Columns.Any(x => Math.Abs(x.SurfaceDisplacementMeters) > 1e-8));

        for (var i = 0; i < solution.Columns.Count; i++)
        {
            var displacement = solution.Columns[i];
            var material = deformation.Columns[i].State.Material;
            Assert.AreEqual(material.Id, displacement.MaterialId);
            Assert.AreEqual(material.Composition, displacement.Composition);
            Assert.AreEqual(material.Composition == PlanetCrustComposition.Oceanic
                ? PlanetCrustAiryIsostasySolver.DefaultSeawaterDensityKilogramsPerCubicMeter : 0.0,
                displacement.CoverDensityKilogramsPerCubicMeter);
            Assert.AreEqual(displacement.SolidThicknessChangeMeters,
                displacement.SurfaceDisplacementMeters + displacement.MantleRootChangeMeters, 1e-8);
            Assert.IsTrue(double.IsFinite(displacement.SurfaceDisplacementMeters));
            Assert.IsTrue(double.IsFinite(displacement.MantleRootChangeMeters));
            Assert.IsLessThan(1e-6, Math.Abs(displacement.PressureBalanceResidualKilogramsPerSquareMeter));
        }
    }

    [TestMethod]
    public void ComputeColumn_CompressionAndExtension_ProduceOppositeSurfaceAndRootChanges()
    {
        var catalog = PlanetCrustParcelCatalog.Create(42);
        var continental = catalog.Parcels.First(x => x.Composition == PlanetCrustComposition.Continental);
        var oceanic = catalog.Parcels.First(x => x.Composition == PlanetCrustComposition.Oceanic);

        foreach (var material in new[] { continental, oceanic })
        {
            foreach (var strain in new[] { -0.08, 0.08 })
            {
                var column = CreateSyntheticColumn(material, strain);
                var cover = material.Composition == PlanetCrustComposition.Oceanic ? 1_030.0 : 0.0;
                var response = PlanetCrustAiryIsostasySolver.ComputeColumn(column, 3_300.0, cover);
                var deltaT = column.ThicknessMeters - material.ReferenceThicknessMeters;
                var expected = deltaT * (3_300.0 - material.ReferenceDensityKilogramsPerCubicMeter) /
                    (3_300.0 - cover);

                Assert.AreEqual(expected, response.SurfaceDisplacementMeters, 1e-8);
                Assert.AreEqual(deltaT - expected, response.MantleRootChangeMeters, 1e-8);
                Assert.AreEqual(strain < 0, response.SurfaceDisplacementMeters > 0);
                Assert.AreEqual(strain < 0, response.MantleRootChangeMeters > 0);
                Assert.IsLessThan(1e-6, Math.Abs(response.PressureBalanceResidualKilogramsPerSquareMeter));
            }
        }
    }

    [TestMethod]
    public void ComputeColumn_WaterCover_BalancesGreaterUpliftForSameMaterialColumn()
    {
        var catalog = PlanetCrustParcelCatalog.Create(42);
        var material = catalog.Parcels.First(x => x.Composition == PlanetCrustComposition.Continental);
        var compressed = CreateSyntheticColumn(material, -0.08);
        var air = PlanetCrustAiryIsostasySolver.ComputeColumn(compressed, 3_300, 0);
        var submerged = PlanetCrustAiryIsostasySolver.ComputeColumn(compressed, 3_300, 1_030);

        Assert.IsGreaterThan(air.SurfaceDisplacementMeters, submerged.SurfaceDisplacementMeters);
        Assert.IsLessThan(air.MantleRootChangeMeters, submerged.MantleRootChangeMeters);
        Assert.IsLessThan(1e-6, Math.Abs(submerged.PressureBalanceResidualKilogramsPerSquareMeter));
    }

    [TestMethod]
    public void Evaluate_ZeroDuration_HasNoAppreciableHeightOrRootDelta()
    {
        var deformation = CreateDeformation(24061984, 0.0);
        var response = new PlanetCrustAiryIsostasySolver().Evaluate(deformation);
        foreach (var column in response.Columns)
        {
            Assert.AreEqual(0.0, column.SurfaceDisplacementMeters, 1e-8);
            Assert.AreEqual(0.0, column.MantleRootChangeMeters, 1e-8);
        }

        var field = new PlanetCrustIsostaticReliefField(response);
        foreach (var column in response.Columns.Where((_, i) => i % 53 == 0))
        {
            Assert.AreEqual(0.0, field.SampleUnpublishedDisplacementMeters(column.GeographicDirection), 1e-8);
        }
    }

    [TestMethod]
    public void Evaluate_RecreatedWorld_ReplaysIdenticalIsostaticColumnsAndGlobalField()
    {
        const int seed = 24061984;
        var left = new PlanetCrustAiryIsostasySolver().Evaluate(CreateDeformation(seed, 0.5, 20_000_000));
        var right = new PlanetCrustAiryIsostasySolver().Evaluate(CreateDeformation(seed, 0.5, 20_000_000));

        CollectionAssert.AreEqual(left.Columns.ToArray(), right.Columns.ToArray());
        var fieldA = new PlanetCrustIsostaticReliefField(left);
        var fieldB = new PlanetCrustIsostaticReliefField(right);
        foreach (var column in left.Columns.Where((_, i) => i % 31 == 0))
        {
            Assert.AreEqual(fieldA.SampleUnpublishedDisplacementMeters(column.GeographicDirection),
                fieldB.SampleUnpublishedDisplacementMeters(column.GeographicDirection));
        }
    }

    [TestMethod]
    public void ReliefField_CrossRegionAndCrossLodLocations_StaySmoothAndSeedIsolated()
    {
        const int seed = 24061984;
        var solution = new PlanetCrustAiryIsostasySolver().Evaluate(CreateDeformation(seed, 0.5));
        var field = new PlanetCrustIsostaticReliefField(solution);
        var source = new ProceduralPlanetElevationSource();
        var isolated = new PlanetCrustIsostaticResearchElevationSource(source, field);

        foreach (var column in solution.Columns.Where((_, i) => i % 41 == 0))
        {
            var direction = column.GeographicDirection;
            var shifted = PlanetVector.Normalize(direction + PlanetVector.UnitY *
                (20.0 / PlanetTectonicPlateKinematics.NominalPlanetRadiusMeters));
            var a = field.SampleUnpublishedDisplacementMeters(direction);
            var b = field.SampleUnpublishedDisplacementMeters(shifted);
            Assert.IsTrue(double.IsFinite(a));
            Assert.IsLessThan(5.0, Math.Abs(a - b));
            Assert.AreEqual(source.SampleElevationMeters(direction, seed) + a, isolated.SampleElevationMeters(direction, seed));
            Assert.AreEqual(source.SampleElevationMeters(direction, seed + 1),
                isolated.SampleElevationMeters(direction, seed + 1));
        }

        var sample = PlanetVector.Normalize(new PlanetVector(0.62, -0.38, 0.69));
        var baseline = source.SampleElevationMeters(sample, seed);
        _ = isolated.SampleElevationMeters(sample, seed);
        Assert.AreEqual(baseline, source.SampleElevationMeters(sample, seed),
            "Opt-in research source cannot change the canonical elevation provider.");
    }

    [TestMethod]
    public void ComputeColumn_InvalidPhysicalDensitiesAndBrokenLedger_RejectUnsafeCompensation()
    {
        var material = PlanetCrustParcelCatalog.Create(42).Parcels[0];
        var column = CreateSyntheticColumn(material, -0.02);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetCrustAiryIsostasySolver.ComputeColumn(column, material.ReferenceDensityKilogramsPerCubicMeter, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetCrustAiryIsostasySolver.ComputeColumn(column, 3_300, 3_300));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetCrustAiryIsostasySolver.ComputeColumn(column, 3_300, -10));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetCrustAiryIsostasySolver.ComputeColumn(column, double.NaN, 0));
        Assert.ThrowsExactly<ArgumentException>(() =>
            PlanetCrustAiryIsostasySolver.ComputeColumn(column with { TotalMassKilograms = column.TotalMassKilograms * 2 },
                3_300, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlanetCrustAiryIsostasySolver().Evaluate(CreateDeformation(42, 0.1), 3_300, 3_500));
    }

    private static PlanetCrustExchangedColumn CreateSyntheticColumn(PlanetCrustMaterialParcel material, double strain)
    {
        var state = new PlanetCrustMaterialState(material, PlanetGeologicalEpoch.Present,
            material.InitialPlate, material.PresentDirection);
        var area = material.ReferenceAreaSquareMeters * Math.Exp(strain);
        var thickness = material.ReferenceMassKilograms / (area * material.ReferenceDensityKilogramsPerCubicMeter);
        return new PlanetCrustExchangedColumn(state, area, material.ReferenceMassKilograms,
            0, 0, material.ReferenceMassKilograms, thickness, strain);
    }

    private static PlanetCrustDeformationSnapshot CreateDeformation(int seed, double duration, long yearsBeforePresent = 0)
    {
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var graph = PlanetCrustMaterialNeighborhood.Build(catalog);
        var history = PlanetCrustOwnershipHistory.Create(catalog);
        var epoch = new PlanetGeologicalEpoch(yearsBeforePresent);
        var strain = new PlanetCrustContactStrainSolver().Evaluate(catalog, history, graph, epoch, duration);
        return new PlanetCrustMassExchangeSolver().Exchange(catalog, graph, strain);
    }
}
