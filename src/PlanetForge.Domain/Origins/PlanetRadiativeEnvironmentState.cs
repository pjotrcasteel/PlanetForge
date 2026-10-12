namespace PlanetForge.Domain.Origins;

/// <summary>
/// Cumulative net energy exchange across a planetary radiative boundary.
/// This is distinct from the interior/mantle heat budget. Both absorption
/// and outgoing radiation require named environmental energy stores.
/// </summary>
public sealed class PlanetRadiativeEnvironmentState
{
    public PlanetRadiativeEnvironmentState(PlanetEnvironmentalEnergyBudget environment)
        : this(environment, environment?.TotalEnergyJoules ?? throw new ArgumentNullException(nameof(environment)), 0.0, 0.0)
    {
    }

    private PlanetRadiativeEnvironmentState(
        PlanetEnvironmentalEnergyBudget environment,
        double initialEnergyJoules,
        double absorbedJoules,
        double emittedJoules)
    {
        Environment = environment;
        InitialEnergyJoules = initialEnergyJoules;
        CumulativeAbsorbedStellarEnergyJoules = absorbedJoules;
        CumulativeEmittedThermalEnergyJoules = emittedJoules;
    }

    public PlanetEnvironmentalEnergyBudget Environment { get; }

    public double InitialEnergyJoules { get; }

    public double CumulativeAbsorbedStellarEnergyJoules { get; }

    public double CumulativeEmittedThermalEnergyJoules { get; }

    public double ConservationResidualJoules => InitialEnergyJoules + CumulativeAbsorbedStellarEnergyJoules -
        CumulativeEmittedThermalEnergyJoules - Environment.TotalEnergyJoules;

    /// <summary>
    /// Integrates explicitly supplied constant top-of-atmosphere power over a
    /// bounded physical interval. The owner must update emitting temperature,
    /// stellar inputs, greenhouse properties and albedo between intervals.
    /// No claim of a calibrated Hadean cooling timeline is made.
    /// </summary>
    public PlanetRadiativeEnvironmentState Advance(
        PlanetRadiativeBalance boundary,
        double elapsedSeconds,
        PlanetEnvironmentHeatReservoir stellarAbsorptionReservoir,
        PlanetEnvironmentHeatReservoir infraredEmissionReservoir,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Radiative duration must be finite and nonnegative.");
        }

        // Validate both reservoirs before any arithmetic.
        Environment.GetEnergyJoules(infraredEmissionReservoir);
        Environment.GetEnergyJoules(stellarAbsorptionReservoir);
        RequirePower(boundary.AbsorbedStellarPowerWatts, nameof(boundary.AbsorbedStellarPowerWatts));
        RequirePower(boundary.EmittedThermalPowerWatts, nameof(boundary.EmittedThermalPowerWatts));

        var incoming = boundary.AbsorbedStellarPowerWatts * elapsedSeconds;
        var outgoing = boundary.EmittedThermalPowerWatts * elapsedSeconds;
        RequirePower(incoming, nameof(boundary.AbsorbedStellarPowerWatts));
        RequirePower(outgoing, nameof(boundary.EmittedThermalPowerWatts));

        var nextAbsorbed = CumulativeAbsorbedStellarEnergyJoules + incoming;
        var nextEmitted = CumulativeEmittedThermalEnergyJoules + outgoing;
        RequirePower(nextAbsorbed, nameof(nextAbsorbed));
        RequirePower(nextEmitted, nameof(nextEmitted));

        var next = Environment.ApplyExternalRadiation(
            stellarAbsorptionReservoir, incoming, infraredEmissionReservoir, outgoing);
        cancellationToken.ThrowIfCancellationRequested();
        return new PlanetRadiativeEnvironmentState(next, InitialEnergyJoules, nextAbsorbed, nextEmitted);
    }

    private static void RequirePower(double wattsOrJoules, string name)
    {
        if (!double.IsFinite(wattsOrJoules) || wattsOrJoules < 0.0)
        {
            throw new ArgumentOutOfRangeException(name, "Radiative power and integrated energy must be finite and nonnegative.");
        }
    }
}
