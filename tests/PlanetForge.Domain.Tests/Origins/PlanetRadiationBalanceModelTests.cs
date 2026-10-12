using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetRadiationBalanceModelTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void Calculate_EarthLikeReferenceBoundary_UsesCorrectCrossSectionAndSurfaceArea()
    {
        // Illustrative contemporary boundary numbers, not a claim about
        // Earth's Hadean albedo, stellar luminosity or greenhouse state.
        var balance = PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(EarthRadiusMeters, 1_361.0, 0.30, 1.0, 255.0));

        var area = 4.0 * Math.PI * EarthRadiusMeters * EarthRadiusMeters;
        Assert.AreEqual(area, balance.PlanetSurfaceAreaSquareMeters, area * 1e-12);
        Assert.AreEqual(238.175, balance.AbsorbedFluxWattsPerSquareMeter, 1e-10);
        Assert.AreEqual(239.76, balance.EmittedFluxWattsPerSquareMeter, 0.1);
        Assert.AreEqual(balance.AbsorbedFluxWattsPerSquareMeter * area, balance.AbsorbedStellarPowerWatts, area * 1e-10);
        Assert.AreEqual(balance.EmittedFluxWattsPerSquareMeter * area, balance.EmittedThermalPowerWatts, area * 1e-10);
        Assert.IsTrue(balance.NetPowerWatts < 0.0);
    }

    [TestMethod]
    public void Calculate_DoublingPlanetRadius_QuadruplesPowerButNotSurfaceFlux()
    {
        var reference = PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(1_000.0, 800.0, 0.2, 0.9, 200.0));
        var doubled = PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(2_000.0, 800.0, 0.2, 0.9, 200.0));

        Assert.AreEqual(reference.AbsorbedFluxWattsPerSquareMeter, doubled.AbsorbedFluxWattsPerSquareMeter, 0.0);
        Assert.AreEqual(reference.EmittedFluxWattsPerSquareMeter, doubled.EmittedFluxWattsPerSquareMeter, 0.0);
        Assert.AreEqual(4.0 * reference.PlanetSurfaceAreaSquareMeters, doubled.PlanetSurfaceAreaSquareMeters, 1e-6);
        Assert.AreEqual(4.0 * reference.AbsorbedStellarPowerWatts, doubled.AbsorbedStellarPowerWatts, 1e-5);
        Assert.AreEqual(4.0 * reference.EmittedThermalPowerWatts, doubled.EmittedThermalPowerWatts, 1e-5);
    }

    [TestMethod]
    public void Calculate_NoIncomingStarlight_WithThermalEmission_IsCoolingBoundary()
    {
        var result = PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(EarthRadiusMeters, 0.0, 0.5, 1.0, 300.0));

        Assert.AreEqual(0.0, result.AbsorbedStellarPowerWatts, 0.0);
        Assert.IsTrue(result.EmittedThermalPowerWatts > 0.0);
        Assert.IsTrue(result.NetPowerWatts < 0.0);
    }

    [TestMethod]
    public void Calculate_PerfectReflectionOrZeroEmissivity_RespectEnergyLimits()
    {
        var reflected = PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(EarthRadiusMeters, 1_000.0, 1.0, 1.0, 200.0));
        var nonemitting = PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(EarthRadiusMeters, 1_000.0, 0.0, 0.0, 300.0));

        Assert.AreEqual(0.0, reflected.AbsorbedStellarPowerWatts, 0.0);
        Assert.IsTrue(reflected.NetPowerWatts < 0.0);
        Assert.AreEqual(0.0, nonemitting.EmittedThermalPowerWatts, 0.0);
        Assert.IsTrue(nonemitting.NetPowerWatts > 0.0);
    }

    [TestMethod]
    public void Calculate_LowerStellarIrradiance_DecreasesAbsorbedPowerWithSamePlanet()
    {
        var olderStar = PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(EarthRadiusMeters, 1_000.0, 0.3, 1.0, 255.0));
        var reference = PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(EarthRadiusMeters, 1_361.0, 0.3, 1.0, 255.0));

        Assert.IsTrue(olderStar.AbsorbedStellarPowerWatts < reference.AbsorbedStellarPowerWatts);
        Assert.AreEqual(reference.EmittedThermalPowerWatts, olderStar.EmittedThermalPowerWatts, 0.0);
        Assert.IsTrue(olderStar.NetPowerWatts < reference.NetPowerWatts);
    }

    [TestMethod]
    public void Calculate_ZeroTemperatureAndNoStarlight_HasNoNetRadiativeExchange()
    {
        var result = PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(EarthRadiusMeters, 0.0, 0.0, 1.0, 0.0));

        Assert.AreEqual(0.0, result.AbsorbedStellarPowerWatts, 0.0);
        Assert.AreEqual(0.0, result.EmittedThermalPowerWatts, 0.0);
        Assert.AreEqual(0.0, result.NetPowerWatts, 0.0);
    }

    [TestMethod]
    public void Calculate_InvalidBoundaryConditions_AreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(0.0, 1_000.0, 0.3, 1.0, 250.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(EarthRadiusMeters, -1.0, 0.3, 1.0, 250.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(EarthRadiusMeters, 1_000.0, 1.1, 1.0, 250.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(EarthRadiusMeters, 1_000.0, 0.3, double.NaN, 250.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(EarthRadiusMeters, 1_000.0, 0.3, 1.0, -250.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(EarthRadiusMeters, 1_000.0, 0.3, 1.0, double.PositiveInfinity)));
    }

    [TestMethod]
    public void Calculate_NumericalOverflow_FailsInsteadOfPublishingInfiniteEnergy()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(double.MaxValue, 1_000.0, 0.3, 1.0, 250.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetRadiationBalanceModel.Calculate(
            new PlanetRadiationBoundaryConditions(EarthRadiusMeters, 1_000.0, 0.3, 1.0, 1.0e100)));
    }
}
