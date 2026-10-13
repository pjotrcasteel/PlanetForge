using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Planets;

/// <summary>
/// Explicit, hypothetical initially molten dry surface-shell reference.
/// This is a conserved high-enthalpy initial condition, not a reconstruction
/// of the exact Hadean impact history or a convecting full-depth magma ocean.
/// The phase model, surface inventory, and rock material match the existing
/// canonical regional-rock reference; no heat is created by the renderer.
/// </summary>
public static class PlanetMagmaOceanReference
{
    public const double InitialSurfaceTemperatureKelvin = 2_200.0;
    public const double InitialInteriorTemperatureKelvin = 2_400.0;

    private const double SurfaceRockMassKilograms = 1.0e19;
    private const double RegionalContrastFraction = 0.08;
    private static readonly PlanetOriginSurfaceRockPhaseMaterial Material =
        new(850.0, 400_000.0, 1_300.0, 1_900.0);

    public static PlanetOriginCanonicalRockCells CreateInitial(double planetRadiusMeters, double contrastFraction = RegionalContrastFraction, int gridLevel = 3)
    {
        if (!double.IsFinite(contrastFraction) || contrastFraction < 0.0 || contrastFraction > 0.3)
            throw new ArgumentOutOfRangeException(nameof(contrastFraction));
        var mantle = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(
            4.0e24, 1_200.0, 400_000.0, 1_300.0, 1_900.0));
        var interior = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(mantle.CalculateStoredEnergyJoules(InitialInteriorTemperatureKelvin)), mantle);
        var initialRockEnthalpy = new PlanetRockEnthalpyModel(Material.ToRockProperties(SurfaceRockMassKilograms))
            .CalculateStoredEnergyJoules(InitialSurfaceTemperatureKelvin);
        var environment = new PlanetWaterEnvironmentalState(PlanetWaterInventory.Empty,
            new PlanetEnvironmentalEnergyBudget(0.0, 0.0, initialRockEnthalpy));
        var capacities = new PlanetEnvironmentHeatCapacityParameters(
            0.0, 1_000.0, 2_000.0, 4_200.0, SurfaceRockMassKilograms,
            Material.SpecificHeatCapacityJoulesPerKilogramKelvin)
        {
            SurfaceRockPhaseMaterial = Material
        };
        var ledger = new PlanetOriginEnergyLedger(interior, environment, capacities);
        var uniform = PlanetOriginSurfaceRockPhaseField.FromCanonicalPhaseSurface(
            ledger, new PlanetSurfaceGridLayout(gridLevel), planetRadiusMeters);

        // Broad spherical harmonics encode one declared, non-unique regional
        // initial-temperature hypothesis. We redistribute *existing* energy
        // rather than generating artificial local heat or inventing impacts.
        var field = uniform.RedistributeInitialEnergy(cell =>
        {
            var position = PlanetSurfaceGridGeometry.GetCenterDirection(cell.Id);
            var variation = 1.0 + 0.36 * position.X +
                0.20 * position.Y * position.Z +
                0.15 * Math.Cos(3.0 * position.Z - position.Y);
            return Math.Max(0.05, variation) * cell.ActiveRockMassKilograms;
        }, initialRockEnthalpy * contrastFraction);

        // The visual state is only a valid "initially molten" reference when
        // every canonical cell is liquid under this explicitly named model.
        if (field.Cells.Any(cell => cell.Resolve(Material).MeltFraction < 1.0 - 1e-12))
            throw new InvalidOperationException("Magma-ocean initial conditions must fund every surface cell above its liquidus.");

        return new PlanetOriginCanonicalRockCells(new PlanetOriginEpochState(ledger), field);
    }
}
