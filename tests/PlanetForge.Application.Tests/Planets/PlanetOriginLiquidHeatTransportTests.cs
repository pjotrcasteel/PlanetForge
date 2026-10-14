using PlanetForge.Application.Planets;
using PlanetForge.Domain.Origins;

namespace PlanetForge.Application.Tests.Planets;

[TestClass]
public sealed class PlanetOriginLiquidHeatTransportTests
{
    private const double RadiusMeters = 6_371_000.0;

    [TestMethod]
    public void Advance_SeededNearLiquidus_ErrorControlMatchesFinePhysicalReference()
    {
        var funded = PlanetFormationInitialHeat.CreateUpperMagma(RadiusMeters, 24061984, gridLevel: 1);
        var initial = PlanetRockColumnReference.CreateCoupledUpperMagmaInitial(funded);
        var boundary = initial.SurfaceBoundary!.Value;
        var duration = PlanetOriginDuration.FromSeconds(1.0);
        var controlled = PlanetOriginRockColumnIntegrator.Advance(initial, duration, boundary,
            new(0.25, 10000) { MaximumThermalErrorKelvin = 0.1 });
        var reference = PlanetOriginRockColumnIntegrator.Advance(initial, duration, boundary, new(0.0005, 10000));
        var worstFaceDifference = initial.Field.Cells.Max(cell => Math.Abs(controlled.ResolveRadiatingSurfaceTemperature(cell.Id) -
            reference.ResolveRadiatingSurfaceTemperature(cell.Id)));
        Assert.IsLessThan(1.0, worstFaceDifference);
        var emitted = reference.Epoch.CoupledEnergy!.CumulativeEmittedInfraredEnergyJoules;
        Assert.IsLessThan(0.005, Math.Abs(controlled.Epoch.CoupledEnergy!.CumulativeEmittedInfraredEnergyJoules - emitted) / emitted);
        Assert.IsTrue(controlled.Field.Cells.All(cell => controlled.ResolveRadiatingSurfacePhase(cell.Id).MeltFraction == 1.0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => PlanetOriginRockColumnIntegrator.Advance(initial, duration, boundary,
            new(0.25, 10000) { MaximumThermalErrorKelvin = 0.1 }, cancellation.Token));
        Assert.AreEqual(0L, initial.Epoch.Revision);
    }

    [TestMethod]
    public void Advance_ErrorControlledLiquidCooling_ReachesExactTimeAndTighterReferenceWithoutPublishingTrials()
    {
        var initial = PlanetRockColumnReference.CreateUpperMagmaInitial(RadiusMeters, gridLevel: 0);
        var boundary = initial.SurfaceBoundary!.Value with { LiquidHeatTransport = PlanetOriginLiquidHeatTransportParameters.Reference };
        var loose = PlanetOriginRockColumnIntegrator.Advance(initial, PlanetOriginDuration.FromSeconds(1.0), boundary,
            new(0.25, 10000) { MaximumThermalErrorKelvin = 1.0 });
        var tight = PlanetOriginRockColumnIntegrator.Advance(initial, PlanetOriginDuration.FromSeconds(1.0), boundary,
            new(0.25, 10000) { MaximumThermalErrorKelvin = 0.1 });
        var reference = PlanetOriginRockColumnIntegrator.Advance(initial, PlanetOriginDuration.FromSeconds(1.0), boundary, new(0.001, 2000));
        var expected = reference.Epoch.CoupledEnergy!.CumulativeEmittedInfraredEnergyJoules;
        var looseError = Math.Abs(loose.Epoch.CoupledEnergy!.CumulativeEmittedInfraredEnergyJoules - expected);
        var tightError = Math.Abs(tight.Epoch.CoupledEnergy!.CumulativeEmittedInfraredEnergyJoules - expected);
        Assert.IsLessThan(looseError, tightError);
        Assert.IsLessThan(0.005, tightError / expected);
        Assert.AreEqual(1_000_000L, tight.Epoch.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(0L, initial.Epoch.Revision);
        Assert.AreEqual(0.0, tight.Epoch.EnergyConservationResidualJoules, tight.Epoch.CoupledEnergy.InitialStoredEnergyJoules * 1e-12);
        Assert.ThrowsExactly<InvalidOperationException>(() => PlanetOriginRockColumnIntegrator.Advance(initial,
            PlanetOriginDuration.FromSeconds(1.0), boundary, new(0.25, 1) { MaximumThermalErrorKelvin = 0.001 }));
        Assert.AreEqual(0L, initial.Epoch.Revision);
    }

    [TestMethod]
    public void LiquidHeatTransport_StableInitialLiquid_HasNoInventedConvectiveFlux()
    {
        var initial = PlanetRockColumnReference.CreateUpperMagmaInitial(RadiusMeters, gridLevel: 0);
        foreach (var column in initial.Columns!.Columns)
        {
            var diagnostic = PlanetOriginLiquidHeatTransport.Diagnose(column, initial.Field.Material, initial.Columns.Properties,
                PlanetOriginLiquidHeatTransportParameters.Reference);
            Assert.AreEqual(0, diagnostic.EnhancedInterfaceCount);
            Assert.AreEqual(1.0, diagnostic.MaximumConductivityMultiplier);
            Assert.AreEqual(1_000.0, diagnostic.ConnectedLiquidDepthMeters, 1e-8);
        }
    }

    [TestMethod]
    public void LiquidHeatTransport_CoolingCreatesInstability_ExchangesOwnedHeatAndExcludesSolidSkin()
    {
        var initial = PlanetRockColumnReference.CreateUpperMagmaInitial(RadiusMeters, gridLevel: 0);
        var boundary = initial.SurfaceBoundary!.Value;
        var cooled = initial.Advance(PlanetOriginDuration.FromSeconds(0.1), boundary);
        var column = cooled.Columns!.Columns[0];
        var closure = PlanetOriginLiquidHeatTransportParameters.Reference;
        var diagnostic = PlanetOriginLiquidHeatTransport.Diagnose(column, cooled.Field.Material, cooled.Columns.Properties, closure);
        Assert.IsGreaterThan(0, diagnostic.EnhancedInterfaceCount);
        Assert.IsGreaterThan(1.0, diagnostic.MaximumConductivityMultiplier);
        Assert.IsLessThan(column.ThicknessMeters(cooled.Field.Material, cooled.Columns.Properties), diagnostic.ConnectedLiquidDepthMeters);
        var viscous = PlanetOriginLiquidHeatTransport.Diagnose(column, cooled.Field.Material, cooled.Columns.Properties,
            closure with { DynamicViscosityPascalSeconds = 1_000.0 });
        Assert.IsLessThan(diagnostic.MaximumConductivityMultiplier, viscous.MaximumConductivityMultiplier);
        var coupledBoundary = boundary with { LiquidHeatTransport = closure };
        var next = cooled.Advance(PlanetOriginDuration.FromSeconds(0.01), coupledBoundary);
        var conductive = cooled.Advance(PlanetOriginDuration.FromSeconds(0.01), boundary);
        Assert.AreNotEqual(conductive.Columns!.Columns[0].StoredEnthalpyJoules, next.Columns!.Columns[0].StoredEnthalpyJoules);
        Assert.AreEqual(0.0, next.Epoch.EnergyConservationResidualJoules,
            next.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
        Assert.AreEqual(cooled.Field.TotalActiveRockMassKilograms, next.Field.TotalActiveRockMassKilograms,
            cooled.Field.TotalActiveRockMassKilograms * 1e-12);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            (coupledBoundary with { LiquidHeatTransport = closure with { DynamicViscosityPascalSeconds = 0.0 } }).Validate());
    }

    [TestMethod]
    public void Advance_RefinedDepthAndTime_ConvergesLiquidFaceAndOutgoingEnergy()
    {
        static (double Face, double Radiation) Resolve(int layers, double step)
        {
            var initial = PlanetRockColumnReference.CreateUpperMagmaInitial(RadiusMeters, layerCount: layers, gridLevel: 0);
            var boundary = initial.SurfaceBoundary!.Value with { LiquidHeatTransport = PlanetOriginLiquidHeatTransportParameters.Reference };
            var state = PlanetOriginRockColumnIntegrator.Advance(initial, PlanetOriginDuration.FromSeconds(1.0), boundary, new(step, 1000));
            Assert.AreEqual(1_000_000L, state.Epoch.Clock.MicrosecondsIntoYear);
            Assert.AreEqual(0.0, state.Epoch.EnergyConservationResidualJoules, state.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
            return (state.ResolveRadiatingSurfaceTemperature(state.Columns!.Columns[0].Id), state.Epoch.CoupledEnergy.CumulativeEmittedInfraredEnergyJoules);
        }
        var coarse = Resolve(32, 0.005);
        var medium = Resolve(64, 0.005);
        var fine = Resolve(128, 0.005);
        Assert.IsLessThan(Math.Abs(coarse.Face - medium.Face), Math.Abs(medium.Face - fine.Face));
        Assert.IsLessThan(Math.Abs(coarse.Radiation - medium.Radiation), Math.Abs(medium.Radiation - fine.Radiation));
        Assert.IsLessThan(0.025, Math.Abs(medium.Radiation - fine.Radiation) / fine.Radiation);
        var roughTime = Resolve(64, 0.02);
        var coarseTime = Resolve(64, 0.01);
        Assert.IsLessThan(Math.Abs(roughTime.Face - coarseTime.Face), Math.Abs(coarseTime.Face - medium.Face));
        Assert.IsLessThan(Math.Abs(roughTime.Radiation - coarseTime.Radiation), Math.Abs(coarseTime.Radiation - medium.Radiation));
        Assert.IsLessThan(0.001, Math.Abs(coarseTime.Radiation - medium.Radiation) / medium.Radiation);
    }

    [TestMethod]
    public void CreateCoupledUpperMagma_SameSeed_ReplaysFundedMassHeatAndClosure()
    {
        var first = PlanetFormationInitialHeat.CreateUpperMagma(RadiusMeters, 24061984, gridLevel: 1);
        var replay = PlanetFormationInitialHeat.CreateUpperMagma(RadiusMeters, 24061984, gridLevel: 1);
        CollectionAssert.AreEqual(first.Field.Cells.ToArray(), replay.Field.Cells.ToArray());
        var state = PlanetRockColumnReference.CreateCoupledUpperMagmaInitial(first);
        Assert.AreEqual(PlanetOriginLiquidHeatTransportParameters.Reference, state.SurfaceBoundary!.Value.LiquidHeatTransport!.Value);
        Assert.AreEqual(first.Field.TotalActiveRockMassKilograms, state.Field.TotalActiveRockMassKilograms, first.Field.TotalActiveRockMassKilograms * 1e-12);
        Assert.AreEqual(first.Field.TotalStoredEnthalpyJoules, state.Field.TotalStoredEnthalpyJoules, first.Field.TotalStoredEnthalpyJoules * 1e-12);
    }
}
