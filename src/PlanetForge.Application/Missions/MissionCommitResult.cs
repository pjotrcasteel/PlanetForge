namespace PlanetForge.Application.Missions;

public sealed record MissionCommitResult(MissionSnapshot Mission, IReadOnlyList<MissionTimelineFrame> Frames);