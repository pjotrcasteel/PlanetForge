using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetCrustSphericalFaceFluxLedgerTests
{
    [TestMethod]
    public void Apply_ConnectedFaceFluxes_PreservesBothSidesAndAllMaterialMass()
    {
        var (state, geometry) = BuildInputs(24061984);
        var face = geometry.Faces[42];
        var reverse = geometry.Faces[57];
        var first = state.Columns[(int)face.First.Value - 1].TotalMassKilograms;
        var opposite = state.Columns[(int)reverse.Second.Value - 1].TotalMassKilograms;
        var fluxes = new[] {
            new PlanetCrustSphericalFaceFlux(face.First, face.Second, first * 0.005),
            new PlanetCrustSphericalFaceFlux(reverse.First, reverse.Second, -opposite * 0.005)
        };
        var result = new PlanetCrustSphericalFaceFluxLedger().Apply(state, geometry, fluxes);
        Assert.AreEqual(state.World, result.World);
        Assert.AreEqual(state.Epoch, result.Epoch);
        Assert.AreEqual(state.Columns.Count, result.Balances.Count);
        Assert.AreEqual(state.TotalFinalMassKilograms, result.InitialMassKilograms,
            state.TotalFinalMassKilograms * 1e-12);
        Assert.AreEqual(result.InitialMassKilograms, result.FinalMassKilograms,
            result.InitialMassKilograms * 1e-12);
        Assert.AreEqual(result.Balances.Sum(value => value.IncomingKilograms),
            result.Balances.Sum(value => value.OutgoingKilograms),
            result.InitialMassKilograms * 1e-12);
        CollectionAssert.AreEqual(fluxes, result.Transfers.ToArray());
        Assert.IsTrue(result.Balances.All(b => b.EndingKilograms > 0.0));
    }

    [TestMethod]
    public void Apply_MultipleSharedFacesOfOneDonor_EnforcesAggregateBudgetNotPerFaceBudget()
    {
        var (state, geometry) = BuildInputs(42);
        var group = geometry.Faces.GroupBy(f => f.First).First(g => g.Count() >= 2).Take(2).ToArray();
        var donor = state.Columns[(int)group[0].First.Value - 1].TotalMassKilograms;
        var tooMuch = group.Select(face =>
            new PlanetCrustSphericalFaceFlux(face.First, face.Second, donor * 0.015)).ToArray();
        var ledger = new PlanetCrustSphericalFaceFluxLedger();
        Assert.ThrowsExactly<ArgumentException>(() => ledger.Apply(state, geometry, tooMuch));

        var safe = group.Select(face =>
            new PlanetCrustSphericalFaceFlux(face.First, face.Second, donor * 0.009)).ToArray();
        var result = ledger.Apply(state, geometry, safe);
        Assert.AreEqual(donor * 0.018,
            result.Balances[(int)group[0].First.Value - 1].OutgoingKilograms, donor * 1e-12);
    }

    [TestMethod]
    public void Apply_NonexistentDuplicateCrossWorldAndInvalidFlux_RejectWithoutMutatingInput()
    {
        var (state, geometry) = BuildInputs(42);
        var (_, other) = BuildInputs(24061984);
        var first = geometry.Faces[0];
        var one = new PlanetCrustSphericalFaceFlux(first.First, first.Second, 10_000.0);
        var ledger = new PlanetCrustSphericalFaceFluxLedger();
        Assert.ThrowsExactly<ArgumentException>(() => ledger.Apply(state, other, [one]));
        Assert.ThrowsExactly<ArgumentException>(() => ledger.Apply(state, geometry, [one, one]));
        Assert.ThrowsExactly<ArgumentException>(() => ledger.Apply(state, geometry, [
            one with { SignedKilograms = double.NaN }]));
        Assert.ThrowsExactly<ArgumentException>(() => ledger.Apply(state, geometry, [
            new PlanetCrustSphericalFaceFlux(first.Second, first.First, 1.0)]));
        Assert.ThrowsExactly<ArgumentException>(() => ledger.Apply(state, geometry, [
            new PlanetCrustSphericalFaceFlux(new PlanetCrustParcelId(1), new PlanetCrustParcelId(1025), 1.0)]));

        var unchanged = ledger.Apply(state, geometry, []);
        Assert.AreEqual(0, unchanged.Transfers.Count);
        for (var index = 0; index < state.Columns.Count; index++)
        {
            Assert.AreEqual(state.Columns[index].TotalMassKilograms, unchanged.Balances[index].EndingKilograms);
        }
    }

    [TestMethod]
    public void Apply_RepeatSameSeedAndPhysicalState_YieldsBitwiseReplayableLedger()
    {
        var (state, geometry) = BuildInputs(24061984);
        var face = geometry.Faces[120];
        var flux = new PlanetCrustSphericalFaceFlux(face.First, face.Second, 1e9);
        var ledger = new PlanetCrustSphericalFaceFluxLedger();
        var left = ledger.Apply(state, geometry, [flux]);
        var right = ledger.Apply(state, geometry, [flux]);
        CollectionAssert.AreEqual(left.Transfers.ToArray(), right.Transfers.ToArray());
        CollectionAssert.AreEqual(left.Balances.ToArray(), right.Balances.ToArray());
    }

    private static (PlanetCrustDeformationSnapshot State, PlanetCrustSphericalControlVolumes Geometry) BuildInputs(int seed)
    {
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        var neighbors = PlanetCrustMaterialNeighborhood.Build(catalog);
        var interactions = new PlanetCrustContactStrainSolver().Evaluate(catalog,
            PlanetCrustOwnershipHistory.Create(catalog), neighbors, PlanetGeologicalEpoch.Present, 0.25);
        return (new PlanetCrustMassExchangeSolver().Exchange(catalog, neighbors, interactions),
            PlanetCrustSphericalControlVolumes.Build(catalog));
    }
}
