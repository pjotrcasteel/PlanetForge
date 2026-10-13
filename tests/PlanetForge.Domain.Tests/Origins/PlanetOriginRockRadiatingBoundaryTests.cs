using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginRockRadiatingBoundaryTests
{
    [TestMethod]
    [DataRow(2200.0, 3.0, 0.4, 0.0, 0.9)]
    [DataRow(900.0, 3.0, 0.001, 1361.0, 0.9)]
    [DataRow(300.0, 3.0, 0.1, 100000.0, 0.9)]
    public void ResolveTemperature_FiniteConductivity_ClosesIndependentFaceHeatBalance(
        double kelvin, double conductivity, double halfWidth, double solar, double emissivity)
    {
        var face = PlanetOriginRockRadiatingBoundary.ResolveTemperature(kelvin, conductivity, halfWidth, solar, emissivity);
        var conduction = conductivity * (kelvin - face) / halfWidth;
        var emission = emissivity * 5.670374419e-8 * Math.Pow(face, 4);
        Assert.AreEqual(emission, conduction + solar, Math.Max(1e-8, emission * 1e-12));
        Assert.IsTrue(face >= 0.0);
    }

    [TestMethod]
    public void ResolveTemperature_InsulatedFace_HasNoConductiveHeatExchange()
    {
        var face = PlanetOriginRockRadiatingBoundary.ResolveTemperature(2200.0, 0.0, 0.1, 1361.0, 0.9);
        Assert.AreEqual(1361.0, 0.9 * 5.670374419e-8 * Math.Pow(face, 4), 1e-8);
        Assert.AreEqual(0.0, PlanetOriginRockRadiatingBoundary.ResolveTemperature(2200.0, 0.0, 0.1, 0.0, 0.9), 0.0);
    }

    [TestMethod]
    public void ResolveTemperature_NoEmission_ConductsAllAbsorbedHeatIntoCell()
    {
        Assert.AreEqual(305.0, PlanetOriginRockRadiatingBoundary.ResolveTemperature(300.0, 2.0, 0.1, 100.0, 0.0), 0.0);
        Assert.ThrowsExactly<InvalidOperationException>(() => PlanetOriginRockRadiatingBoundary.ResolveTemperature(300.0, 0.0, 0.1, 100.0, 0.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetOriginRockRadiatingBoundary.ResolveTemperature(double.NaN, 3.0, 0.1, 0.0, 0.9));
    }
}
