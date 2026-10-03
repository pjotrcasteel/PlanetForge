using Microsoft.JSInterop;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Presentation.Web.Pages;

public partial class Home
{
    private DotNetObjectReference<Home>? surfaceLodReference;

    [JSInvokable("UpdateSurfaceView")]
    public PlanetRenderSnapshot UpdateSurfaceView(
        double directionX,
        double directionY,
        double directionZ,
        double cameraDistanceFromCenter,
        int viewportHeightPixels,
        double verticalFieldOfViewRadians)
    {
        var view = new PlanetSurfaceView(
            new PlanetVector(directionX, directionY, directionZ),
            cameraDistanceFromCenter,
            viewportHeightPixels,
            verticalFieldOfViewRadians);

        snapshot = Experience.UpdateSurfaceView(view);
        return snapshot;
    }

    private DotNetObjectReference<Home> GetOrCreateSurfaceLodReference() => surfaceLodReference ??= DotNetObjectReference.Create(this);

    private void DisposeSurfaceLodReference()
    {
        surfaceLodReference?.Dispose();
        surfaceLodReference = null;
    }
}
