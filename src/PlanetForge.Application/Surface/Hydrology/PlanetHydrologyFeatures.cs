namespace PlanetForge.Application.Surface.Hydrology;

public sealed record PlanetHydrologyFeatures(
    IReadOnlyList<PlanetWatershed> Watersheds,
    IReadOnlyList<PlanetLake> Lakes,
    IReadOnlyList<PlanetRiverSegment> RiverSegments);
