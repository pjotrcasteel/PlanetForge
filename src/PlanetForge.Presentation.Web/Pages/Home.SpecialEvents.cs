using Microsoft.JSInterop;

namespace PlanetForge.Presentation.Web.Pages;

public partial class Home
{
    private PlanetSpecialEventStatus? specialEventsStatus;

    private static string FormatEventEpoch(double seconds)
    {
        const double secondsPerYear = 31_557_600.0;
        var years = seconds / secondsPerYear;
        return years >= 1_000_000.0 ? $"T+{years / 1_000_000.0:0.###} Myr"
            : years >= 1_000.0 ? $"T+{years / 1_000.0:0.##} kyr"
            : years >= 1.0 ? $"T+{years:0.##} years" : $"T+{seconds:0} s";
    }

    // Geological eons are display context, derived from elapsed model time
    // against a 4.567-Gyr formation age. They do not declare a new model event.
    private static string TimelineEon(double planetSeconds)
    {
        const double secondsPerYear = 31_557_600.0;
        const double earthAgeYears = 4_567_000_000.0;
        var elapsedYears = double.IsFinite(planetSeconds)
            ? Math.Clamp(planetSeconds / secondsPerYear, 0.0, earthAgeYears) : 0.0;
        var remainingGa = (earthAgeYears - elapsedYears) / 1_000_000_000.0;
        return remainingGa >= 4.0 ? "HADEAN"
            : remainingGa >= 2.5 ? "ARCHEAN"
            : remainingGa >= 0.5388 ? "PROTEROZOIC" : "PHANEROZOIC";
    }

    // A linear 4.567-Gyr rail hides early impacts at one pixel. Log time
    // distributes witnessed events without changing their real epochs.
    private static string TimelinePosition(double planetSeconds)
    {
        const double secondsPerYear = 31_557_600.0;
        const double earthAgeYears = 4_567_000_000.0;
        var years = double.IsFinite(planetSeconds)
            ? Math.Clamp(planetSeconds / secondsPerYear, 0.0, earthAgeYears) : 0.0;
        var percent = 100.0 * Math.Log10(1.0 + years) / Math.Log10(1.0 + earthAgeYears);
        return percent.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

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

    private static string EventAuthorityLabel(string authority) => authority switch
    {
        "model-observed" => "MODEL OBSERVATION",
        "reconstruction" => "RECONSTRUCTION",
        "hypothesis" => "HYPOTHESIS",
        _ => "UNVERIFIED"
    };

    private sealed record PlanetEventInterval(double StartSeconds, double EndSeconds);

    private sealed record PlanetSpecialEvent(
        string Id, string Title, double PlanetSeconds, bool Inspectable,
        string Source, string Authority, string Trigger, string Uncertainty,
        PlanetEventInterval Interval, string Scale, int Priority, string Scope,
        string Limitations);

    private sealed record PlanetSpecialEventFocus(
        string Id, string Title, double ElapsedSeconds, double PlanetSeconds,
        string Scale, string TimeRate, string Source, string Authority,
        string Trigger, string Uncertainty, string Limitations);
}
