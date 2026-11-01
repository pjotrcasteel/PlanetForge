using Microsoft.JSInterop;

namespace PlanetForge.Presentation.Web.Pages;

public partial class Home
{
    private PlanetSpecialEventStatus? specialEventsStatus;

    private static string FormatEventEpoch(double seconds)
    {
        const double secondsPerYear = 31_557_600.0;
        var years = seconds / secondsPerYear;
        if (years >= 1_000_000_000.0) return $"T+{years / 1_000_000_000.0:0.###} Gyr";
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
        return elapsedYears < 60_000_000.0 ? "ACCRETION"
            : remainingGa >= 4.0 ? "HADEAN"
            : remainingGa >= 2.5 ? "ARCHEAN"
            : remainingGa >= 0.5388 ? "PROTEROZOIC" : "PHANEROZOIC";
    }

    // E7-A: a true linear time camera. The narrow rail is locally to scale and
    // follows the model clock; the slim overview remains to scale across 4.567 Gyr.
    // Historical anchors are approximate evidence dates, NEVER model events.
    private const double TimelineSecondsPerYear = 31_557_600.0;
    private const double TimelineEarthAgeYears = 4_567_000_000.0;

    private sealed record TimelineChapter(
        string Key, string Title, double ElapsedYears, string EvidenceNote, bool IsPresent = false);

    private static readonly TimelineChapter[] TimelineChapters =
    [
        new("formation", "Accretion begins", 0, "First Solar System solids ~4.567 Ga; proto-Earth grows over tens of millions of years."),
        new("moon", "Moon-forming collision", 60_000_000,
            "The 60 Myr playback is a selected reconstruction. Actual giant-impact timing and Moon assembly remain uncertain; no SPH collision is solved."),
        new("first-crust", "Ancient crust evidence", 167_000_000,
            "Jack Hills zircons are about 4.4 billion years old; the first crust may be older."),
        new("early-oceans", "Early oceans", 267_000_000,
            "Evidence suggests liquid water by 4.3–4.4 billion years ago; exact onset is unknown."),
        new("first-life", "Earliest life evidence", 867_000_000,
            "Possible biological signals by 3.7 billion years ago; the actual origin of life is unknown."),
        new("oxygen", "Great Oxidation Event", 2_167_000_000,
            "Major atmospheric oxygenation around 2.4 billion years ago."),
        new("complex-cells", "Complex cells", 2_567_000_000,
            "Eukaryotic cells evolved roughly 2 billion years ago; dating is approximate."),
        new("animals", "First animals", 3_767_000_000,
            "Molecular evidence suggests animal origins around 800 million years ago."),
        new("life-on-land", "Early land animals", 4_137_000_000,
            "Land-animal fossils appear around 443–419 million years ago."),
        new("present", "Present day", TimelineEarthAgeYears, "Current day.", IsPresent: true)
    ];

    private sealed record TimelineLens(double StartYears, double SpanYears, double NowYears)
    {
        public double EndYears => StartYears + SpanYears;

        public bool Contains(double elapsedYears) =>
            elapsedYears >= StartYears && elapsedYears <= EndYears;

        public string CssPosition(double elapsedYears)
        {
            // Values beyond the visible lens are clipped; keep DOM nodes in the
            // model for previews, but avoid enormous offscreen CSS coordinates.
            var percent = Math.Clamp(100.0 * (elapsedYears - StartYears) / SpanYears, -100.0, 200.0);
            return percent.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }
    }

    private TimelineLens CurrentTimelineLens()
    {
        var seconds = evolution?.TimeSeconds ?? 0;
        var nowYears = double.IsFinite(seconds)
            ? Math.Clamp(seconds / TimelineSecondsPerYear, 0.0, TimelineEarthAgeYears)
            : 0.0;

        // Continuous zoom-out: first a kyr-scale model window, later a Gyr-scale
        // window. Equal horizontal distances always mean equal time IN the window.
        // At formation the cursor moves to 32%; then the strip scrolls under it.
        var spanYears = Math.Clamp(25_000.0 + 3.0 * nowYears, 25_000.0, 1_500_000_000.0);
        var startYears = Math.Max(0.0, nowYears - spanYears * 0.32);
        startYears = Math.Min(startYears, TimelineEarthAgeYears - spanYears);
        return new(startYears, spanYears, nowYears);
    }

    private static string GlobalTimelinePosition(double elapsedYears) =>
        (100.0 * elapsedYears / TimelineEarthAgeYears)
            .ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static string FormatTimelineYears(double years)
    {
        if (years >= 1_000_000_000.0) return $"{years / 1_000_000_000.0:0.##} Gyr";
        if (years >= 1_000_000.0) return $"{years / 1_000_000.0:0.##} Myr";
        if (years >= 1_000.0) return $"{years / 1_000.0:0.##} kyr";
        return $"{years:0} yr";
    }

    private static string AgeOfChapter(TimelineChapter chapter) =>
        chapter.IsPresent ? "Today"
            : $"{(TimelineEarthAgeYears - chapter.ElapsedYears) / 1_000_000_000.0:0.###} Ga ago";

    private TimelineChapter? selectedTimelineChapter;

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
