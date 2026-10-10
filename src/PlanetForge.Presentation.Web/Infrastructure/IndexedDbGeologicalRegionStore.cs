using Microsoft.JSInterop;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Presentation.Web.Infrastructure;

/// <summary>
/// Durable geological region storage in the browser's IndexedDB, independent
/// of the existing gameplay-save key. Explicitly invoked by the repository;
/// opening the planet never publishes experimental geology automatically.
/// </summary>
public sealed class IndexedDbGeologicalRegionStore(IJSRuntime javascript) : IPlanetGeologicalRegionDocumentStore, IAsyncDisposable
{
    private readonly SemaphoreSlim moduleGate = new(1, 1);
    private IJSObjectReference? module;

    public async Task<string?> LoadAsync(string key, CancellationToken cancellationToken)
    {
        var store = await GetModuleAsync(cancellationToken);
        return await store.InvokeAsync<string?>("loadArchive", cancellationToken, key);
    }

    public async Task SaveAsync(string key, string json, CancellationToken cancellationToken)
    {
        var store = await GetModuleAsync(cancellationToken);
        await store.InvokeVoidAsync("saveArchive", cancellationToken, key, json);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var store = await GetModuleAsync(cancellationToken);
        await store.InvokeVoidAsync("deleteArchive", cancellationToken, key);
    }

    private async Task<IJSObjectReference> GetModuleAsync(CancellationToken cancellationToken)
    {
        await moduleGate.WaitAsync(cancellationToken);
        try
        {
            module ??= await javascript.InvokeAsync<IJSObjectReference>(
                "import", cancellationToken, "./js/geologicalRegionStore.js");
            return module;
        }
        finally
        {
            moduleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            try
            {
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Browser navigation already released the JavaScript module.
            }
        }

        moduleGate.Dispose();
    }
}
