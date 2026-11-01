using Microsoft.JSInterop;

namespace PlanetForge.Presentation.Web.Pages;

public partial class Home
{
    private PlanetSpecialEventStatus? specialEventsStatus;

    private static string FormatEventEpoch(double seconds)
    {
        const double secondsPerYear = 31_557_600.0;
        var years = seconds / secondsPerYear;
        if (years >= 1_000_000.0) return $"T+{years / 1_000_000.0:0.###} Myr";
        if (years >= 1_000.0) return $"T+{years / 1_000.0:0.##} kyr";
        if (years >= 1.0) return $"T+{years:0.##} years";
        if (seconds >= 86_400.0) return $"T+{seconds / 86_400.0:0.#} days";
        if (seconds >= 3_600.0) return $"T+{seconds / 3_600.0:0.#} hours";
        if (seconds >= 60.0) return $"T+{seconds / 60.0:0.#} min";
        return $"T+{seconds:0} s";
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

    // E7-A: the chapter rail is deliberately ordinal, not a fake time scale.
    // Exact sampled model times remain available in each recorded-event log.
    private sealed record TimelineChapter(
        string Key, string Title, int PositionPercent, bool IsPresent = false, bool IsModeled = false);

    private static readonly TimelineChapter[] TimelineChapters =
    [
        new("formation", "Earth forms", 0),
        new("solid-lid", "Rocky lid", 11, IsModeled: true),
        new("ocean-rainout", "First oceans", 22, IsModeled: true),
        new("plate-onset", "Plate regime (model)", 33, IsModeled: true),
        new("first-life", "First life", 44),
        new("oxygen", "Atmospheric oxygenation", 55),
        new("complex-cells", "Complex cells", 66),
        new("animals", "First animals", 77),
        new("life-on-land", "Life on land", 88),
        new("present", "Present day", 100, true)
    ];

    private TimelineChapter? selectedTimelineChapter;

    private PlanetSpecialEvent? GetRecordedEventForChapter(TimelineChapter chapter)
    {
        if (chapter.Key is "formation" or "present" || specialEventsStatus?.Events is null)
        {
            return null;
        }

        return Array.Find(specialEventsStatus.Events,
            recorded => recorded.Id.EndsWith("-" + chapter.Key, StringComparison.Ordinal));
    }

    private string TimelineNowPosition()
    {
        var latestPercent = 0;
        foreach (var chapter in TimelineChapters)
        {
            if (GetRecordedEventForChapter(chapter) is not null)
            {
                latestPercent = Math.Max(latestPercent, chapter.PositionPercent);
            }
        }
        return latestPercent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    private void SelectTimelineChapter(TimelineChapter chapter) => selectedTimelineChapter = chapter;
    private void DismissTimelineChapter() => selectedTimelineChapter = null;

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
