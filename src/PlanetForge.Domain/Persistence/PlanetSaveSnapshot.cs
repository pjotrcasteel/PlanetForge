namespace PlanetForge.Domain.Persistence;

public sealed class PlanetSaveSnapshot
{
    private readonly IReadOnlyList<PlanetEntityState> entities;

    public PlanetSaveSnapshot(PlanetSaveHeader header, IReadOnlyList<PlanetEntityState> entities)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(entities);

        var entityIds = new HashSet<PlanetEntityId>();
        foreach (var entity in entities)
        {
            ArgumentNullException.ThrowIfNull(entity);
            if (!entityIds.Add(entity.Id))
            {
                throw new ArgumentException($"Duplicate entity ID '{entity.Id}'.", nameof(entities));
            }
        }

        Header = header;
        this.entities = entities.ToArray();
    }

    public PlanetSaveHeader Header { get; }

    public IReadOnlyList<PlanetEntityState> Entities => entities;
}