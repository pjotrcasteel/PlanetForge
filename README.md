# PlanetForge

PlanetForge is a science-grounded, low-poly planetary terraforming game built in C# and playable directly in the browser.

## Current milestone

### 0.0.1 — Living Rock

The first milestone establishes the technical and visual foundation:

- .NET 10 / C# core
- Blazor WebAssembly browser client
- Custom WebGL rendering with no commercial game engine
- Procedural low-poly planet
- Mouse, touch and wheel camera controls
- Seeded terrain, ocean and atmosphere visuals
- Directional sunlight
- Small interactive planet controls
- GitHub Actions validation
- GitHub Pages deployment

## Principles

- Real formulas and scientific theories form the long-term simulation foundation.
- Scientific models will eventually be inspectable and programmable through Planet Logic blocks.
- Rendering is a thin presentation concern; simulation and game logic stay framework-independent.
- No Unity, paid engine, paid-required package, runtime royalty, or mandatory commercial service.
- Dependencies must be free/open-source and license-compatible.

## Run locally

```bash
dotnet restore PlanetForge.slnx
dotnet run --project src/PlanetForge.Presentation.Web/PlanetForge.Presentation.Web.csproj
```

## Test

```bash
dotnet test PlanetForge.slnx
```
