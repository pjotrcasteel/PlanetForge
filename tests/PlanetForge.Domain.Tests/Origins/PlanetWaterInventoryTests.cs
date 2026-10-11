using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetWaterInventoryTests
{
    private const double ReferenceWaterMassKilograms = 1.4e21;

    [TestMethod]
    public void DeliverDegasCondenseFreezeAndEscape_ConservesExternalWaterBudget()
    {
        var primordial = PlanetWaterInventory.Empty;
        var afterAccretion = primordial.Deliver(PlanetWaterReservoir.HydratedMantle, ReferenceWaterMassKilograms);
        var mantleMelt = afterAccretion.Transfer(PlanetWaterReservoir.HydratedMantle,
            PlanetWaterReservoir.DissolvedInMagma, 1.0e21);
        var degassed = mantleMelt.Transfer(PlanetWaterReservoir.DissolvedInMagma,
            PlanetWaterReservoir.AtmosphericVapor, 8.0e20);
        var withOcean = degassed.Transfer(PlanetWaterReservoir.AtmosphericVapor,
            PlanetWaterReservoir.SurfaceLiquid, 7.0e20);
        var withIce = withOcean.Transfer(PlanetWaterReservoir.SurfaceLiquid,
            PlanetWaterReservoir.SurfaceIce, 1.0e20);
        var afterEscape = withIce.EscapeAtmosphericVapor(2.0e19);

        Assert.AreEqual(ReferenceWaterMassKilograms, afterEscape.CumulativeDeliveredMassKilograms, 0.0);
        Assert.AreEqual(2.0e19, afterEscape.CumulativeEscapedMassKilograms, 1e6);
        Assert.AreEqual(6.0e20, afterEscape.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 1e6);
        Assert.AreEqual(1.0e20, afterEscape.GetMassKilograms(PlanetWaterReservoir.SurfaceIce), 1e6);
        Assert.AreEqual(8.0e19, afterEscape.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 1e6);
        Assert.AreEqual(ReferenceWaterMassKilograms - 2.0e19, afterEscape.PlanetaryWaterMassKilograms, 1e7);
        Assert.IsLessThan(ReferenceWaterMassKilograms * 1e-12, Math.Abs(afterEscape.ConservationResidualKilograms),
            "All H2O must originate from an external delivery and every escaped kilogram must be accounted.");
        Assert.AreEqual(0.0, primordial.PlanetaryWaterMassKilograms, 0.0,
            "Past geological snapshots must remain immutable.");
        Assert.AreEqual(ReferenceWaterMassKilograms, afterAccretion.GetMassKilograms(PlanetWaterReservoir.HydratedMantle), 0.0);
    }

    [TestMethod]
    public void Transfer_WithoutAvailableSourceMass_RejectsSpontaneousWaterCreation()
    {
        var dry = PlanetWaterInventory.Empty;

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            dry.Transfer(PlanetWaterReservoir.AtmosphericVapor, PlanetWaterReservoir.SurfaceLiquid, 1.0));
        Assert.ThrowsExactly<InvalidOperationException>(() => dry.EscapeAtmosphericVapor(1.0));
        Assert.AreEqual(0.0, dry.CumulativeDeliveredMassKilograms, 0.0);
        Assert.AreEqual(0.0, dry.PlanetaryWaterMassKilograms, 0.0);
    }

    [TestMethod]
    public void Transfer_MagmaToVaporThenOcean_TracksTheSameWaterWithoutExtraExternalDelivery()
    {
        var inventory = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.DissolvedInMagma, 1_000.0);
        var rain = inventory.Transfer(PlanetWaterReservoir.DissolvedInMagma, PlanetWaterReservoir.AtmosphericVapor, 700.0)
            .Transfer(PlanetWaterReservoir.AtmosphericVapor, PlanetWaterReservoir.SurfaceLiquid, 600.0);

        Assert.AreEqual(1_000.0, rain.PlanetaryWaterMassKilograms, 1e-9);
        Assert.AreEqual(1_000.0, rain.CumulativeDeliveredMassKilograms, 1e-9);
        Assert.AreEqual(600.0, rain.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 1e-9);
        Assert.AreEqual(0.0, rain.CumulativeEscapedMassKilograms, 0.0);
        Assert.AreEqual(0.0, rain.ConservationResidualKilograms, 1e-9);
    }

    [TestMethod]
    public void DeliverAndTransfer_InvalidValues_AreRejectedWithoutMutatingInventory()
    {
        var empty = PlanetWaterInventory.Empty;

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => empty.Deliver(PlanetWaterReservoir.HydratedMantle, double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => empty.Deliver(PlanetWaterReservoir.HydratedMantle, -1.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => empty.Deliver((PlanetWaterReservoir)99, 1.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            empty.Transfer(PlanetWaterReservoir.HydratedMantle, (PlanetWaterReservoir)99, 0.0));
        Assert.ThrowsExactly<ArgumentException>(() =>
            empty.Transfer(PlanetWaterReservoir.HydratedMantle, PlanetWaterReservoir.HydratedMantle, 1.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => empty.EscapeAtmosphericVapor(double.PositiveInfinity));
        Assert.AreEqual(0.0, empty.PlanetaryWaterMassKilograms, 0.0);
    }

    [TestMethod]
    public void Deliver_Overflow_IsRejectedWithoutInventingInfiniteWater()
    {
        var inventory = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.HydratedMantle, double.MaxValue);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            inventory.Deliver(PlanetWaterReservoir.HydratedMantle, double.MaxValue));
        Assert.AreEqual(double.MaxValue, inventory.CumulativeDeliveredMassKilograms);
    }

    [TestMethod]
    public void ZeroTransfers_NeverAdvanceThePhysicalState()
    {
        var inventory = PlanetWaterInventory.Empty;

        Assert.AreSame(inventory, inventory.Deliver(PlanetWaterReservoir.HydratedMantle, 0.0));
        Assert.AreSame(inventory, inventory.Transfer(PlanetWaterReservoir.HydratedMantle, PlanetWaterReservoir.SurfaceLiquid, 0.0));
        Assert.AreSame(inventory, inventory.EscapeAtmosphericVapor(0.0));
    }
}
