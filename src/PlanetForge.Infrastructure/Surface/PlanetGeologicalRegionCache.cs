using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Bounded in-memory LRU of geological checkpoints. It does not generate
/// physics on lookup and never mutates the canonical planet. Evicted entries
/// can be reconstructed by importing their archive from durable storage.
/// </summary>
public sealed class PlanetGeologicalRegionCache
{
    private readonly object gate = new();
    private readonly int capacity;
    private readonly Dictionary<(PlanetGeologicalRegionId Region, PlanetGeologicalEpoch Epoch),
        LinkedListNode<PlanetGeologicalRegionHistory>> lookup = [];
    private readonly LinkedList<PlanetGeologicalRegionHistory> recent = [];

    public PlanetGeologicalRegionCache(int capacity = 8)
    {
        if (capacity is < 1 or > 1_024)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        this.capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return lookup.Count;
            }
        }
    }

    public void Put(PlanetGeologicalRegionHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var copy = history.Copy();
        var key = (copy.Region, copy.Epoch);
        lock (gate)
        {
            if (lookup.Remove(key, out var previous))
            {
                recent.Remove(previous);
            }

            var node = recent.AddFirst(copy);
            lookup.Add(key, node);
            if (recent.Count > capacity)
            {
                var oldest = recent.Last!;
                lookup.Remove((oldest.Value.Region, oldest.Value.Epoch));
                recent.RemoveLast();
            }
        }
    }

    public bool TryGet(PlanetGeologicalRegionId region, PlanetGeologicalEpoch epoch, out PlanetGeologicalRegionHistory? history)
    {
        lock (gate)
        {
            if (!lookup.TryGetValue((region, epoch), out var node))
            {
                history = null;
                return false;
            }

            recent.Remove(node);
            recent.AddFirst(node);
            history = node.Value.Copy();
            return true;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            recent.Clear();
            lookup.Clear();
        }
    }
}
