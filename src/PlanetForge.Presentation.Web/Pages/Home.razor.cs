using Microsoft.JSInterop;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Presentation.Web.Pages;

public partial class Home
{
    private DotNetObjectReference<Home>? surfaceLodReference;
    private PlanetLocalSurfaceMesh? lastFullLocalSurface;
    private int remainingGuaranteedFullDeliveries;

    [JSInvokable("UpdateSurfaceView")]
    public PlanetRenderSnapshot UpdateSurfaceView(
        double directionX,
        double directionY,
        double directionZ,
        double cameraDistanceFromCenter,
        int viewportWidthPixels,
        int viewportHeightPixels,
        double verticalFieldOfViewRadians)
    {
        var view = new PlanetSurfaceView(
            new PlanetVector(directionX, directionY, directionZ),
            cameraDistanceFromCenter,
            viewportHeightPixels,
            verticalFieldOfViewRadians)
        {
            ViewportAspectRatio = viewportWidthPixels / (double)viewportHeightPixels,
        };

        snapshot = EnsureLocalSurfaceDelivery(Experience.UpdateSurfaceView(view));
        return snapshot;
    }

    [JSInvokable("MoveLocalSurfaceAnchor")]
    public PlanetRenderSnapshot MoveLocalSurfaceAnchor(double eastMeters, double northMeters)
    {
        snapshot = EnsureLocalSurfaceDelivery(Experience.MoveLocalSurfaceAnchor(eastMeters, northMeters));
        return snapshot;
    }

    private PlanetRenderSnapshot EnsureLocalSurfaceDelivery(PlanetRenderSnapshot nextSnapshot)
    {
        var localSurface = nextSnapshot.LocalSurface;
        if (localSurface is null)
        {
            lastFullLocalSurface = null;
            remainingGuaranteedFullDeliveries = 0;
            return nextSnapshot;
        }

        if (localSurface.PositionsMeters.Length > 0)
        {
            lastFullLocalSurface = localSurface;
            remainingGuaranteedFullDeliveries = 1;
            return nextSnapshot;
        }

        if (remainingGuaranteedFullDeliveries <= 0 || lastFullLocalSurface?.Key != localSurface.Key)
        {
            return nextSnapshot;
        }

        remainingGuaranteedFullDeliveries--;
        var guaranteedSurface = lastFullLocalSurface with { CameraAltitudeMeters = localSurface.CameraAltitudeMeters };
        return nextSnapshot with { LocalSurface = guaranteedSurface };
    }

    private DotNetObjectReference<Home> GetOrCreateSurfaceLodReference() => surfaceLodReference ??= DotNetObjectReference.Create(this);

    private void DisposeSurfaceLodReference()
    {
        surfaceLodReference?.Dispose();
        surfaceLodReference = null;
    }
}