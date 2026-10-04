namespace PlanetForge.Application.Runs;

public sealed record PlanetWaterPredictionOption(PlanetWaterPrediction Prediction, string Label, string Hint);

public sealed record PlanetWaterChallengeDefinition(int Number, string Title, string Question, IReadOnlyList<PlanetWaterPredictionOption> Options);

public sealed record PlanetWaterPredictionResult(
    int ChallengeNumber,
    PlanetWaterPrediction Prediction,
    bool Correct,
    int InsightAwarded,
    string Headline,
    string Explanation);
