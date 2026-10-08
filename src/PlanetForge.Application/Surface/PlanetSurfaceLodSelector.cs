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

        var normalizedDirection = PlanetVector.Normalize(view.CameraDirection);
        var normalizedView = view with { CameraDirection = normalizedDirection };

        // The LOD cap grows with proximity, but only the visible area is refined.
        // A strict tile budget keeps synchronous Blazor WebAssembly mesh generation viable on phones.
        var permittedLevel = Math.Min(options.MaxLevel, view.CameraDistanceFromCenter switch
        {
            >= 1.14 => 3,
            >= 1.045 => 4,
            >= 1.012 => 5,
            _ => 6,
        });

        for (var level = permittedLevel; level >= 0; level--)
        {
            var targetDiameter = options.TargetTileDiameterPixels;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var result = new List<PlanetTileId>();

                foreach (var face in Enum.GetValues<CubeFace>())
                {
                    SelectRecursive(new PlanetTileId(face, 0, 0, 0), normalizedView, result, level, targetDiameter);
                }

                if (result.Count <= MaximumAdaptiveTiles)
                {
                    return result;
                }

                targetDiameter *= 1.5;
            }
        }

        return Enum.GetValues<CubeFace>().Select(face => new PlanetTileId(face, 0, 0, 0)).ToArray();
    }

    private void SelectRecursive(PlanetTileId id, PlanetSurfaceView view, List<PlanetTileId> result, int maximumLevel, double targetDiameterPixels)
    {
        var bounds = PlanetTileGeometry.CalculateBounds(id);
        if (!IsVisible(bounds, view))
        {
            result.Add(id);
            return;
        }

        var projectedDiameter = CalculateProjectedDiameterPixels(bounds, view);
        if (id.Level < maximumLevel && projectedDiameter > targetDiameterPixels)
        {
            foreach (var child in id.Children())
            {
                SelectRecursive(child, view, result, maximumLevel, targetDiameterPixels);
            }

            return;
        }

        result.Add(id);
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