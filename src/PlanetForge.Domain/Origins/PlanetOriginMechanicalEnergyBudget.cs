namespace PlanetForge.Domain.Origins;

/// <summary>Finite, initially funded gravitational work for a creeping-flow reference; not a kinetic-energy or full geodynamics model.</summary>
public sealed class PlanetOriginMechanicalEnergyBudget
{
    private readonly double roundingCorrection;
    private readonly double thermalizedRoundingCorrection;

    public PlanetOriginMechanicalEnergyBudget(double initiallyFundedJoules) : this(initiallyFundedJoules, initiallyFundedJoules, 0.0, 0.0, 0.0)
    {
        if (!double.IsFinite(initiallyFundedJoules) || initiallyFundedJoules < 0.0)
            throw new ArgumentOutOfRangeException(nameof(initiallyFundedJoules));
    }

    private PlanetOriginMechanicalEnergyBudget(double initial, double stored, double thermalized, double correction, double thermalizedCorrection)
    {
        InitialStoredEnergyJoules = initial;
        StoredEnergyJoules = stored;
        CumulativeThermalizedJoules = thermalized;
        roundingCorrection = correction;
        thermalizedRoundingCorrection = thermalizedCorrection;
    }

    public double InitialStoredEnergyJoules { get; }
    public double StoredEnergyJoules { get; }
    public double CumulativeThermalizedJoules { get; }
    public double ConservationResidualJoules => InitialStoredEnergyJoules - CumulativeThermalizedJoules - StoredEnergyJoules;

    public PlanetOriginMechanicalEnergyBudget Thermalize(double joules)
    {
        if (!double.IsFinite(joules) || joules < 0.0) throw new ArgumentOutOfRangeException(nameof(joules));
        if (joules > StoredEnergyJoules) throw new InvalidOperationException("Mechanical work exceeds its finite donor account.");
        if (joules == 0.0) return this;
        var corrected = -joules - roundingCorrection;
        var next = StoredEnergyJoules + corrected;
        var correction = (next - StoredEnergyJoules) - corrected;
        var correctedHeat = joules - thermalizedRoundingCorrection;
        var cumulative = CumulativeThermalizedJoules + correctedHeat;
        var heatCorrection = (cumulative - CumulativeThermalizedJoules) - correctedHeat;
        if (!double.IsFinite(next) || next < 0.0 || !double.IsFinite(cumulative))
            throw new InvalidOperationException("Mechanical work produced an invalid energy account.");
        return new(InitialStoredEnergyJoules, next, cumulative, correction, heatCorrection);
    }
}
