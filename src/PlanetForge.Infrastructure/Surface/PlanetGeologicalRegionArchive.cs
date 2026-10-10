using System.Text.Json;
using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Portable A2 region checkpoint. JSON is a transportable archive, not a
/// promise of automatic browser persistence: the caller chooses durable
/// storage and owns crash-safe file/database writes.
/// </summary>
public static class PlanetGeologicalRegionArchive
{
    public const int CurrentFormatVersion = 1;
    private const int MaximumJsonCharacters = 40_000_000;

    public static string Serialize(PlanetGeologicalRegionHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return JsonSerializer.Serialize(new Archive(
            CurrentFormatVersion, history.Region.World.Seed, history.Region.World.GenerationVersion.Value,
            (int)history.Region.Tile.Face, history.Region.Tile.Level, history.Region.Tile.X, history.Region.Tile.Y,
            history.Epoch.YearsBeforePresent, history.PlanetRadiusMeters, history.Original, history.Evolved));
    }

    public static PlanetGeologicalRegionHistory Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (json.Length > MaximumJsonCharacters)
        {
            throw new ArgumentException("Region archive exceeds the supported size.", nameof(json));
        }

        var archive = JsonSerializer.Deserialize<Archive>(json) ??
            throw new ArgumentException("Region archive cannot be empty.", nameof(json));
        if (archive.FormatVersion != CurrentFormatVersion)
        {
            throw new ArgumentException("Unsupported geological region archive version.", nameof(json));
        }

        var tile = new PlanetTileId((CubeFace)archive.Face, archive.Level, archive.X, archive.Y);
        var region = new PlanetGeologicalRegionId(
            new PlanetWorldIdentity(archive.Seed, new PlanetGenerationVersion(archive.GenerationVersion)), tile);
        return PlanetGeologicalRegionHistory.Create(region,
            new PlanetGeologicalEpoch(archive.YearsBeforePresent), archive.PlanetRadiusMeters,
            archive.Original, archive.Evolved);
    }

    private sealed record Archive(
        int FormatVersion, int Seed, int GenerationVersion, int Face, int Level, int X, int Y,
        long YearsBeforePresent, double PlanetRadiusMeters,
        PlanetRegionalGeologySnapshot Original, PlanetRegionalGeologySnapshot Evolved);
}
