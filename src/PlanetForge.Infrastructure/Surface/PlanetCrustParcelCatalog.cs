using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Coarse, immutable material-seed catalogue. A bounded number of equal-weight
/// spherical samples are assigned to the EXISTING canonical 18 plate frames.
/// Each sample retains its (world, parcel) identity and reference material
/// quantities across geological epochs and fixed geographic observation windows.
/// </summary>
public sealed class PlanetCrustParcelCatalog
{
    // A representative equal-area quadrature budget, NOT contiguous Voronoi
    // crust polygons. No raster or renderer depends on the count.
    public const int ReferenceParcelCount = 1024;
    private static readonly double GoldenAngle = Math.PI * (3.0 - Math.Sqrt(5.0));

    private readonly PlanetTectonicPlateState[] plateStates;
    private readonly PlanetCrustMaterialParcel[] parcels;
    private readonly IReadOnlyList<PlanetCrustMaterialParcel> readonlyParcels;

    private PlanetCrustParcelCatalog(
        PlanetWorldIdentity world,
        PlanetTectonicPlateState[] plateStates,
        PlanetCrustMaterialParcel[] parcels)
    {
        World = world;
        this.plateStates = plateStates;
        this.parcels = parcels;
        readonlyParcels = Array.AsReadOnly(parcels);
        TotalReferenceMassKilograms = parcels.Sum(parcel => parcel.ReferenceMassKilograms);
    }

    public PlanetWorldIdentity World { get; }
    public IReadOnlyList<PlanetCrustMaterialParcel> Parcels => readonlyParcels;
    public double TotalReferenceMassKilograms { get; }

    /// <summary>
    /// Reconstruct the same material identities and composition after restart
    /// from the canonical world seed, without changing the existing save format.
    /// </summary>
    public static PlanetCrustParcelCatalog Create(int seed)
    {
        var kinematics = new PlanetTectonicPlateKinematics();
        var plates = kinematics.GetPlates(seed).ToArray();
        var world = PlanetWorldIdentity.CreateCurrent(seed);
        var source = new ProceduralPlanetElevationSource();
        var parcels = new PlanetCrustMaterialParcel[ReferenceParcelCount];
        var area = 4.0 * Math.PI * Math.Pow(PlanetTectonicPlateKinematics.NominalPlanetRadiusMeters, 2) / ReferenceParcelCount;

        // Seed-dependent longitude rotation removes the fixed meridian bias;
        // Fibonacci directions spread reference samples over the entire globe.
        var phase = (unchecked((uint)seed * 0x9E3779B9u) / 4_294_967_296.0) * Math.Tau;
        for (var index = 0; index < parcels.Length; index++)
        {
            var y = 1.0 - (2.0 * (index + 0.5) / ReferenceParcelCount);
            var radius = Math.Sqrt(1.0 - y * y);
            var angle = phase + GoldenAngle * index;
            var direction = new PlanetVector(radius * Math.Cos(angle), y, radius * Math.Sin(angle));
            var plate = plates[NearestPlateIndex(direction, plates)];
            // This is the CANONICAL continental potential, rather than an
            // independent plate-shaped continent generator or a sea-level mask.
            var potential = source.SampleTerrainFields(direction, seed).ContinentalPotential;
            var composition = potential >= 0.0 ? PlanetCrustComposition.Continental : PlanetCrustComposition.Oceanic;
            var strength = Math.Clamp(Math.Abs(potential), 0.0, 1.0);
            var thickness = composition == PlanetCrustComposition.Continental
                ? 30_000.0 + 12_000.0 * strength
                : 5_500.0 + 2_000.0 * strength;
            var density = composition == PlanetCrustComposition.Continental ? 2_750.0 : 2_950.0;

            parcels[index] = new PlanetCrustMaterialParcel(world, new PlanetCrustParcelId((ulong)index + 1),
                plate.Id, direction, composition, area, thickness, density);
        }

        return new PlanetCrustParcelCatalog(world, plates, parcels);
    }

    /// <summary>
    /// Reconstruct this material's geographic centroid without changing its
    /// composition, mass or identity. Plate transfer and mass redistribution
    /// must be added by an explicit geological history solver.
    /// </summary>
    public PlanetCrustParcelPosition PositionAt(PlanetCrustParcelId id, PlanetGeologicalEpoch epoch)
    {
        if (id.Value == 0 || id.Value > (ulong)parcels.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown parcel ID in this generated world.");
        }

        var material = parcels[(int)id.Value - 1];
        var plate = plateStates[material.InitialPlate.Index];
        return new PlanetCrustParcelPosition(material, epoch, plate.Move(material.PresentDirection, epoch));
    }

    /// <summary>
    /// Explicit diagnostic geographic lookup. Returns moving material samples
    /// within the requested angular window at an epoch; this is not a dense
    /// planet terrain sampler or a claim of filled polygon coverage.
    /// </summary>
    public IReadOnlyList<PlanetCrustParcelPosition> FindNear(
        PlanetVector fixedGeographicDirection,
        PlanetGeologicalEpoch epoch,
        double angularRadiusRadians)
    {
        if (!double.IsFinite(angularRadiusRadians) || angularRadiusRadians < 0.0 || angularRadiusRadians > Math.PI)
        {
            throw new ArgumentOutOfRangeException(nameof(angularRadiusRadians));
        }

        var center = PlanetVector.Normalize(fixedGeographicDirection);
        var minimumDot = Math.Cos(angularRadiusRadians);
        var matches = new List<PlanetCrustParcelPosition>();
        foreach (var parcel in parcels)
        {
            var position = PositionAt(parcel.Id, epoch);
            if (PlanetVector.Dot(center, position.GeographicDirection) >= minimumDot)
            {
                matches.Add(position);
            }
        }

        return matches.AsReadOnly();
    }

    private static int NearestPlateIndex(PlanetVector direction, IReadOnlyList<PlanetTectonicPlateState> plates)
    {
        var nearest = 0;
        var best = double.NegativeInfinity;
        for (var index = 0; index < plates.Count; index++)
        {
            var dot = PlanetVector.Dot(direction, plates[index].CenterAtPresent);
            if (dot > best)
            {
                best = dot;
                nearest = index;
            }
        }

        return nearest;
    }
}
