using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface;

public sealed class PlanetSurfaceLodSelector(PlanetSurfaceLodOptions options)
{
    private const int FarOrbitLevel = 1;
    private const double FarOrbitDistanceFromCenter = 1.35;
    private const int MaximumAdaptiveTiles = 56;

    public IReadOnlyList<PlanetTileId> Select(PlanetSurfaceView view)
    {
        Validate(view);
        if (view.CameraDistanceFromCenter >= FarOrbitDistanceFromCenter)
        {
            return CreateGlobalCoverage(FarOrbitLevel);
        }

        var normalizedView = view with { CameraDirection = PlanetVector.Normalize(view.CameraDirection) };
        var maximumLevel = Math.Min(options.MaxLevel, view.CameraDistanceFromCenter switch
        {
            >= 1.14 => 3,
            >= 1.045 => 4,
            >= 1.012 => 5,
            _ => 6,
        });

        // Best-first subdivision concentrates geometry under the camera instead of refining
        // every visible region equally. Each split adds three tiles; a fixed budget prevents
        // pathological WebAssembly work at very low altitudes.
        var leaves = Enum.GetValues<CubeFace>().Select(face => new PlanetTileId(face, 0, 0, 0)).ToList();
        while (leaves.Count + 3 <= MaximumAdaptiveTiles)
        {
            var bestIndex = -1;
            var bestScore = options.TargetTileDiameterPixels;

            for (var index = 0; index < leaves.Count; index++)
            {
                var tile = leaves[index];
                if (tile.Level >= maximumLevel)
                {
                    continue;
                }

                var bounds = PlanetTileGeometry.CalculateBounds(tile);
                if (!IsVisible(bounds, normalizedView))
                {
                    continue;
                }

                var alignment = Math.Max(0.0, PlanetVector.Dot(normalizedView.CameraDirection, bounds.CenterDirection));
                var gazeWeight = 0.10 + (0.90 * Math.Pow(alignment, 8.0));
                var score = CalculateProjectedDiameterPixels(bounds, normalizedView) * gazeWeight;
                if (score > bestScore)
                {
                    bestIndex = index;
                    bestScore = score;
                }
            }

            if (bestIndex < 0)
            {
                break;
            }

            var selected = leaves[bestIndex];
            leaves.RemoveAt(bestIndex);
            leaves.AddRange(selected.Children());
        }

        return leaves.OrderBy(tile => tile.Face).ThenBy(tile => tile.Level).ThenBy(tile => tile.Y).ThenBy(tile => tile.X).ToArray();
    }

    private static IReadOnlyList<PlanetTileId> CreateGlobalCoverage(int level)
    {
        var tilesPerAxis = 1 << level;
        var result = new List<PlanetTileId>(6 * tilesPerAxis * tilesPerAxis);
        foreach (var face in Enum.GetValues<CubeFace>())
        {
            for (var y = 0; y < tilesPerAxis; y++)
            {
                for (var x = 0; x < tilesPerAxis; x++)
                {
                    result.Add(new PlanetTileId(face, level, x, y));
                }
            }
        }

        return result;
    }

    private bool IsVisible(PlanetTileBounds bounds, PlanetSurfaceView view)
    {
        var horizonAngle = Math.Acos(1.0 / view.CameraDistanceFromCenter);
        var centerAngle = Math.Acos(Math.Clamp(PlanetVector.Dot(view.CameraDirection, bounds.CenterDirection), -1.0, 1.0));
        return centerAngle <= horizonAngle + bounds.AngularRadiusRadians + options.HorizonPaddingRadians;
    }

    private static double CalculateProjectedDiameterPixels(PlanetTileBounds bounds, PlanetSurfaceView view)
    {
        var cameraPosition = view.CameraDirection * view.CameraDistanceFromCenter;
        var tileCenter = bounds.CenterDirection;
        var distanceToTileCenter = (cameraPosition - tileCenter).Length;
        var chordRadius = 2.0 * Math.Sin(bounds.AngularRadiusRadians * 0.5);
        var projectedAngle = 2.0 * Math.Atan2(chordRadius, Math.Max(distanceToTileCenter, 0.000001));
        return projectedAngle / view.VerticalFieldOfViewRadians * view.ViewportHeightPixels;
    }

    private static void Validate(PlanetSurfaceView view)
    {
        if (!double.IsFinite(view.CameraDistanceFromCenter) || view.CameraDistanceFromCenter <= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(view), view.CameraDistanceFromCenter, "Camera distance must be finite and greater than the unit planet radius.");
        }

        if (view.ViewportHeightPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(view), view.ViewportHeightPixels, "Viewport height must be greater than zero.");
        }

        if (!double.IsFinite(view.VerticalFieldOfViewRadians) || view.VerticalFieldOfViewRadians <= 0.0 || view.VerticalFieldOfViewRadians >= Math.PI)
        {
            throw new ArgumentOutOfRangeException(nameof(view), view.VerticalFieldOfViewRadians, "Vertical field of view must be between zero and pi radians.");
        }
    }
}