using System.Globalization;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Finds saved geographical regions near the camera. Stable tile IDs describe
/// addresses, not tectonic plates or the physical erosion-grid dimensions.
/// At orbital distances no private archive is fetched or published.
/// </summary>
public static class PlanetGeologicalCameraRegionSelector
{
    private const double MaximumCandidateAngleRadians = 0.065;
    private const double MaximumStreamingDistanceFromCenter = 1.35;

    public static PlanetGeologicalRegionId? Select(IReadOnlyList<PlanetGeologicalRegionId> available,
        int seed, PlanetSurfaceView view)
    {
        ArgumentNullException.ThrowIfNull(available);
        if (!double.IsFinite(view.CameraDistanceFromCenter) || view.CameraDistanceFromCenter <= 1 ||
            view.CameraDistanceFromCenter >= MaximumStreamingDistanceFromCenter)
        {
            return null;
        }

        var direction = PlanetVector.Normalize(view.CameraDirection);
        var bestAlignment = -1.0;
        PlanetGeologicalRegionId? nearest = null;
        foreach (var region in available)
        {
            if (region.World.Seed != seed || region.World.GenerationVersion != PlanetGenerationVersion.Current)
            {
                continue;
            }

            var alignment = Math.Clamp(PlanetVector.Dot(direction, region.CenterDirection), -1.0, 1.0);
            var bounds = PlanetTileGeometry.CalculateBounds(region.Tile);
            var maximumAngle = Math.Min(MaximumCandidateAngleRadians, bounds.AngularRadiusRadians + 0.01);
            if (alignment < Math.Cos(maximumAngle))
            {
                continue;
            }

            if (alignment > bestAlignment + 1e-12 ||
                (Math.Abs(alignment - bestAlignment) <= 1e-12 &&
                 (nearest is null || StringComparer.Ordinal.Compare(region.ToString(), nearest.Value.ToString()) < 0)))
            {
                bestAlignment = alignment;
                nearest = region;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Parses IndexedDB keys with no disk reads. Only the current geological
    /// present epoch is streamed; historical epochs remain opt-in inspections.
    /// </summary>
    public static bool TryParsePresentArchiveKey(string key, out PlanetGeologicalRegionId region)
    {
        region = default;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var parts = key.Split('/');
        if (parts.Length != 5 || parts[0] != "planetforge" || parts[1] != "geology" ||
            parts[2] != "v1" || parts[4] != "YBP:0")
        {
            return false;
        }

        var address = parts[3].Split(':');
        if (address.Length != 6 ||
            !TryNumber(address[0], 'G', out var version) || version != PlanetGenerationVersion.Current.Value ||
            !TryNumber(address[1], 'S', out var seed) ||
            !TryNumber(address[2], 'F', out var face) || !Enum.IsDefined((CubeFace)face) ||
            !TryNumber(address[3], 'L', out var level) || level is < 0 or > 20 ||
            !TryNumber(address[4], 'X', out var x) ||
            !TryNumber(address[5], 'Y', out var y))
        {
            return false;
        }

        var cells = 1 << level;
        if (x < 0 || y < 0 || x >= cells || y >= cells)
        {
            return false;
        }

        region = PlanetGeologicalRegionId.CreateCurrent(seed, new PlanetTileId((CubeFace)face, level, x, y));
        return true;
    }

    private static bool TryNumber(string part, char prefix, out int result)
    {
        result = 0;
        return part.Length >= 2 && part[0] == prefix &&
            int.TryParse(part.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }
}
