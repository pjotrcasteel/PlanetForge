using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PlanetForge.Bootstrap;
using PlanetForge.Presentation.Web;
using PlanetForge.Presentation.Web.Infrastructure;
using PlanetForge.Infrastructure.Surface;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddPlanetForge();
builder.Services.AddScoped<IPlanetGeologicalRegionDocumentStore, IndexedDbGeologicalRegionStore>();
builder.Services.AddScoped<PlanetGeologicalRegionCache>();
builder.Services.AddScoped<PlanetGeologicalRegionRepository>();

await builder.Build().RunAsync();
