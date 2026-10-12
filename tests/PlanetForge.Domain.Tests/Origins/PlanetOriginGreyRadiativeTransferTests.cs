using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginGreyRadiativeTransferTests
{
    private const double Radius = 1.0;
    private const double Sigma = PlanetRadiationBalanceModel.StefanBoltzmannWattsPerSquareMeterKelvinFourth;

    [TestMethod]
    public void Calculate_TransparentAtmosphere_RecoversSurfaceBlackbodyAndNoAtmosphericExchange()
    {
        var flux = Calculate(surfaceKelvin: 1_500.0, atmosphereKelvin: 300.0, opticalDepth: 0.0);
        var expected = 4.0 * Math.PI * Sigma * Math.Pow(1_500.0, 4.0);

        Assert.AreEqual(1.0, flux.HemisphericSurfaceTransmittance, 0.0);
        Assert.AreEqual(expected, flux.SurfaceTransmittedToSpaceWatts, 1e-6);
        Assert.AreEqual(expected, flux.TotalOutgoingToSpaceWatts, 1e-6);
        Assert.AreEqual(0.0, flux.AtmosphereAbsorbedSurfacePowerWatts, 0.0);
        Assert.AreEqual(0.0, flux.AtmosphereEmittedUpWatts, 0.0);
        Assert.AreEqual(0.0, flux.AtmosphereEmittedDownWatts, 0.0);
        Assert.AreEqual(0.0, flux.AtmosphereNetCoolingWatts, 0.0);
        Assert.AreEqual(0.0, flux.PlanetConservationResidualWatts, 1e-6);
    }

    [TestMethod]
    public void Calculate_OpticalDepthOne_MatchesRadiativeTransferExponentialIntegralReference()
    {
        var flux = Calculate(surfaceKelvin: 1_200.0, atmosphereKelvin: 800.0, opticalDepth: 1.0);
        // Reference: 2 E3(1) = 0.2193839343955204 for the exact
        // plane-parallel, purely absorbing isothermal formal solution.
        Assert.AreEqual(0.2193839343955204, flux.HemisphericSurfaceTransmittance, 1e-7);
        Assert.IsGreaterThan(0.0, flux.AtmosphereAbsorbedSurfacePowerWatts);
        Assert.IsGreaterThan(0.0, flux.AtmosphereEmittedUpWatts);
        Assert.AreEqual(flux.AtmosphereEmittedUpWatts, flux.AtmosphereEmittedDownWatts, 0.0);
        Assert.AreEqual(0.0, flux.PlanetConservationResidualWatts, 1e-6);
    }

    [TestMethod]
    public void Calculate_OpaqueCoolAtmosphere_EmitsFromGasInsteadOfHotSurface()
    {
        var flux = Calculate(surfaceKelvin: 2_000.0, atmosphereKelvin: 700.0, opticalDepth: 100.0);
        var layerBlackbody = 4.0 * Math.PI * Sigma * Math.Pow(700.0, 4.0);

        Assert.IsLessThan(1e-35, flux.HemisphericSurfaceTransmittance);
        Assert.AreEqual(layerBlackbody, flux.TotalOutgoingToSpaceWatts, 1e-6);
        Assert.AreEqual(flux.SurfaceEmittedPowerWatts, flux.AtmosphereAbsorbedSurfacePowerWatts, 1e-6);
        Assert.AreEqual(0.0, flux.PlanetConservationResidualWatts, 1e-6);
    }

    [TestMethod]
    public void Calculate_IsothermalAtmosphere_OutputIsBlackbodyAtAnyOpticalDepth()
    {
        foreach (var depth in new[] { 0.0, 0.01, 0.1, 1.0, 10.0, 100.0 })
        {
            var flux = Calculate(900.0, 900.0, depth);
            var expected = 4.0 * Math.PI * Sigma * Math.Pow(900.0, 4.0);
            Assert.AreEqual(expected, flux.TotalOutgoingToSpaceWatts, expected * 1e-12);
            Assert.AreEqual(0.0, flux.PlanetConservationResidualWatts, expected * 1e-12);
        }
    }

    [TestMethod]
    public void Calculate_HotAtmosphere_CanHeatSurfaceWithoutViolatingPlanetaryEnergy()
    {
        var flux = Calculate(surfaceKelvin: 500.0, atmosphereKelvin: 1_000.0, opticalDepth: 10.0);

        Assert.IsGreaterThan(0.0, flux.AtmosphereNetCoolingWatts);
        Assert.IsLessThan(0.0, flux.SurfaceNetCoolingWatts);
        Assert.IsGreaterThan(0.0, flux.TotalOutgoingToSpaceWatts);
        Assert.AreEqual(flux.TotalOutgoingToSpaceWatts,
            flux.SurfaceNetCoolingWatts + flux.AtmosphereNetCoolingWatts, 1e-6);
    }

    [TestMethod]
    public void Calculate_InvalidGeometryTemperaturesOrOpticalDepth_RejectsUnsafeBoundary()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginGreyRadiativeTransfer.Calculate(new(-1.0, 500.0, 400.0, 1.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginGreyRadiativeTransfer.Calculate(new(Radius, double.NaN, 400.0, 1.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginGreyRadiativeTransfer.Calculate(new(Radius, 500.0, -400.0, 1.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginGreyRadiativeTransfer.Calculate(new(Radius, 500.0, 400.0, -1.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginGreyRadiativeTransfer.Calculate(new(Radius, 500.0, 400.0, double.PositiveInfinity)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginGreyRadiativeTransfer.Calculate(new(Radius, 1e120, 400.0, 1.0)));
    }

    private static PlanetOriginGreyRadiativeFlux Calculate(
        double surfaceKelvin, double atmosphereKelvin, double opticalDepth) =>
        PlanetOriginGreyRadiativeTransfer.Calculate(
            new PlanetOriginGreyRadiativeColumn(Radius, surfaceKelvin, atmosphereKelvin, opticalDepth));
}
