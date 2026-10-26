using Microsoft.JSInterop;

namespace PlanetForge.Presentation.Web.Pages;

public partial class Home
{
    private PlanetSpecialEventStatus? specialEventsStatus;

    private async Task EnterSpecialEventAsync(string eventId)
    {
        if (!formationRendererReady || rendererModule is null || specialEventsStatus?.Focus is not null) return;
        try
        {
            specialEventsStatus = await rendererModule.InvokeAsync<PlanetSpecialEventStatus>(
                "enterSpecialEvent", disposeCancellationTokenSource.Token, eventId);
            formationPlaying = false;
            formationStatus = string.Empty;
        }
        catch (OperationCanceledException) when (disposeCancellationTokenSource.IsCancellationRequested) { }
        catch (JSException error)
        {
            formationStatus = "Unable to inspect the chosen geological event.";
            Console.Error.WriteLine(error);
        }
    }

    private async Task ExitSpecialEventAsync()
    {
        if (rendererModule is null || specialEventsStatus?.Focus is null) return;
        try
        {
            specialEventsStatus = await rendererModule.InvokeAsync<PlanetSpecialEventStatus>(
                "exitSpecialEvent", disposeCancellationTokenSource.Token);
            evolution = await rendererModule.InvokeAsync<PlanetEvolutionStatus>(
                "getEvolutionStatus", disposeCancellationTokenSource.Token);
            formationPlaying = evolution?.Playing ?? false;
            formationStatus = string.Empty;
        }
        catch (OperationCanceledException) when (disposeCancellationTokenSource.IsCancellationRequested) { }
        catch (JSException error)
        {
            formationStatus = "Unable to return to planetary time.";
            Console.Error.WriteLine(error);
        }
    }

    private sealed record PlanetSpecialEventStatus(PlanetSpecialEvent[] Events, PlanetSpecialEventFocus? Focus);

    private sealed record PlanetSpecialEvent(string Id, string Title, double PlanetSeconds, string Limitations);

    private sealed record PlanetSpecialEventFocus(
        string Id, string Title, double ElapsedSeconds, double PlanetSeconds,
        string Scale, string TimeRate, string Limitations);
}
