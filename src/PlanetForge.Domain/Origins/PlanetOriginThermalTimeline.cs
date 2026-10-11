namespace PlanetForge.Domain.Origins;

/// <summary>
/// Advances one and the same absolute geological clock and conserved rock
/// thermal-energy budget. No solver iteration is silently treated as a
/// geological year; a calibrated flux model must supply the actual heating
/// and cooling rates for each chosen physical interval.
/// </summary>
public sealed class PlanetOriginThermalTimeline
{
    private readonly PlanetRockEnthalpyModel rockModel;

    public PlanetOriginThermalTimeline(
        PlanetOriginClock clock,
        PlanetThermalEnergyBudget energyBudget,
        PlanetRockEnthalpyModel rockModel)
    {
        Clock = clock;
        EnergyBudget = energyBudget ?? throw new ArgumentNullException(nameof(energyBudget));
        this.rockModel = rockModel ?? throw new ArgumentNullException(nameof(rockModel));
    }

    public PlanetOriginClock Clock { get; }

    public PlanetThermalEnergyBudget EnergyBudget { get; }

    public PlanetRockThermalState Rock => rockModel.Resolve(EnergyBudget);

    public PlanetOriginThermalTimeline AdvanceYears(long elapsedYears, PlanetThermalEnergyFlux flux)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedYears);
        // Calculate both candidates before constructing the new state.
        // A failed energy or clock step must leave the former epoch untouched.
        var nextClock = Clock.AdvanceYears(elapsedYears);
        var nextEnergy = EnergyBudget.Advance(elapsedYears, flux);
        return new PlanetOriginThermalTimeline(nextClock, nextEnergy, rockModel);
    }
}
