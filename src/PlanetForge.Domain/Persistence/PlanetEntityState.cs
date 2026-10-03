using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Persistence;

public sealed record PlanetEntityState
{
    public PlanetEntityState(PlanetEntityId id, string kind, PlanetSurfaceAddress currentSurfaceAddress)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("Entity ID is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("Entity kind is required.", nameof(kind));
        }

        _ = PlanetSurfaceAddressing.Decode(currentSurfaceAddress);
        Id = id;
        Kind = kind.Trim();
        CurrentSurfaceAddress = currentSurfaceAddress;
    }

    public PlanetEntityId Id { get; }

    public string Kind { get; }

    public PlanetSurfaceAddress CurrentSurfaceAddress { get; }

    public PlanetEntityState MoveTo(PlanetSurfaceAddress address) => new(Id, Kind, address);
}