using PlanetForge.Application.Planets;
using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Application.Tests.Planets;

[TestClass]
public sealed class PlanetFormationInitialHeatTests
{
    [TestMethod]
    public void Create_IntegritySeeds_PreservesFundedEnergyMassAndMoltenTemperatureBounds()
    {
        var uniform = PlanetMagmaOceanReference.CreateInitial(6371000.0, 0.0, 4);
        foreach (var seed in PlanetSeedCatalog.IntegritySeeds)
        {
            var region = PlanetFormationInitialHeat.Create(6371000.0, seed);
            var phases = region.Field.Cells.Select(cell => cell.Resolve(region.Field.Material)).ToArray();
            Assert.AreEqual(uniform.Field.TotalStoredEnthalpyJoules, region.Field.TotalStoredEnthalpyJoules, uniform.Field.TotalStoredEnthalpyJoules * 1e-12);
            Assert.AreEqual(uniform.Field.TotalActiveRockMassKilograms, region.Field.TotalActiveRockMassKilograms, 0.0);
            Assert.IsTrue(phases.All(phase => phase.MeltFraction == 1.0));
            Assert.IsTrue(phases.All(phase => phase.TemperatureKelvin >= PlanetFormationInitialHeat.MinimumInitialKelvin - 1e-6 &&
                phase.TemperatureKelvin <= PlanetFormationInitialHeat.MaximumInitialKelvin + 1e-6));
            Assert.IsTrue(phases.Max(phase => phase.TemperatureKelvin) - phases.Min(phase => phase.TemperatureKelvin) > 700.0);
            var mean = region.Field.Cells.Select(cell => cell.ActiveRockMassKilograms * cell.Resolve(region.Field.Material).TemperatureKelvin).Sum() /
                region.Field.TotalActiveRockMassKilograms;
            Assert.AreEqual(PlanetMagmaOceanReference.InitialSurfaceTemperatureKelvin, mean, 1e-8);
        }
    }

    [TestMethod]
    public void Create_SameAndDifferentSeeds_ReplaysExactlyAndChangesGeographicHeat()
    {
        var first = PlanetFormationInitialHeat.Create(6371000.0, 24061984);
        var replay = PlanetFormationInitialHeat.Create(6371000.0, 24061984);
        var other = PlanetFormationInitialHeat.Create(6371000.0, 104729);
        CollectionAssert.AreEqual(first.Field.Cells.ToArray(), replay.Field.Cells.ToArray());
        Assert.IsTrue(first.Field.Cells.Zip(other.Field.Cells).Any(pair => Math.Abs(pair.First.StoredEnthalpyJoules - pair.Second.StoredEnthalpyJoules) > 1.0));
    }

    [TestMethod]
    public void Cool_HeterogeneousShell_HasSolidSkinBesideMeltAndAgreesAcrossDepthRefinement()
    {
        var funded = PlanetFormationInitialHeat.Create(6371000.0, 24061984);
        var boundary = new PlanetOriginRegionalAirlessParameters(PlanetVector.UnitX, 1361.0, 0.15, 0.9, 3.0, 25.0);
        var rough = PlanetOriginRockColumnIntegrator.Advance(PlanetRockColumnReference.CreateResolvedInitial(funded, 32),
            PlanetOriginDuration.FromSeconds(180), boundary, new(0.25, 10000), CancellationToken.None);
        var coarse = PlanetOriginRockColumnIntegrator.Advance(PlanetRockColumnReference.CreateResolvedInitial(funded, 64),
            PlanetOriginDuration.FromSeconds(180), boundary, new(0.25, 10000), CancellationToken.None);
        var fine = PlanetOriginRockColumnIntegrator.Advance(PlanetRockColumnReference.CreateResolvedInitial(funded, 128),
            PlanetOriginDuration.FromSeconds(180), boundary, new(0.25, 10000), CancellationToken.None);
        var skin = fine.Field.Cells.Select(cell => fine.ResolveRadiatingSurfacePhase(cell.Id)).ToArray();
        Assert.IsTrue(skin.Any(phase => phase.MeltFraction == 0.0));
        Assert.IsTrue(skin.Any(phase => phase.MeltFraction > 0.2));
        var faceError = 0.0;
        var previousFaceError = 0.0;
        var frontError = 0.0;
        for (var index = 0; index < fine.Field.Cells.Count; index++)
        {
            var id = fine.Field.Cells[index].Id;
            previousFaceError = Math.Max(previousFaceError, Math.Abs(coarse.ResolveRadiatingSurfaceTemperature(id) - rough.ResolveRadiatingSurfaceTemperature(id)));
            faceError = Math.Max(faceError, Math.Abs(fine.ResolveRadiatingSurfaceTemperature(id) - coarse.ResolveRadiatingSurfaceTemperature(id)));
            frontError = Math.Max(frontError, Math.Abs(fine.Columns!.Columns[index].ReconstructSolidusDepthMeters(fine.Field.Material, fine.Columns.Properties, fine.ResolveRadiatingSurfaceTemperature(id)) -
                coarse.Columns!.Columns[index].ReconstructSolidusDepthMeters(coarse.Field.Material, coarse.Columns.Properties, coarse.ResolveRadiatingSurfaceTemperature(id))));
        }
        TestContext.WriteLine($"Matched differences: face={faceError:R} K; front={frontError:R} m.");
        Assert.IsTrue(faceError < previousFaceError, $"Refinement must reduce the face difference: {previousFaceError:R} to {faceError:R} K.");
        Assert.IsTrue(faceError < 10.0, $"Matched 64/128-layer maximum face difference {faceError:R} K.");
        Assert.IsTrue(frontError < 0.002, $"Matched 64/128-layer maximum solidus-depth difference {frontError:R} m.");
        Assert.AreEqual(0.0, fine.Epoch.EnergyConservationResidualJoules, fine.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
        TestContext.WriteLine($"Heterogeneous 64/128-layer face difference={faceError:R} K; solidus depth difference={frontError:R} m.");
    }

    public TestContext TestContext { get; set; } = null!;
}
