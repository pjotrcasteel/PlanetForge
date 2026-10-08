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
    private int orbitalAtlasRevision;

    private async Task RebuildOrbitalAtlasAsync()
    {
        if (rendererModule is null)
        {
            return;
        }

        const int width = 320;
        const int height = 160;
        var revision = ++orbitalAtlasRevision;
        var seed = CurrentSeed;
        var radius = snapshot?.PhysicalParameters.RadiusMeters ?? 6_371_000.0;
        var elevations = new double[width * height];
        var colors = new byte[width * height * 4];
        var longitudeStep = 2.0 * Math.PI / width;
        var latitudeStep = Math.PI / height;

        for (var y = 0; y < height; y++)
        {
            var latitude = Math.PI * (0.5 - (y + 0.5) / height);
            var cosLatitude = Math.Cos(latitude);

            for (var x = 0; x < width; x++)
            {
                var longitude = longitudeStep * (x + 0.5);
                var direction = new PlanetVector(cosLatitude * Math.Cos(longitude), Math.Sin(latitude), cosLatitude * Math.Sin(longitude));
                elevations[y * width + x] = ElevationSource.SampleElevationMeters(direction, seed);
            }

            if (y % 8 == 0)
            {
                await Task.Delay(1, disposeCancellationTokenSource.Token);
                if (revision != orbitalAtlasRevision)
                {
                    return;
                }
            }
        }

        for (var y = 0; y < height; y++)
        {
            var latitude = Math.PI * (0.5 - (y + 0.5) / height);
            var sine = Math.Sin(latitude);
            var cosine = Math.Cos(latitude);
            var metersPerLongitude = Math.Max(radius * cosine * longitudeStep, 1.0);
            var metersPerLatitude = radius * latitudeStep;

            for (var x = 0; x < width; x++)
            {
                var longitude = longitudeStep * (x + 0.5);
                var radial = new PlanetVector(cosine * Math.Cos(longitude), sine, cosine * Math.Sin(longitude));
                var east = new PlanetVector(-Math.Sin(longitude), 0, Math.Cos(longitude));
                var north = new PlanetVector(-sine * Math.Cos(longitude), cosine, -sine * Math.Sin(longitude));
                var left = elevations[y * width + (x + width - 1) % width];
                var right = elevations[y * width + (x + 1) % width];
                var upper = elevations[Math.Max(0, y - 1) * width + x];
                var lower = elevations[Math.Min(height - 1, y + 1) * width + x];
                var eastGradient = (right - left) / (2.0 * metersPerLongitude);
                var northGradient = (upper - lower) / (2.0 * metersPerLatitude);
                var normal = PlanetVector.Normalize(radial - (east * eastGradient) - (north * northGradient));
                var index = (y * width + x) * 4;
                colors[index] = EncodeUnitNormal(normal.X);
                colors[index + 1] = EncodeUnitNormal(normal.Y);
                colors[index + 2] = EncodeUnitNormal(normal.Z);
                colors[index + 3] = (byte)Math.Clamp((elevations[y * width + x] + 7_600.0) / 16_000.0 * 255.0, 0.0, 255.0);
            }

            if (y % 16 == 0)
            {
                await Task.Delay(1, disposeCancellationTokenSource.Token);
                if (revision != orbitalAtlasRevision)
                {
                    return;
                }
            }
        }

        if (revision == orbitalAtlasRevision)
        {
            await rendererModule.InvokeVoidAsync("setOrbitalAtlas", disposeCancellationTokenSource.Token, seed, width, height, colors);
        }
    }

    private static byte EncodeUnitNormal(double value) => (byte)Math.Clamp(Math.Round((value + 1.0) * 127.5), 0.0, 255.0);

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