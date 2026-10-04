using PlanetForge.Application.Missions;

namespace PlanetForge.Application.Runs;

public sealed record PlanetRunCommitResult(PlanetRunSnapshot Run, IReadOnlyList<MissionTimelineFrame> Frames);