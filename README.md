# PlanetForge

PlanetForge is a science-grounded, low-poly planetary terraforming game built in C# and playable directly in the browser.

## Current milestone

### 0.0.2 — Physical Planet

The planet now has a real physical baseline instead of arbitrary gameplay values:

- .NET 10 / C# core
- Blazor WebAssembly browser client
- Custom WebGL 2 rendering with no commercial game engine
- Procedural low-poly planet
- Editable orbital distance, stellar luminosity, Bond albedo, planet mass and planet radius
- Surface gravity from `g = GM / R²`
- Stellar irradiance from the inverse-square relation `S = L / (4πd²)`
- Equilibrium temperature from a zero-greenhouse global energy-balance model
- Mean density and escape velocity derived from mass and radius
- Earth-like reference preset
- Visual response to calculated irradiance and equilibrium temperature
- GitHub Actions validation and GitHub Pages deployment

Sea level and atmosphere thickness are still visual sandbox controls in 0.0.2. They become physical simulation inputs in the atmosphere/water milestone rather than being falsely presented as climate science now.

## Scientific baseline

PlanetForge distinguishes direct physical relations from simplified models. The 0.0.2 equilibrium-temperature calculation assumes uniform heat redistribution, emissivity 1 and no greenhouse warming.

Earth reference values are based on the NASA Earth Fact Sheet. Solar luminosity and astronomical scaling use standard IAU nominal values.

References:

- NASA NSSDC Earth Fact Sheet: https://nssdc.gsfc.nasa.gov/planetary/factsheet/earthfact.html
- IAU 2015 Resolution B3, nominal solar and planetary conversion constants: https://www.iau.org/static/resolutions/IAU2015_English.pdf

## Principles

- Real formulas and scientific theories form the simulation foundation.
- Scientific models will become inspectable and programmable through Planet Logic blocks.
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
