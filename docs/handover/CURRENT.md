# PlanetForge — active continuation state

**Workstream:** `PF-EARTH-20261013-A`  
**Last documented:** 2026-10-13  
**GitHub state checked at handover creation; RE-QUERY BEFORE ANY CODE OR RELEASE.**

## Baseline: deployed and validated

- GitHub: https://github.com/pjotrcasteel/PlanetForge
- Latest verified `main` SHA: `6f5f38ce61a31873029b3e41bcd2be8a66cc8310`
- Displayed browser version: **PF 0.0.40.2**
- GitHub Pages validation: **success**, workflow run [38387990214](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38387990214), includes publish, deployment, and production smoke.
- Main 3D planet uses opt-in `?visualTest=1&regionalRock=1` to inspect conserved regional-rock phase. Regular `?visualTest=1` is the separate Snowball experiment; **not** the primordial-Earth timeline.
- Human iPhone visual acceptance: on PF 0.0.40.2, thermal HUD compact and readable, bottom Advance/Reset/Back/Uniform Reference buttons visible, switches/reset operate, T+1/T+2 updates accepted. The planet **still does NOT look like a true molten/magma-ocean world**. See detailed snapshot.

## Completed work

| PR | Verified outcome |
| --- | --- |
| [#195](https://github.com/pjotrcasteel/PlanetForge/pull/195) | Seam-safe spatial interpolation of existing thermal cells; merged |
| [#197](https://github.com/pjotrcasteel/PlanetForge/pull/197) | Conservative lateral spherical heat exchange optional in canonical epoch; merged; existing visual scenario does **not** enable lateral conductivity |
| [#198](https://github.com/pjotrcasteel/PlanetForge/pull/198) | Blazor isolated CSS loaded; compact iPhone HUD; merged |
| [#199](https://github.com/pjotrcasteel/PlanetForge/pull/199) | Production smoke now checks version dynamically and verifies actual scoped CSS; merged |
| [#200](https://github.com/pjotrcasteel/PlanetForge/pull/200) | iPhone viewport/control recovery and bounds/hit tests; merged and deployed as PF 0.0.40.2 |

**After handover PR #201:** the active workstream documentation is merged into main (`b9eec395`); re-query Pages status before claiming its release is verified. The new `feature/magma-ocean-initial-reference-0.0.41` branch was fast-forwarded onto that main commit and now contains an **unreleased** PF 0.0.41.0 implementation: a separately funded fully molten canonical surface-shell initial scenario, opt-in same-globe query `?magmaOcean=1`, phase-based molten material rendering, C#/browser tests and [scientific limitations](../earth-origin-magma-ocean-initial-reference.md). This is **NOT merged/deployed/visually accepted** until GitHub confirms the separate gates. The original `?regionalRock=1` experiment and Snowball must remain unchanged.

## Current task: credible molten starting surface

User's visual feedback: the regional planet is mostly orange-tinted existing barren bedrock, plus one small brighter spot. This is not the target. Investigated source in `src/PlanetForge.Application/Planets/PlanetRegionalCoolingReference.cs`: initial surface starts at 1600 K, with material solidus 1300 K and liquidus 1900 K, then shifts only 1.2% of initial enthalpy into a small hotspot. This intentionally creates a scientific **contrast/reference** and a 48–100% melt range rather than a globally molten scenario. Renderer `src/PlanetForge.Presentation.Web/wwwroot/js/planetRenderer.js` uses melt-dependent orange blend over `barrenRockMaterial`, so molten regions currently look like reddish terrain, not a magma-dominated surface.

**Implementation underway, not yet released:** the named molten scenario is coded on the feature branch with surface-shell enthalpy initially funded at 2200 K, bulk-mantle reference 2400 K and a smooth conservative 8% lateral enthalpy redistribution. Renderer switches from dry-bedrock albedo to liquid optical material where the **canonical melt fraction** is effectively 100%. Added C#/Playwright tests and scientific limits. **NEXT:** finish CI and correct any failures; inspect published globe + LOD; merge through green PR; verify exact-sha Pages; ask user for T+0/T+1h screenshots. Next scientific milestone thereafter: mantle/surface coupling and persistent primary crust; no fake black crust masks.

The exact Earth Hadean thermal state is uncertain, so label this an explicit scenario/hypothesis with parameters, not the true history. Earth-scale magma circulation, deep convection, crust differentiation, steam EOS, outgassing, precipitation and first life remain future physical milestones.

## Next ten high-level gates

See [roadmap issue #196](https://github.com/pjotrcasteel/PlanetForge/issues/196): (1) molten regional visual integrity, (2) spatial heat transport, (3) primary crust, (4) geology/tectonic regimes, (5) radiative hot atmosphere, (6) volatile delivery/outgassing, (7) physical clouds/rain, (8) oceans/lakes/rivers, (9) climate/ice/erosion, (10) prebiotic conditions. A named milestone may require multiple PRs. The initial **magma-ocean scenario** is needed before claiming stage 1 visually accepted.

## Execution and communication

- Developer has delegated implementation sequencing, CI/CD, merging/release and testing. Keep working until a meaningful **new visual inspection** is ready or a genuinely essential user decision is needed; don't request a repeated “continue.”
- Scope: .NET 10, MSTest, Blazor WASM, WebGL2, Playwright, GitHub Actions/Pages. Preserve existing regional terrain/Hero geology and planet LOD 0–3.
- Always build/test, run browser checks, merge green PR, verify Pages SHA/visible version/production smoke. If any check fails, fix in code and re-run.
- Never assert future autonomous/background work unless a scheduling mechanism is explicitly used. Work synchronously when a session is active.
- Each milestone's PR updates **this file**. Create a separate snapshot when handing off chats.
