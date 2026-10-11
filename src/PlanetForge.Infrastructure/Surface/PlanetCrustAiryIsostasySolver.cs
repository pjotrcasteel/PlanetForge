using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Pressure-balanced, local Airy-isostatic response for one existing crust
/// material column. Positive SurfaceDisplacementMeters is potential uplift.
/// Positive MantleRootChangeMeters is downward crustal-root growth.
/// Neither quantity is an accumulated landscape elevation.
/// </summary>
public readonly record struct PlanetCrustIsostaticColumn(
    PlanetCrustParcelId MaterialId,
    PlanetVector GeographicDirection,
    PlanetCrustComposition Composition,
    double SolidThicknessChangeMeters,
    double SurfaceDisplacementMeters,
    double MantleRootChangeMeters,
    double CoverDensityKilogramsPerCubicMeter,
    double ColumnLoadChangeKilogramsPerSquareMeter,
    double PressureBalanceResidualKilogramsPerSquareMeter);

public sealed class PlanetCrustIsostasySnapshot
{
    internal PlanetCrustIsostasySnapshot(
        PlanetWorldIdentity world,
        PlanetGeologicalEpoch epoch,
        double durationMillionYears,
        PlanetCrustIsostaticColumn[] columns,
        double originalMaterialMassKilograms,
        double balancedMaterialMassKilograms)
    {
        World = world;
        Epoch = epoch;
        DurationMillionYears = durationMillionYears;
        Columns = Array.AsReadOnly(columns);
        OriginalMaterialMassKilograms = originalMaterialMassKilograms;
        BalancedMaterialMassKilograms = balancedMaterialMassKilograms;
    }

    public PlanetWorldIdentity World { get; }
    public PlanetGeologicalEpoch Epoch { get; }
    public double DurationMillionYears { get; }
    public IReadOnlyList<PlanetCrustIsostaticColumn> Columns { get; }
    public double OriginalMaterialMassKilograms { get; }
    public double BalancedMaterialMassKilograms { get; }
}

/// <summary>
/// Research-only local Airy compensation of the conservative A2.5.6 crust
/// mass/solid-volume ledger. Treats lithosphere as vertically supported
/// columns, with an incompressible mantle of fixed reference density and a
/// fixed-density cover (air on continental parcels, seawater on oceanic ones).
///
/// At a fixed compensation depth the load balance is:
///   Δcolumn_load - ρ_mantle * Δroot - ρ_cover * Δsurface = 0
/// and the geometric relation is Δthickness = Δroot + Δsurface.
/// This explicitly separates mantle root growth from free-surface relief.
/// There is NO plastic deformation, viscoelastic relaxation, seawater-level
/// transition, dynamic mantle flow, sediment, magma or future time evolution.
/// </summary>
public sealed class PlanetCrustAiryIsostasySolver
{
    public const double DefaultMantleDensityKilogramsPerCubicMeter = 3_300.0;
    public const double DefaultSeawaterDensityKilogramsPerCubicMeter = 1_030.0;

    public PlanetCrustIsostasySnapshot Evaluate(
        PlanetCrustDeformationSnapshot deformation,
        double mantleDensityKilogramsPerCubicMeter = DefaultMantleDensityKilogramsPerCubicMeter,
        double seawaterDensityKilogramsPerCubicMeter = DefaultSeawaterDensityKilogramsPerCubicMeter)
    {
        ArgumentNullException.ThrowIfNull(deformation);
        if (!double.IsFinite(mantleDensityKilogramsPerCubicMeter) || mantleDensityKilogramsPerCubicMeter <= 0.0 ||
            !double.IsFinite(seawaterDensityKilogramsPerCubicMeter) || seawaterDensityKilogramsPerCubicMeter < 0.0 ||
            seawaterDensityKilogramsPerCubicMeter >= mantleDensityKilogramsPerCubicMeter)
        {
            throw new ArgumentOutOfRangeException(nameof(mantleDensityKilogramsPerCubicMeter),
                "Mantle density must be finite and greater than the nonnegative covering-fluid density.");
        }

        var columns = new PlanetCrustIsostaticColumn[deformation.Columns.Count];
        for (var i = 0; i < columns.Length; i++)
        {
            var column = deformation.Columns[i];
            var cover = column.State.Material.Composition == PlanetCrustComposition.Oceanic
                ? seawaterDensityKilogramsPerCubicMeter : 0.0;
            columns[i] = ComputeColumn(column, mantleDensityKilogramsPerCubicMeter, cover);
        }

        return new PlanetCrustIsostasySnapshot(deformation.World, deformation.Epoch, deformation.DurationMillionYears,
            columns, deformation.ReferenceMassKilograms, deformation.TotalFinalMassKilograms);
    }

    /// <summary>
    /// Pure local hydrostatic force balance; pressure residual is published
    /// for testable numerical closure, NOT evidence of physical equilibrium.
    /// </summary>
    public static PlanetCrustIsostaticColumn ComputeColumn(
        PlanetCrustExchangedColumn column,
        double mantleDensityKilogramsPerCubicMeter,
        double coverDensityKilogramsPerCubicMeter)
    {
        var material = column.State.Material ?? throw new ArgumentException("Material must be present.", nameof(column));
        if (!double.IsFinite(mantleDensityKilogramsPerCubicMeter) || mantleDensityKilogramsPerCubicMeter <=
            material.ReferenceDensityKilogramsPerCubicMeter ||
            !double.IsFinite(coverDensityKilogramsPerCubicMeter) || coverDensityKilogramsPerCubicMeter < 0.0 ||
            coverDensityKilogramsPerCubicMeter >= mantleDensityKilogramsPerCubicMeter)
        {
            throw new ArgumentOutOfRangeException(nameof(mantleDensityKilogramsPerCubicMeter),
                "The mantle must be denser than the reference crust and its covering fluid.");
        }

        if (!double.IsFinite(column.AreaSquareMeters) || column.AreaSquareMeters <= 0.0 ||
            !double.IsFinite(column.TotalMassKilograms) || column.TotalMassKilograms <= 0.0 ||
            !double.IsFinite(column.ThicknessMeters) || column.ThicknessMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(column), "Column mass, area and thickness must be finite and positive.");
        }

        var recordedMass = column.RetainedLocalMassKilograms + column.ImportedMassKilograms;
        if (!double.IsFinite(recordedMass) ||
            Math.Abs(recordedMass - column.TotalMassKilograms) > column.TotalMassKilograms * 1e-10)
        {
            throw new ArgumentException("Column must have a balanced input material ledger.", nameof(column));
        }

        var deltaThickness = column.ThicknessMeters - material.ReferenceThicknessMeters;
        var deltaLoad = column.TotalMassKilograms / column.AreaSquareMeters -
            material.ReferenceMassKilograms / material.ReferenceAreaSquareMeters;
        var surface = (mantleDensityKilogramsPerCubicMeter * deltaThickness - deltaLoad) /
            (mantleDensityKilogramsPerCubicMeter - coverDensityKilogramsPerCubicMeter);
        var root = deltaThickness - surface;
        var residual = deltaLoad - mantleDensityKilogramsPerCubicMeter * root - coverDensityKilogramsPerCubicMeter * surface;
        if (!double.IsFinite(surface) || !double.IsFinite(root) || !double.IsFinite(residual))
        {
            throw new InvalidOperationException("Nonfinite isostatic response cannot be published.");
        }

        return new PlanetCrustIsostaticColumn(material.Id, PlanetVector.Normalize(column.State.GeographicDirection),
            material.Composition, deltaThickness, surface, root, coverDensityKilogramsPerCubicMeter, deltaLoad, residual);
    }
}
