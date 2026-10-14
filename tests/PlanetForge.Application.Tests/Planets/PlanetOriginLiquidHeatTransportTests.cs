using PlanetForge.Application.Planets;
using PlanetForge.Domain.Origins;

namespace PlanetForge.Application.Tests.Planets;

[TestClass]
public sealed class PlanetOriginLiquidHeatTransportTests
{
    private const double RadiusMeters = 6_371_000.0;

    [TestMethod]
    public void ResolveSuspensionViscosity_Crystallization_ThickensFluidAndRejectsJammedMaterial()
    {
        var initial = PlanetRockColumnReference.CreateUpperMagmaInitial(RadiusMeters, gridLevel: 0);
        var properties = initial.Columns!.Properties;
        var closure = PlanetOriginLiquidHeatTransportParameters.CrystalSuspensionReference;
        double? Viscosity(double melt) => PlanetOriginLiquidHeatTransport.ResolveCrystalSuspensionViscosity(melt, properties, closure);
        Assert.AreEqual(closure.DynamicViscosityPascalSeconds, Viscosity(1.0)!.Value);
        Assert.IsGreaterThan(Viscosity(1.0)!.Value, Viscosity(0.8)!.Value);
        Assert.IsGreaterThan(Viscosity(0.8)!.Value, Viscosity(0.5)!.Value);
        Assert.IsGreaterThan(Viscosity(0.5)!.Value, Viscosity(0.4)!.Value);
        Assert.IsNull(Viscosity(0.3));
        Assert.IsNull(Viscosity(0.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Viscosity(double.NaN));
    }

    [TestMethod]
    public void Advance_SuspensionHeatTransport_MixedFluidContinuesExchangingConservedOwnedHeat()
    {
        var initial = PlanetRockColumnReference.CreateUpperMagmaInitial(RadiusMeters, gridLevel: 0);
        var boundary = initial.SurfaceBoundary!.Value;
        var cooled = initial.Advance(PlanetOriginDuration.FromSeconds(3.0), boundary);
        var column = cooled.Columns!.Columns[0];
        var closure = PlanetOriginLiquidHeatTransportParameters.CrystalSuspensionReference;
        Assert.IsTrue(column.Layers[0].Resolve(cooled.Field.Material).MeltFraction < 1.0);
        var liquidOnly = PlanetOriginLiquidHeatTransport.Diagnose(column, cooled.Field.Material, cooled.Columns.Properties,
            PlanetOriginLiquidHeatTransportParameters.Reference);
        var suspension = PlanetOriginLiquidHeatTransport.Diagnose(column, cooled.Field.Material, cooled.Columns.Properties, closure);
        Assert.IsGreaterThan(liquidOnly.ConnectedLiquidDepthMeters, suspension.ConnectedLiquidDepthMeters);
        var next = cooled.Advance(PlanetOriginDuration.FromSeconds(0.01), boundary with { LiquidHeatTransport = closure });
        var strict = cooled.Advance(PlanetOriginDuration.FromSeconds(0.01), boundary with { LiquidHeatTransport = PlanetOriginLiquidHeatTransportParameters.Reference });
        Assert.AreNotEqual(strict.Columns!.Columns[0].Layers[0].StoredEnthalpyJoules, next.Columns!.Columns[0].Layers[0].StoredEnthalpyJoules);
        Assert.AreEqual(0.0, next.Epoch.EnergyConservationResidualJoules, next.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
        Assert.AreEqual(cooled.Field.TotalActiveRockMassKilograms, next.Field.TotalActiveRockMassKilograms,
            cooled.Field.TotalActiveRockMassKilograms * 1e-12);
        Assert.AreEqual(0L, initial.Epoch.Revision);
    }

    [TestMethod]
    public void Advance_Nested128LayerSeededTransient_AdmitsSupportedMicrosecondTrialsAndReachesExactTime()
    {
        var funded = PlanetFormationInitialHeat.CreateUpperMagma(RadiusMeters, 24061984, gridLevel: 1);
        var initial = PlanetRockColumnReference.CreateCoupledUpperMagmaInitial(funded, 128);
        var next = PlanetOriginRockColumnIntegrator.Advance(initial, PlanetOriginDuration.FromSeconds(1.0), initial.SurfaceBoundary!.Value,
            new(30.0, 10000) { MaximumThermalErrorKelvin = 0.1 });
        Assert.AreEqual(1_000_000L, next.Epoch.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(0L, initial.Epoch.Revision);
        Assert.IsTrue(next.Field.Cells.All(cell => next.ResolveRadiatingSurfacePhase(cell.Id).MeltFraction == 1.0));
        Assert.AreEqual(initial.Field.TotalActiveRockMassKilograms, next.Field.TotalActiveRockMassKilograms,
            initial.Field.TotalActiveRockMassKilograms * 1e-12);
        Assert.AreEqual(0.0, next.Epoch.EnergyConservationResidualJoules, next.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
    }

    [TestMethod]
    public void CreateCoupledUpperMagma_NestedDepthRefinement_PreservesEveryParentMassEnergyAndPhysicalExtent()
    {
        var funded = PlanetFormationInitialHeat.CreateUpperMagma(RadiusMeters, 24061984, gridLevel: 0);
        var coarse = PlanetRockColumnReference.CreateCoupledUpperMagmaInitial(funded, 64);
        var fine = PlanetRockColumnReference.CreateCoupledUpperMagmaInitial(funded, 128);
        for (var cell = 0; cell < coarse.Columns!.Columns.Count; cell++)
        {
            var parent = coarse.Columns.Columns[cell];
            var children = fine.Columns!.Columns[cell];
            for (var depth = 0; depth < parent.Layers.Count; depth++)
            {
                var first = children.Layers[2 * depth];
                var second = children.Layers[2 * depth + 1];
                Assert.AreEqual(parent.Layers[depth].MassKilograms, first.MassKilograms + second.MassKilograms, parent.MassKilograms * 1e-12);
                Assert.AreEqual(parent.Layers[depth].StoredEnthalpyJoules, first.StoredEnthalpyJoules + second.StoredEnthalpyJoules,
                    parent.StoredEnthalpyJoules * 1e-12);
            }
            Assert.AreEqual(parent.ThicknessMeters(coarse.Field.Material, coarse.Columns.Properties),
                children.ThicknessMeters(fine.Field.Material, fine.Columns.Properties), 1e-8);
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetRockColumnReference.CreateResolvedInitial(funded,
            layerCount: 100, depthGradingBaseLayerCount: 64));
    }

    [TestMethod]
    public async Task AdvanceAsync_AdaptiveController_PreservesExactSynchronousStateAndCancelsUnpublishedWork()
    {
        var initial = PlanetRockColumnReference.CreateUpperMagmaInitial(RadiusMeters, gridLevel: 0);
        var boundary = initial.SurfaceBoundary!.Value with { LiquidHeatTransport = PlanetOriginLiquidHeatTransportParameters.Reference };
        var duration = PlanetOriginDuration.FromSeconds(1.0);
        var options = new PlanetOriginRockColumnIntegrationOptions(0.25, 10000) { MaximumThermalErrorKelvin = 0.1 };
        var synchronous = PlanetOriginRockColumnIntegrator.Advance(initial, duration, boundary, options);
        var asynchronous = await PlanetOriginRockColumnIntegrator.AdvanceAsync(initial, duration, boundary, options);
        CollectionAssert.AreEqual(synchronous.Columns!.Columns.SelectMany(column => column.Layers).ToArray(),
            asynchronous.Columns!.Columns.SelectMany(column => column.Layers).ToArray());
        Assert.AreEqual(synchronous.Epoch.Clock, asynchronous.Epoch.Clock);
        Assert.AreEqual(synchronous.Epoch.Revision, asynchronous.Epoch.Revision);
        Assert.AreEqual(synchronous.Epoch.CoupledEnergy!.CumulativeEmittedInfraredEnergyJoules,
            asynchronous.Epoch.CoupledEnergy!.CumulativeEmittedInfraredEnergyJoules);
        using var cancellation = new CancellationTokenSource();
        var pending = PlanetOriginRockColumnIntegrator.AdvanceAsync(initial, duration, boundary, options, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => pending);
        Assert.AreEqual(0L, initial.Epoch.Revision);
    }

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
        var cooled = initial.Advance(PlanetOriginDuration.FromSeconds(3.0), boundary);
        var column = cooled.Columns!.Columns[0];
        var closure = PlanetOriginLiquidHeatTransportParameters.Reference;
        var diagnostic = PlanetOriginLiquidHeatTransport.Diagnose(column, cooled.Field.Material, cooled.Columns.Properties, closure);
        Assert.IsTrue(column.Layers[0].Resolve(cooled.Field.Material).MeltFraction < 1.0);
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
