# PlanetForge

PlanetForge is a science-grounded, low-poly planetary terraforming game built in C# and playable directly in the browser.

## Current milestone

### 0.0.3 — Atmosphere & Water

PlanetForge now has its first coupled environmental simulation:

- .NET 10 / C# simulation core
- Blazor WebAssembly browser client
- Custom WebGL 2 rendering with no commercial game engine
- Procedural low-poly planet
- Orbital distance, stellar luminosity, Bond albedo, planet mass and radius
- Atmospheric mass and mean surface pressure
- Atmospheric composition with editable CO₂ concentration
- Logarithmic CO₂ radiative forcing
- Simplified greenhouse surface-temperature model
- Total planetary water inventory
- Pressure/temperature water phase partitioning into ice, liquid and vapor
- Triple-point constraint for stable liquid water
- Pressure-dependent boiling-point approximation
- Atmosphere, oceans, ice and steam/desiccation visuals derived from simulation state
- Earth-like reference preset
- GitHub Actions validation and GitHub Pages deployment

There are no manual sea-level or atmosphere-visual sliders anymore. The rendered planet now follows the coupled simulation output.

## Scientific model transparency

PlanetForge distinguishes direct physical relations, empirical relationships and simplified simulation models.

Current direct/physical relations include:

- Surface gravity: `g = GM / R²`
- Stellar irradiance: `S = L / (4πd²)`
- Mean surface pressure: `p = Mₐg / (4πR²)`
- Zero-greenhouse equilibrium temperature from global radiative balance

Current empirical/simplified models include:

- CO₂ radiative forcing: `ΔF = 5.35 ln(C / C₀)`
- A deliberately simplified greenhouse temperature response
- A global water-phase partition model rather than a full atmosphere/ocean circulation model
- A Clausius–Clapeyron-based boiling-point approximation

These approximations are intentionally explicit. Later milestones will turn scientific models into inspectable and editable Planet Logic blocks rather than hiding assumptions inside gameplay code.

## References

- NASA NSSDC Earth Fact Sheet: https://nssdc.gsfc.nasa.gov/planetary/factsheet/earthfact.html
- IAU 2015 Resolution B3, nominal solar and planetary conversion constants: https://www.iau.org/static/resolutions/IAU2015_English.pdf
- IPCC radiative forcing literature for the logarithmic CO₂ relationship

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
