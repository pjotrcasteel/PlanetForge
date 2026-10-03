using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface;

public sealed class PlanetSurfaceLodSelector(PlanetSurfaceLodOptions options)
{
    public IReadOnlyList<PlanetTileId> Select(PlanetSurfaceView view)
    {
        Validate(view);
        var normalizedDirection = PlanetVector.Normalize(view.CameraDirection);
        var normalizedView = view with { CameraDirection = normalizedDirection };
        var result = new List<PlanetTileId>();

        foreach (var face in Enum.GetValues<CubeFace>())
        {
            SelectRecursive(new PlanetTileId(face, 0, 0, 0), normalizedView, result);
        }

        return result;
    }

    private void SelectRecursive(PlanetTileId id, PlanetSurfaceView view, List<PlanetTileId> result)
    {
        var bounds = PlanetTileGeometry.CalculateBounds(id);
        if (!IsVisible(bounds, view))
        {
            result.Add(id);
            return;
        }

        var projectedDiameter = CalculateProjectedDiameterPixels(bounds, view);
        if (id.Level < options.MaxLevel && projectedDiameter > options.TargetTileDiameterPixels)
        {
            foreach (var child in id.Children())
            {
                SelectRecursive(child, view, result);
            }

            return;
        }

        result.Add(id);
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