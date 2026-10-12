namespace PlanetForge.Domain.Origins;

/// <summary>
/// Restricted material routes whose water mass and energy can be accounted for
/// without claiming a mantle chemistry, atmospheric transport or escape-rate law.
/// These are explicit process proposals, not simulated kinetics.
/// </summary>
public enum PlanetOriginVolatileExchangeKind
{
    AccreteHydratedMantle = 0,
    AccreteDissolvedMagma = 1,
    AccreteAtmosphericVapor = 2,
    HydratedMantleToMagma = 3,
    OutgasMagmaVapor = 4,
    EscapeAtmosphericVapor = 5
}

/// <summary>
/// One physical H2O-equivalent material packet. MaterialEnthalpyJoules follows
/// the same effective nonnegative sensible-energy reference as the current
/// coupled ledger. For outgassing it is an *internal* debit/credit; for delivery
/// it is an *external* energy source. Atmospheric escape derives its energy
/// from the actual well-mixed donor, so callers may not invent a second loss.
/// </summary>
public readonly record struct PlanetOriginVolatileExchange(
    PlanetOriginVolatileExchangeKind Kind,
    double MassKilograms,
    double MaterialEnthalpyJoules)
{
    public static PlanetOriginVolatileExchange DeliverToMantle(double mass, double joules) =>
        new(PlanetOriginVolatileExchangeKind.AccreteHydratedMantle, mass, joules);

    public static PlanetOriginVolatileExchange DeliverToMagma(double mass, double joules) =>
        new(PlanetOriginVolatileExchangeKind.AccreteDissolvedMagma, mass, joules);

    public static PlanetOriginVolatileExchange DeliverSteam(double mass, double joules) =>
        new(PlanetOriginVolatileExchangeKind.AccreteAtmosphericVapor, mass, joules);

    public static PlanetOriginVolatileExchange DissolveMantleWater(double mass) =>
        new(PlanetOriginVolatileExchangeKind.HydratedMantleToMagma, mass, 0.0);

    public static PlanetOriginVolatileExchange OutgasSteam(double mass, double joules) =>
        new(PlanetOriginVolatileExchangeKind.OutgasMagmaVapor, mass, joules);

    public static PlanetOriginVolatileExchange EscapeSteam(double mass) =>
        new(PlanetOriginVolatileExchangeKind.EscapeAtmosphericVapor, mass, 0.0);
}

internal sealed record PlanetOriginVolatileExchangeResult(
    PlanetOriginThermalTimeline Interior,
    PlanetWaterEnvironmentalState Environment,
    double ExternalMaterialInputJoules,
    double ExternalMaterialEscapeJoules);

