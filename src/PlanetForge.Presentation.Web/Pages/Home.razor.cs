using Microsoft.JSInterop;
using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Presentation.Web.Pages;

public partial class Home
{
    private string FormationTime
    {
        get
        {
            var seconds = playableDemo ? (long)demoReadout.Seconds : Experience.PublishedOriginEpoch is { } epoch ? (long)PlanetFormationSimulation.ElapsedSeconds(epoch.Clock) : 0L;
            return $"{(playableDemo ? "Demo " : "")}T+{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
        }
    }

    private string FormationPhase
    {
        get
        {
            if (playableDemo) return demoReadout.Phase;
            var world = Experience.PublishedRegionalRock;
            if (world?.Columns is not { } columns) return "Initializing planet";
            var melts = world.Field.Cells.Select(c => world.ResolveSurfacePhase(c.Id).MeltFraction).ToArray();
            if (melts.All(f => f >= 0.999)) return "Molten surface";
            if (melts.Any(f => f > 0.0)) return "Surface crystallizing";
            return columns.Columns.Any(c => c.Layers[^1].Resolve(columns.Material).MeltFraction > 0.0)
                ? "Solid surface · molten interior" : "Solid rock";
        }
    }

    private void ToggleFormationPlayback() => formationPlaying = !formationPlaying;

    private async Task RunFormationLoopAsync()
    {
        try
        {
            while (!disposeCancellationTokenSource.IsCancellationRequested)
            {
                await Task.Delay(250, disposeCancellationTokenSource.Token);
                if (formationPlaying) await AdvanceFormationAsync();
            }
        }
        catch (OperationCanceledException) when (disposeCancellationTokenSource.IsCancellationRequested) { }
    }

    private async Task AdvanceFormationAsync()
    {
        if (!continuousSimulation || formationBusy || rendererModule is null) return;
        formationBusy = true;
        formationStatus = string.Empty;
        StateHasChanged();
        try
        {
            await Task.Delay(16, disposeCancellationTokenSource.Token); // Present busy state before the bounded physics batch.
            if (playableDemo)
                demoReadout = await rendererModule.InvokeAsync<DemoReadout>("advance", disposeCancellationTokenSource.Token, formationStepSeconds);
            else
            {
                snapshot = await PlanetFormationSimulation.AdvanceAsync(Experience, formationStepSeconds, disposeCancellationTokenSource.Token);
                await SyncPlanetAsync();
            }
        }
        catch (OperationCanceledException) when (disposeCancellationTokenSource.IsCancellationRequested) { }
        catch (Exception error)
        {
            formationPlaying = false;
            formationStatus = "Simulation paused: this step could not be accepted.";
            Console.Error.WriteLine(error);
        }
        finally
        {
            formationBusy = false;
            if (!disposeCancellationTokenSource.IsCancellationRequested) StateHasChanged();
        }
    }

    private async Task RestartFormationAsync()
    {
        if (formationBusy || !continuousSimulation) return;
        formationPlaying = false;
        formationBusy = true;
        StateHasChanged();
        try
        {
            await Task.Delay(16, disposeCancellationTokenSource.Token);
            if (playableDemo && rendererModule is not null)
                demoReadout = await rendererModule.InvokeAsync<DemoReadout>("restart", disposeCancellationTokenSource.Token, formationSeed);
            else
            {
                snapshot = PlanetFormationSimulation.Start(Experience, formationSeed);
                await SyncPlanetAsync();
            }
            formationStatus = string.Empty;
        }
        catch (OperationCanceledException) when (disposeCancellationTokenSource.IsCancellationRequested) { }
        catch (Exception error)
        {
            formationStatus = "The planet could not be restarted.";
            Console.Error.WriteLine(error);
        }
        finally
        {
            formationBusy = false;
            if (!disposeCancellationTokenSource.IsCancellationRequested) StateHasChanged();
        }
    }

    private sealed class DemoReadout
    {
        public DemoReadout() { }
        public double Seconds { get; set; }
        public double Coverage { get; set; }
        public string Phase { get; set; } = "Magma ocean";
    }

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
        if (regionalStreamingEnabled && regionalCatalogReady)
        {
            lastRegionalCameraView = view;
            _ = ObserveRegionalCameraAsync(view, snapshot.Seed);
        }
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
