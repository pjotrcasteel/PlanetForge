using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Presentation.Web.Pages;

public partial class Home
{
    private static string FormatFlux(double wattsPerSquareMeter) => wattsPerSquareMeter >= 1000.0
        ? $"{wattsPerSquareMeter / 1000.0:0.#} kW/m²"
        : $"{wattsPerSquareMeter * 1000.0:0} mW/m²";

    private async Task ToggleFormationPlaybackAsync()
    {
        if (!formationRendererReady || rendererModule is null || specialEventsStatus?.Focus is not null) return;
        formationPlaying = !formationPlaying;
        await rendererModule.InvokeVoidAsync("setEvolutionPlaying", disposeCancellationTokenSource.Token, formationPlaying);
    }

    private async Task ToggleFormationCloudsAsync()
    {
        if (!formationRendererReady || rendererModule is null) return;
        formationClouds = !formationClouds;
        await rendererModule.InvokeVoidAsync("setEvolutionClouds", disposeCancellationTokenSource.Token, formationClouds);
    }

    private async Task ChangeFormationSpeedAsync(ChangeEventArgs change)
    {
        if (!int.TryParse(change.Value?.ToString(), out var myrPerSecond) || myrPerSecond <= 0 || rendererModule is null || specialEventsStatus?.Focus is not null) return;
        formationMaximumMyrPerSecond = myrPerSecond;
        await rendererModule.InvokeVoidAsync("setEvolutionSpeed", disposeCancellationTokenSource.Token, myrPerSecond * 1e6);
    }

    // The planet evolves on the GPU every animation frame; Blazor only polls
    // its status for the HUD.
    private async Task RunFormationLoopAsync()
    {
        try
        {
            while (!disposeCancellationTokenSource.IsCancellationRequested)
            {
                await Task.Delay(250, disposeCancellationTokenSource.Token);
                if (rendererModule is null) continue;
                try
                {
                    evolution = await rendererModule.InvokeAsync<PlanetEvolutionStatus?>("getEvolutionStatus", disposeCancellationTokenSource.Token);
                    // Automatic key-moment inspection pauses the JS model and
                    // later resumes it. Keep the UI button truthful even when
                    // playback changed without a click in Blazor.
                    formationPlaying = evolution?.Playing ?? formationPlaying;
                    specialEventsStatus = await rendererModule.InvokeAsync<PlanetSpecialEventStatus?>("getSpecialEventStatus", disposeCancellationTokenSource.Token);
                    formationStatus = string.Empty;
                }
                catch (JSException error)
                {
                    formationStatus = "The planet evolution could not be read.";
                    Console.Error.WriteLine(error);
                }
                StateHasChanged();
            }
        }
        catch (OperationCanceledException) when (disposeCancellationTokenSource.IsCancellationRequested) { }
    }

    private async Task RestartFormationAsync()
    {
        if (!formationRendererReady || formationBusy || !continuousSimulation || rendererModule is null || specialEventsStatus?.Focus is not null) return;
        formationBusy = true;
        try
        {
            evolution = await rendererModule.InvokeAsync<PlanetEvolutionStatus>("restartEvolution", disposeCancellationTokenSource.Token, formationSeed);
            specialEventsStatus = await rendererModule.InvokeAsync<PlanetSpecialEventStatus>("getSpecialEventStatus", disposeCancellationTokenSource.Token);
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

    private sealed record PlanetEvolutionStatus(
        double TimeSeconds,
        string TimeLabel,
        string RateLabel,
        string Regime,
        string Phase,
        double PotentialKelvin,
        double CrystalFraction,
        double HeatFluxWattsPerSquareMeter,
        string? CrustLifetimeLabel,
        double GlowingFraction,
        int Rafts,
        int Plates,
        int Plumes,
        double HeatPipeShare,
        string? LidOnsetLabel,
        string? PlateOnsetLabel,
        double FramesPerSecond,
        bool Playing,
        string[]? Liberties);

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