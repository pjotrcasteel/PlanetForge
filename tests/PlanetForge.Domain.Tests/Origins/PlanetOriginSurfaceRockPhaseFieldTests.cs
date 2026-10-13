using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginSurfaceRockPhaseFieldTests
{
    private const double RadiusMeters = 6_371_000.0;
    private const double RockMassKilograms = 1.0e19;
    private const double HeatCapacity = 850.0;
    private const double SolidusKelvin = 1_300.0;
    private const double LiquidusKelvin = 1_900.0;
    private const double LatentHeat = 400_000.0;
    private const double InitialTemperatureKelvin = 1_200.0;
    private static PlanetOriginSurfaceRockPhaseMaterial Material =>
        new(HeatCapacity, LatentHeat, SolidusKelvin, LiquidusKelvin);

    [TestMethod]
    public void FromSolidCanonicalSurface_InitiallySolid_ExactlyReusesExistingEnergyAndMaterialMass()
    {
        var ledger = CreateLedger();
        var field = PlanetOriginSurfaceRockPhaseField.FromSolidCanonicalSurface(
            ledger, new PlanetSurfaceGridLayout(2), RadiusMeters, Material);
        var originalEnergy = ledger.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock);

        Assert.AreEqual(originalEnergy, field.TotalStoredEnthalpyJoules, originalEnergy * 1e-12);
        Assert.AreEqual(RockMassKilograms, field.TotalActiveRockMassKilograms, RockMassKilograms * 1e-12);
        foreach (var cell in field.Cells)
        {
            var phase = cell.Resolve(field.Material);
            Assert.AreEqual(0.0, phase.MeltFraction, 0.0);
            Assert.AreEqual(InitialTemperatureKelvin, phase.TemperatureKelvin, 1e-9);
            Assert.AreEqual(phase.StoredEnergyJoules, cell.StoredEnthalpyJoules, 0.0);
        }
    }

    [TestMethod]
    public void Propose_HeatingThroughSolidus_StoresLatentEnergyAndResolvesPartialMelt()
    {
        var ledger = CreateLedger();
        var field = PlanetOriginSurfaceRockPhaseField.FromSolidCanonicalSurface(
            ledger, new PlanetSurfaceGridLayout(2), RadiusMeters, Material);
        const double targetKelvin = 1_600.0;
        var duration = PlanetOriginDuration.FromSeconds(1.0);
        var expectedExtra = RockMassKilograms * (SpecificEnthalpyAt(targetKelvin) - SpecificEnthalpyAt(InitialTemperatureKelvin));
        var globalArea = 4.0 * Math.PI * RadiusMeters * RadiusMeters;
        var stellarFlux = expectedExtra / globalArea;
        var trial = field.Propose(duration, (_, _) => new PlanetOriginSurfaceThermalRates(0.0, stellarFlux, 0.0));

        Assert.AreEqual(expectedExtra, trial.AbsorbedStellarJoules, expectedExtra * 1e-12);
        Assert.AreEqual(0.0, trial.ConservationResidualJoules, trial.Candidate.TotalStoredEnthalpyJoules * 1e-12);
        Assert.AreEqual(0.0, trial.InteriorConductionJoules, 0.0);
        Assert.AreEqual(0.0, trial.OutgoingInfraredJoules, 0.0);
        foreach (var cell in trial.Candidate.Cells)
        {
            var phase = cell.Resolve(Material);
            Assert.AreEqual(targetKelvin, phase.TemperatureKelvin, 1e-8);
            Assert.AreEqual(0.5, phase.MeltFraction, 1e-12);
        }

        Assert.AreEqual(InitialTemperatureKelvin, field.Cells[0].Resolve(Material).TemperatureKelvin, 1e-9);
        Assert.AreEqual(InitialTemperatureKelvin,
            ledger.Temperatures.SurfaceRockTemperatureKelvin!.Value, 1e-9);
    }

    [TestMethod]
    public void Propose_CoolingAcrossLiquidusAndSolidus_RecoversLatentHeatBeforeSolidRock()
    {
        var initial = PlanetOriginSurfaceRockPhaseField.FromSolidCanonicalSurface(
            CreateLedger(), new PlanetSurfaceGridLayout(1), RadiusMeters, Material);
        var heated = ChangeUniformTemperature(initial, InitialTemperatureKelvin, 2_000.0, heating: true);
        foreach (var cell in heated.Cells)
        {
            Assert.AreEqual(1.0, cell.Resolve(Material).MeltFraction, 0.0);
        }

        var partiallyCooled = ChangeUniformTemperature(heated, 2_000.0, 1_600.0, heating: false);
        foreach (var cell in partiallyCooled.Cells)
        {
            Assert.AreEqual(0.5, cell.Resolve(Material).MeltFraction, 1e-12);
            Assert.AreEqual(1_600.0, cell.Resolve(Material).TemperatureKelvin, 1e-8);
        }

        var solid = ChangeUniformTemperature(partiallyCooled, 1_600.0, 1_100.0, heating: false);
        foreach (var cell in solid.Cells)
        {
            Assert.AreEqual(0.0, cell.Resolve(Material).MeltFraction, 0.0);
            Assert.AreEqual(1_100.0, cell.Resolve(Material).TemperatureKelvin, 1e-8);
        }
    }

    [TestMethod]
    public void FromSolidCanonicalSurface_HotSensibleOnlyReference_RejectsUnfundedLatentEnergy()
    {
        var ledger = CreateLedger(1_600.0);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PlanetOriginSurfaceRockPhaseField.FromSolidCanonicalSurface(
                ledger, new PlanetSurfaceGridLayout(1), RadiusMeters, Material));
        Assert.AreEqual(1_600.0, ledger.Temperatures.SurfaceRockTemperatureKelvin!.Value, 0.0);
    }

    [TestMethod]
    public void FromSolidCanonicalSurface_DifferentMaterialHeatCapacity_RejectsFalseTemperatureInterpretation()
    {
        var ledger = CreateLedger();
        var mismatched = Material with { SpecificHeatCapacityJoulesPerKilogramKelvin = HeatCapacity * 1.2 };
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PlanetOriginSurfaceRockPhaseField.FromSolidCanonicalSurface(
                ledger, new PlanetSurfaceGridLayout(1), RadiusMeters, mismatched));
        Assert.AreEqual(InitialTemperatureKelvin, ledger.Temperatures.SurfaceRockTemperatureKelvin!.Value, 0.0);
    }

    [TestMethod]
    public void Propose_InsufficientEnergyInvalidRatesOrCancellation_PreservesInput()
    {
        var field = PlanetOriginSurfaceRockPhaseField.FromSolidCanonicalSurface(
            CreateLedger(), new PlanetSurfaceGridLayout(1), RadiusMeters, Material);
        var first = field.Cells[0].StoredEnthalpyJoules;

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            field.Propose(PlanetOriginDuration.FromSeconds(60.0), (cell, _) =>
                new PlanetOriginSurfaceThermalRates(0.0, 0.0, cell.StoredEnthalpyJoules / cell.AreaSquareMeters)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            field.Propose(PlanetOriginDuration.FromSeconds(1.0),
                (_, _) => new PlanetOriginSurfaceThermalRates(double.PositiveInfinity, 0.0, 0.0)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            field.Propose(PlanetOriginDuration.FromSeconds(1.0),
                (_, _) => new PlanetOriginSurfaceThermalRates(1.0, 0.0, 0.0), cancellation.Token));

        Assert.AreEqual(first, field.Cells[0].StoredEnthalpyJoules, 0.0);
        Assert.AreEqual(InitialTemperatureKelvin, field.Cells[0].Resolve(Material).TemperatureKelvin, 1e-9);
    }

    private static PlanetOriginSurfaceRockPhaseField ChangeUniformTemperature(
        PlanetOriginSurfaceRockPhaseField field, double fromKelvin, double toKelvin, bool heating)
    {
        var difference = Math.Abs(SpecificEnthalpyAt(toKelvin) - SpecificEnthalpyAt(fromKelvin));
        var totalJoules = RockMassKilograms * difference;
        var flux = totalJoules / (4.0 * Math.PI * RadiusMeters * RadiusMeters);
        var trial = field.Propose(PlanetOriginDuration.FromSeconds(1.0),
            (_, _) => heating
                ? new PlanetOriginSurfaceThermalRates(0.0, flux, 0.0)
                : new PlanetOriginSurfaceThermalRates(0.0, 0.0, flux));
        Assert.AreEqual(0.0, trial.ConservationResidualJoules, field.TotalStoredEnthalpyJoules * 1e-12);
        return trial.Candidate;
    }

    private static double SpecificEnthalpyAt(double kelvin) =>
        new PlanetRockEnthalpyModel(Material.ToRockProperties(1.0)).CalculateStoredEnergyJoules(kelvin);

    private static PlanetOriginEnergyLedger CreateLedger(double initialKelvin = InitialTemperatureKelvin)
    {
        var rockModel = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(
            4.0e24, 1_200.0, 400_000.0, 1_300.0, 1_900.0));
        var timeline = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(rockModel.CalculateStoredEnergyJoules(1_750.0)), rockModel);
        var environment = new PlanetWaterEnvironmentalState(PlanetWaterInventory.Empty,
            new PlanetEnvironmentalEnergyBudget(0.0, 0.0, RockMassKilograms * HeatCapacity * initialKelvin));
        return new PlanetOriginEnergyLedger(timeline, environment,
            new PlanetEnvironmentHeatCapacityParameters(0.0, 1_000.0, 2_000.0, 4_200.0,
                RockMassKilograms, HeatCapacity));
    }
}
