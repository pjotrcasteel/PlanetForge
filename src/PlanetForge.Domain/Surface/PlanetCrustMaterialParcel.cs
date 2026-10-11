using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Domain.Surface;

/// <summary>
/// A stable material sample of the lithosphere. Its identity, reference mass and
/// composition follow the material, not the geographic region at which it is observed.
/// Sample positions do not imply a polygon mesh or continuous crust ownership.
/// </summary>
public enum PlanetCrustComposition
{
    Oceanic,
    Continental
}

/// <summary>
/// Seeded, immutable reference material. The unique key is (World, Id), not Id
/// alone; plate membership is the present-day starting frame, not permanent
/// geological ownership once collision/rifting simulation is implemented.
/// </summary>
public sealed class PlanetCrustMaterialParcel
{
    public PlanetCrustMaterialParcel(
        PlanetWorldIdentity world,
        PlanetCrustParcelId id,
        PlanetTectonicPlateId initialPlate,
        PlanetVector presentDirection,
        PlanetCrustComposition composition,
        double referenceAreaSquareMeters,
        double referenceThicknessMeters,
        double referenceDensityKilogramsPerCubicMeter)
    {
        if (id.Value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "Crust material identity must be nonzero.");
        }

        if (initialPlate.World != world)
        {
            throw new ArgumentException("Crust and plate must belong to the same generated world.", nameof(initialPlate));
        }

        if (!Enum.IsDefined(composition))
        {
            throw new ArgumentOutOfRangeException(nameof(composition));
        }

        RequirePositiveFinite(referenceAreaSquareMeters, nameof(referenceAreaSquareMeters));
        RequirePositiveFinite(referenceThicknessMeters, nameof(referenceThicknessMeters));
        RequirePositiveFinite(referenceDensityKilogramsPerCubicMeter, nameof(referenceDensityKilogramsPerCubicMeter));

        var mass = referenceAreaSquareMeters * referenceThicknessMeters * referenceDensityKilogramsPerCubicMeter;
        if (!double.IsFinite(mass))
        {
            throw new ArgumentOutOfRangeException(nameof(referenceAreaSquareMeters), "Parcel reference mass must be finite.");
        }

        World = world;
        Id = id;
        InitialPlate = initialPlate;
        PresentDirection = PlanetVector.Normalize(presentDirection);
        Composition = composition;
        ReferenceAreaSquareMeters = referenceAreaSquareMeters;
        ReferenceThicknessMeters = referenceThicknessMeters;
        ReferenceDensityKilogramsPerCubicMeter = referenceDensityKilogramsPerCubicMeter;
        ReferenceMassKilograms = mass;
    }

    public PlanetWorldIdentity World { get; }
    public PlanetCrustParcelId Id { get; }
    public PlanetTectonicPlateId InitialPlate { get; }
    public PlanetVector PresentDirection { get; }
    public PlanetCrustComposition Composition { get; }
    public double ReferenceAreaSquareMeters { get; }
    public double ReferenceThicknessMeters { get; }
    public double ReferenceDensityKilogramsPerCubicMeter { get; }
    public double ReferenceMassKilograms { get; }

    private static void RequirePositiveFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Reference material parameters must be finite and positive.");
        }
    }
}

/// <summary>
/// An Eulerian observation of a persistent material parcel at a requested epoch.
/// This is kinematic reconstruction, not collision-resolved material simulation.
/// </summary>
public readonly record struct PlanetCrustParcelPosition(
    PlanetCrustMaterialParcel Material,
    PlanetGeologicalEpoch Epoch,
    PlanetVector GeographicDirection);
