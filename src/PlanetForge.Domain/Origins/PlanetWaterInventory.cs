namespace PlanetForge.Domain.Origins;

/// <summary>
/// Physically distinct H2O-equivalent stores. These are mass accounts, not a
/// temperature or pressure based phase solver and not rendering instructions.
/// </summary>
public enum PlanetWaterReservoir
{
    HydratedMantle = 0,
    DissolvedInMagma = 1,
    AtmosphericVapor = 2,
    SurfaceLiquid = 3,
    SurfaceIce = 4,
    Groundwater = 5
}

/// <summary>
/// Immutable accounting of externally delivered water, internal transfers
/// and atmospheric escape. Water may redistribute without appearing from
/// nothing; a separate physical model must choose which transfers occur.
/// </summary>
public sealed class PlanetWaterInventory
{
    private const int ReservoirCount = 6;
    private readonly double[] masses;

    private PlanetWaterInventory(double[] masses, double deliveredMassKilograms, double escapedMassKilograms)
    {
        this.masses = masses;
        CumulativeDeliveredMassKilograms = deliveredMassKilograms;
        CumulativeEscapedMassKilograms = escapedMassKilograms;
    }

    public static PlanetWaterInventory Empty => new(new double[ReservoirCount], 0.0, 0.0);

    public double CumulativeDeliveredMassKilograms { get; }

    public double CumulativeEscapedMassKilograms { get; }

    public double PlanetaryWaterMassKilograms => masses.Sum();

    public double ConservationResidualKilograms =>
        CumulativeDeliveredMassKilograms - CumulativeEscapedMassKilograms - PlanetaryWaterMassKilograms;

    public double GetMassKilograms(PlanetWaterReservoir reservoir) => masses[ValidateReservoir(reservoir)];

    /// <summary>
    /// External delivery through accretion or an impact, explicitly identified
    /// as an inventory source (not water invented by volcanic outgassing).
    /// </summary>
    public PlanetWaterInventory Deliver(PlanetWaterReservoir destination, double massKilograms)
    {
        var destinationIndex = ValidateReservoir(destination);
        ValidateMass(massKilograms);
        if (massKilograms == 0.0) return this;

        var copy = (double[])masses.Clone();
        copy[destinationIndex] = AddFinite(copy[destinationIndex], massKilograms);
        return new PlanetWaterInventory(copy, AddFinite(CumulativeDeliveredMassKilograms, massKilograms),
            CumulativeEscapedMassKilograms);
    }

    /// <summary>
    /// Move existing H2O between stores: mantle degassing, vapour condensation,
    /// freezing, melting, hydration and groundwater flow are transfer models.
    /// </summary>
    public PlanetWaterInventory Transfer(PlanetWaterReservoir source, PlanetWaterReservoir destination, double massKilograms)
    {
        var sourceIndex = ValidateReservoir(source);
        var destinationIndex = ValidateReservoir(destination);
        ValidateMass(massKilograms);
        if (sourceIndex == destinationIndex)
        {
            throw new ArgumentException("Source and destination water reservoirs must differ.", nameof(destination));
        }

        if (massKilograms == 0.0) return this;
        if (masses[sourceIndex] < massKilograms)
        {
            throw new InvalidOperationException("Cannot transfer more water than the source reservoir contains.");
        }

        var copy = (double[])masses.Clone();
        copy[sourceIndex] -= massKilograms;
        copy[destinationIndex] = AddFinite(copy[destinationIndex], massKilograms);
        return new PlanetWaterInventory(copy, CumulativeDeliveredMassKilograms, CumulativeEscapedMassKilograms);
    }

    /// <summary>
    /// External loss from the atmospheric vapour inventory. Other physical
    /// escape pathways require separate source-identified models.
    /// </summary>
    public PlanetWaterInventory EscapeAtmosphericVapor(double massKilograms)
    {
        ValidateMass(massKilograms);
        if (massKilograms == 0.0) return this;

        var sourceIndex = (int)PlanetWaterReservoir.AtmosphericVapor;
        if (masses[sourceIndex] < massKilograms)
        {
            throw new InvalidOperationException("Atmospheric escape exceeds the available vapour mass.");
        }

        var copy = (double[])masses.Clone();
        copy[sourceIndex] -= massKilograms;
        return new PlanetWaterInventory(copy, CumulativeDeliveredMassKilograms,
            AddFinite(CumulativeEscapedMassKilograms, massKilograms));
    }

    private static int ValidateReservoir(PlanetWaterReservoir reservoir)
    {
        if (reservoir is < PlanetWaterReservoir.HydratedMantle or > PlanetWaterReservoir.Groundwater)
        {
            throw new ArgumentOutOfRangeException(nameof(reservoir), reservoir, "Unknown water reservoir.");
        }

        return (int)reservoir;
    }

    private static void ValidateMass(double massKilograms)
    {
        if (!double.IsFinite(massKilograms) || massKilograms < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(massKilograms), "Water mass must be finite and nonnegative.");
        }
    }

    private static double AddFinite(double first, double second)
    {
        var sum = first + second;
        if (!double.IsFinite(sum))
        {
            throw new ArgumentOutOfRangeException(nameof(second), "Accumulated water mass must remain finite.");
        }

        return sum;
    }
}
