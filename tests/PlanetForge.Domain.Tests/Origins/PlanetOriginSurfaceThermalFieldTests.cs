using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginSurfaceThermalFieldTests
{
    private const double RadiusMeters = 6_371_000.0;
    private const double RockMassKilograms = 1.0e19;
    private const double HeatCapacity = 850.0;
    private const double InitialKelvin = 1_600.0;

    [TestMethod]
    public void Partition_UniformPublishedSurface_ConservesExactCanonicalMassAndEnergyAcrossSphericalCells()
    {
        var source = CreateLedger();
        var layout = new PlanetSurfaceGridLayout(3);

        var field = PlanetOriginSurfaceThermalField.Partition(source, layout, RadiusMeters);
        var expectedEnergy = source.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock);
        var area = field.Cells.Sum(cell => cell.AreaSquareMeters);
        var expectedArea = 4.0 * Math.PI * RadiusMeters * RadiusMeters;

        Assert.AreEqual(layout.CellCount, field.Cells.Count);
        Assert.AreEqual(RockMassKilograms, field.TotalActiveRockMassKilograms, RockMassKilograms * 1e-12);
        Assert.AreEqual(expectedEnergy, field.TotalSensibleEnergyJoules, expectedEnergy * 1e-12);
        Assert.AreEqual(expectedArea, area, expectedArea * 1e-12);
        foreach (var cell in field.Cells)
        {
            Assert.AreEqual(InitialKelvin, cell.TemperatureKelvin, 1e-9);
            Assert.IsGreaterThan(0.0, cell.ActiveRockMassKilograms);
            Assert.IsGreaterThan(0.0, cell.AreaSquareMeters);
            Assert.AreEqual(cell.ActiveRockMassKilograms * HeatCapacity, cell.HeatCapacityJoulesPerKelvin, 1e-3);
        }

        Assert.AreEqual(expectedEnergy, source.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock), 0.0);
    }

    [TestMethod]
    public void Propose_UniformEnergyRates_ConservesSourcesAndLossWithAnUnchangedCanonicalSource()
    {
        var ledger = CreateLedger();
        var field = PlanetOriginSurfaceThermalField.Partition(ledger, new PlanetSurfaceGridLayout(2), RadiusMeters);
        var initial = field.TotalSensibleEnergyJoules;
        var trial = field.Propose(PlanetOriginDuration.FromSeconds(60.0), _ => new PlanetOriginSurfaceThermalRates(4.0, 200.0, 900.0));

        Assert.IsLessThan(initial, trial.Candidate.TotalSensibleEnergyJoules);
        Assert.AreEqual(0.0, trial.ConservationResidualJoules, initial * 1e-12);
        Assert.AreEqual(initial, field.TotalSensibleEnergyJoules, 0.0);
        Assert.AreEqual(InitialKelvin, field.Cells[0].TemperatureKelvin, 1e-9);
        Assert.IsLessThan(InitialKelvin, trial.Candidate.Cells[0].TemperatureKelvin);
        Assert.AreEqual(initial, ledger.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock), 0.0);
        Assert.AreEqual(trial.Candidate.TotalSensibleEnergyJoules,
            initial + trial.InteriorConductionJoules + trial.AbsorbedStellarJoules - trial.OutgoingInfraredJoules,
            initial * 1e-12);
    }

    [TestMethod]
    public void Propose_StellarFacingHemisphere_OnlyIlluminatedCellsReceiveHeat()
    {
        var field = PlanetOriginSurfaceThermalField.Partition(CreateLedger(), new PlanetSurfaceGridLayout(2), RadiusMeters);
        var sunlight = new PlanetVector(1.0, 0.0, 0.0);

        var trial = field.Propose(PlanetOriginDuration.FromSeconds(60.0), cell =>
        {
            var center = PlanetSurfaceGridGeometry.GetCenterDirection(cell.Id);
            var cosine = Math.Max(PlanetVector.Dot(center, sunlight), 0.0);
            return new PlanetOriginSurfaceThermalRates(0.0, 1_361.0 * cosine * 0.85, 0.0);
        });

        Assert.IsGreaterThan(0.0, trial.AbsorbedStellarJoules);
        Assert.AreEqual(0.0, trial.ConservationResidualJoules, field.TotalSensibleEnergyJoules * 1e-12);
        var daylight = trial.Candidate.Cells.Count(cell => cell.TemperatureKelvin > InitialKelvin);
        var night = trial.Candidate.Cells.Count(cell => cell.TemperatureKelvin == InitialKelvin);
        Assert.IsGreaterThan(0, daylight);
        Assert.IsGreaterThan(0, night);
        Assert.AreEqual(0.0, trial.OutgoingInfraredJoules, 0.0);
    }

    [TestMethod]
    public void Propose_OverdrawOrInvalidRates_RejectsWholeTrialAndPreservesSource()
    {
        var field = PlanetOriginSurfaceThermalField.Partition(CreateLedger(), new PlanetSurfaceGridLayout(1), RadiusMeters);
        var original = field.TotalSensibleEnergyJoules;

        Assert.ThrowsExactly<InvalidOperationException>(() => field.Propose(PlanetOriginDuration.FromSeconds(60.0),
            cell => new PlanetOriginSurfaceThermalRates(0.0, 0.0, cell.SensibleEnergyJoules / cell.AreaSquareMeters)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => field.Propose(PlanetOriginDuration.FromSeconds(1.0),
            _ => new PlanetOriginSurfaceThermalRates(0.0, -2.0, 0.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => field.Propose(PlanetOriginDuration.FromSeconds(1.0),
            _ => new PlanetOriginSurfaceThermalRates(double.NaN, 0.0, 0.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => field.Propose(new PlanetOriginDuration(0),
            _ => new PlanetOriginSurfaceThermalRates(1.0, 0.0, 0.0)));
        Assert.AreEqual(original, field.TotalSensibleEnergyJoules, 0.0);
        Assert.AreEqual(InitialKelvin, field.Cells[0].TemperatureKelvin, 1e-9);
    }

    [TestMethod]
    public void Propose_CancelledCandidate_RetainsAllOriginalCellEnergy()
    {
        var field = PlanetOriginSurfaceThermalField.Partition(CreateLedger(), new PlanetSurfaceGridLayout(2), RadiusMeters);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => field.Propose(PlanetOriginDuration.FromSeconds(30.0),
            _ => new PlanetOriginSurfaceThermalRates(0.0, 0.0, 100.0), cancellation.Token));
        Assert.AreEqual(RockMassKilograms, field.TotalActiveRockMassKilograms, RockMassKilograms * 1e-12);
        Assert.AreEqual(InitialKelvin, field.Cells[0].TemperatureKelvin, 1e-9);
    }

    [TestMethod]
    public void Partition_HugeGridOrAbsentRock_RejectsUnsupportedPhysicalInput()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginSurfaceThermalField.Partition(CreateLedger(), new PlanetSurfaceGridLayout(7), RadiusMeters));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginSurfaceThermalField.Partition(CreateLedger(), new PlanetSurfaceGridLayout(0), 0.0));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PlanetOriginSurfaceThermalField.Partition(CreateLedger(0.0), new PlanetSurfaceGridLayout(0), RadiusMeters));
    }

    private static PlanetOriginEnergyLedger CreateLedger(double surfaceMassKilograms = RockMassKilograms)
    {
        var rockModel = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(
            4.0e24, 1_200.0, 400_000.0, 1_300.0, 1_900.0));
        var timeline = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(rockModel.CalculateStoredEnergyJoules(1_750.0)), rockModel);
        var environment = new PlanetWaterEnvironmentalState(PlanetWaterInventory.Empty,
            new PlanetEnvironmentalEnergyBudget(0.0, 0.0, surfaceMassKilograms * HeatCapacity * InitialKelvin));
        var parameters = new PlanetEnvironmentHeatCapacityParameters(
            0.0, 1_000.0, 2_000.0, 4_200.0, surfaceMassKilograms, HeatCapacity);
        return new PlanetOriginEnergyLedger(timeline, environment, parameters);
    }
}