/// <summary>
/// Enthalpy-conserving transfers for specified accretion, mantle-to-magma
/// inventory bookkeeping, magma steam outgassing and atmospheric escape.
/// No empirical rates, greenhouse model, mineral reaction energy, volatile
/// speciation or rain eligibility are inferred.
/// </summary>
internal static class PlanetOriginVolatileTransport
{
    public static PlanetOriginVolatileExchangeResult Apply(
        PlanetOriginThermalTimeline interior,
        PlanetWaterEnvironmentalState environment,
        PlanetEnvironmentHeatCapacityParameters properties,
        IReadOnlyList<PlanetOriginVolatileExchange> changes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(interior);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(changes);
        properties.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var nextInterior = interior;
        var nextEnvironment = environment;
        var delivered = 0.0;
        var escaped = 0.0;

        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validate(change);

            switch (change.Kind)
            {
                case PlanetOriginVolatileExchangeKind.AccreteHydratedMantle:
                case PlanetOriginVolatileExchangeKind.AccreteDissolvedMagma:
                {
                    var reservoir = change.Kind == PlanetOriginVolatileExchangeKind.AccreteHydratedMantle
                        ? PlanetWaterReservoir.HydratedMantle : PlanetWaterReservoir.DissolvedInMagma;
                    var water = nextEnvironment.Water.Deliver(reservoir, change.MassKilograms);
                    nextInterior = nextInterior.ExchangeMaterialEnthalpy(change.MaterialEnthalpyJoules, 0.0);
                    nextEnvironment = nextEnvironment.WithMaterialExchange(water, nextEnvironment.Environment, 0.0);
                    delivered = AddFinite(delivered, change.MaterialEnthalpyJoules);
                    break;
                }
                case PlanetOriginVolatileExchangeKind.AccreteAtmosphericVapor:
                {
                    var water = nextEnvironment.Water.Deliver(PlanetWaterReservoir.AtmosphericVapor, change.MassKilograms);
                    var budget = nextEnvironment.Environment.Deposit(
                        PlanetEnvironmentHeatReservoir.Atmosphere, change.MaterialEnthalpyJoules);
                    nextEnvironment = nextEnvironment.WithMaterialExchange(water, budget, change.MaterialEnthalpyJoules);
                    delivered = AddFinite(delivered, change.MaterialEnthalpyJoules);
                    break;
                }
                case PlanetOriginVolatileExchangeKind.HydratedMantleToMagma:
                {
                    var water = nextEnvironment.Water.Transfer(PlanetWaterReservoir.HydratedMantle,
                        PlanetWaterReservoir.DissolvedInMagma, change.MassKilograms);
                    nextEnvironment = nextEnvironment.WithMaterialExchange(water, nextEnvironment.Environment, 0.0);
                    break;
                }
                case PlanetOriginVolatileExchangeKind.OutgasMagmaVapor:
                {
                    var water = nextEnvironment.Water.Transfer(PlanetWaterReservoir.DissolvedInMagma,
                        PlanetWaterReservoir.AtmosphericVapor, change.MassKilograms);
                    nextInterior = nextInterior.ExchangeMaterialEnthalpy(0.0, change.MaterialEnthalpyJoules);
                    var budget = nextEnvironment.Environment.Deposit(
                        PlanetEnvironmentHeatReservoir.Atmosphere, change.MaterialEnthalpyJoules);
                    nextEnvironment = nextEnvironment.WithMaterialExchange(water, budget, change.MaterialEnthalpyJoules);
                    break;
                }
                case PlanetOriginVolatileExchangeKind.EscapeAtmosphericVapor:
                {
                    var energy = EvaporationFreeEscapeEnergy(nextEnvironment, properties, change.MassKilograms);
                    var water = nextEnvironment.Water.EscapeAtmosphericVapor(change.MassKilograms);
                    var budget = nextEnvironment.Environment.Withdraw(PlanetEnvironmentHeatReservoir.Atmosphere, energy);
                    nextEnvironment = nextEnvironment.WithMaterialExchange(water, budget, -energy);
                    escaped = AddFinite(escaped, energy);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(change), change.Kind, "Unknown volatile material route.");
            }

            // A material exchange must not leave sensible energy in an empty
            // ocean, ice, rock or atmosphere, even transiently at publication.
            PlanetEnvironmentTemperatureModel.Resolve(nextEnvironment, properties);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new PlanetOriginVolatileExchangeResult(nextInterior, nextEnvironment, delivered, escaped);
    }

    private static double EvaporationFreeEscapeEnergy(
        PlanetWaterEnvironmentalState state,
        PlanetEnvironmentHeatCapacityParameters properties,
        double mass)
    {
        if (mass == 0.0) return 0.0;
        // Validate the source mass before calculating an energy debit.
        state.Water.EscapeAtmosphericVapor(mass);
        var capacity = PlanetEnvironmentTemperatureModel.Resolve(state, properties)
            .AtmosphereHeatCapacityJoulesPerKelvin;
        var movedCapacity = mass * properties.VaporSpecificHeatJoulesPerKilogramKelvin;
        var fraction = movedCapacity / capacity;
        if (!double.IsFinite(fraction) || fraction < 0.0 || fraction > 1.0 + 1e-12)
        {
            throw new InvalidOperationException("Escaping vapour cannot exceed the atmosphere's effective heat capacity.");
        }

        var heat = state.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere) * Math.Min(1.0, fraction);
        if (!double.IsFinite(heat) || heat < 0.0)
        {
            throw new InvalidOperationException("Escaping atmospheric sensible energy must be finite and nonnegative.");
        }

        return heat;
    }

    private static void Validate(PlanetOriginVolatileExchange change)
    {
        if (!double.IsFinite(change.MassKilograms) || change.MassKilograms < 0.0 ||
            !double.IsFinite(change.MaterialEnthalpyJoules) || change.MaterialEnthalpyJoules < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(change), "Volatile mass and energy must be finite and nonnegative.");
        }

        if ((change.MassKilograms == 0.0 && change.MaterialEnthalpyJoules != 0.0) ||
            (change.Kind is PlanetOriginVolatileExchangeKind.HydratedMantleToMagma or
                PlanetOriginVolatileExchangeKind.EscapeAtmosphericVapor && change.MaterialEnthalpyJoules != 0.0))
        {
            throw new ArgumentException("Volatile material cannot carry unaccounted heat or change energy without mass.", nameof(change));
        }

        if (change.MassKilograms > 0.0 &&
            change.Kind is PlanetOriginVolatileExchangeKind.OutgasMagmaVapor or
                PlanetOriginVolatileExchangeKind.AccreteAtmosphericVapor &&
            change.MaterialEnthalpyJoules == 0.0)
        {
            throw new ArgumentException("Steam transfer requires an explicit positive carried sensible enthalpy.", nameof(change));
        }
    }

    private static double AddFinite(double first, double second)
    {
        var value = first + second;
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(second), "Accumulated material enthalpy must remain finite.");
        }

        return value;
    }
}
