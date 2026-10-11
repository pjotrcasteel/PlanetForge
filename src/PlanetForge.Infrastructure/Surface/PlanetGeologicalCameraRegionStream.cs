using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Opt-in camera-driven restoration of one nearby, already archived region.
/// No automatic erosion, no fabricated geology, no per-frame IndexedDB loads.
/// New camera selections invalidate pending reads and atomically publish only
/// validated archives. Uncovered territory retains canonical bedrock.
/// </summary>
public sealed class PlanetGeologicalCameraRegionStream(
    PlanetGeologicalRegionRepository repository,
    PlanetGeologicalRegionSession session)
{
    private readonly object gate = new();
    private long requestGeneration;
    private PlanetGeologicalRegionId? active;
    private PlanetGeologicalRegionId? pending;
    private PlanetGeologicalRegionId? lastMiss;
    private PlanetVector? lastMissDirection;

    public PlanetGeologicalRegionId? ActiveRegion
    {
        get
        {
            lock (gate)
            {
                return active;
            }
        }
    }

    public async Task<bool> ObserveAsync(int seed, PlanetSurfaceView view,
        IReadOnlyList<PlanetGeologicalRegionId> savedRegions, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = PlanetGeologicalCameraRegionSelector.Select(savedRegions, seed, view);
        long generation;
        bool clear;
        lock (gate)
        {
            if ((target == active && pending is null) ||
                (target is not null && (target == pending || target == lastMiss && pending is null && active is null &&
                     lastMissDirection is not null &&
                     PlanetVector.Dot(PlanetVector.Normalize(view.CameraDirection), lastMissDirection.Value) > Math.Cos(0.002))))
            {
                return false;
            }

            generation = checked(++requestGeneration);
            clear = active is not null || pending is not null;
            active = null;
            pending = target;
            lastMiss = null;
            lastMissDirection = null;
            // Keep invalidation ordered with camera generations. Otherwise
            // an older request could clear newer geometry after it publishes.
            if (clear)
            {
                session.Clear();
            }
        }

        if (target is null)
        {
            return clear;
        }

        try
        {
            var history = await repository.LoadAsync(target.Value, PlanetGeologicalEpoch.Present, cancellationToken);
            EnsureCurrent(generation, cancellationToken);
            // A tile address can describe a much larger area than the actual
            // evolved erosion grid. Do not load a tiny geological patch merely
            // because the camera is on the same cube-sphere tile.
            if (history is null || history.CreateOverlay().SampleWeightedDeltaMeters(
                    PlanetVector.Normalize(view.CameraDirection), seed).Weight <= 0.0)
            {
                lock (gate)
                {
                    EnsureCurrent(generation, cancellationToken);
                    pending = null;
                    lastMiss = target;
                    lastMissDirection = PlanetVector.Normalize(view.CameraDirection);
                }

                return clear;
            }

            await session.RestoreAsync([target.Value], PlanetGeologicalEpoch.Present, cancellationToken);
            lock (gate)
            {
                EnsureCurrent(generation, cancellationToken);
                pending = null;
                active = target;
            }

            return true;
        }
        catch
        {
            lock (gate)
            {
                if (requestGeneration == generation)
                {
                    pending = null;
                    lastMiss = target;
                    lastMissDirection = PlanetVector.Normalize(view.CameraDirection);
                }
            }

            throw;
        }
    }

    public bool Reset()
    {
        lock (gate)
        {
            var changed = active is not null || pending is not null;
            requestGeneration = checked(requestGeneration + 1);
            active = null;
            pending = null;
            lastMiss = null;
            lastMissDirection = null;
            if (changed)
            {
                session.Clear();
            }

            return changed;
        }
    }

    private void EnsureCurrent(long generation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref requestGeneration) != generation)
        {
            throw new OperationCanceledException("Camera moved before archived geology finished restoring.");
        }
    }
}
