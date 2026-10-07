using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface.Hydrology;

[TestClass]
public sealed class PlanetCryosphereRunoffModelTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void Build_WarmIcyLand_ProducesMeltwaterRunoff()
    {
        var hydrology = new PlanetHydrologyModelBuilder(new FlatElevationSource()).Build(3, 42, EarthRadiusMeters, -1_000.0, CancellationToken.None);

        var result = new PlanetCryosphereRunoffModel().Build(hydrology, -1_000.0, 295.0, 0.65, 0.55, CancellationToken.None);

        Assert.IsTrue(result.MeanAnnualMeltwaterRunoffMillimeters > 0.0);
        Assert.IsTrue(result.PeakAnnualMeltwaterRunoffMillimeters >= result.MeanAnnualMeltwaterRunoffMillimeters);
        Assert.IsTrue(result.LocalAnnualMeltwaterRunoffMillimeters.Any(value => value > 0.0));
    }

    [TestMethod]
    public void Build_NoLandIceOrSnow_ProducesNoMeltwater()
    {
        var hydrology = new PlanetHydrologyModelBuilder(new FlatElevationSource()).Build(3, 42, EarthRadiusMeters, -1_000.0, CancellationToken.None);

        var result = new PlanetCryosphereRunoffModel().Build(hydrology, -1_000.0, 295.0, 0.0, 0.0, CancellationToken.None);

        Assert.AreEqual(0.0, result.MeanAnnualMeltwaterRunoffMillimeters, 0.000001);
        Assert.AreEqual(0.0, result.PeakAnnualMeltwaterRunoffMillimeters, 0.000001);
    }

    [TestMethod]
    public void Build_FrozenClimate_ProducesNoMeltwater()
    {
        var hydrology = new PlanetHydrologyModelBuilder(new FlatElevationSource()).Build(3, 42, EarthRadiusMeters, -1_000.0, CancellationToken.None);

        var result = new PlanetCryosphereRunoffModel().Build(hydrology, -1_000.0, 250.0, 1.0, 1.0, CancellationToken.None);

        Assert.AreEqual(0.0, result.MeanAnnualMeltwaterRunoffMillimeters, 0.000001);
    }

    [TestMethod]
    public void Build_PreCancelledToken_Throws()
    {
        var hydrology = new PlanetHydrologyModelBuilder(new FlatElevationSource()).Build(3, 42, EarthRadiusMeters, -1_000.0, CancellationToken.None);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            new PlanetCryosphereRunoffModel().Build(hydrology, -1_000.0, 295.0, 0.65, 0.55, cancellationTokenSource.Token));
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 1_000.0;
    }
}
