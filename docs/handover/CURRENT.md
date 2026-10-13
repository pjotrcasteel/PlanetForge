# PlanetForge — active continuation state

**Workstream:** `PF-EARTH-20261013-A`  
**Last documented:** 2026-10-13  
**GitHub state checked at handover creation; RE-QUERY BEFORE ANY CODE OR RELEASE.**

## Baseline: deployed and validated

- GitHub: https://github.com/pjotrcasteel/PlanetForge
- Science-code release `main` SHA: `74ba0612a6cd3169b6ec01972ebecce371cd5f5e` (#202); any later docs-only main SHA should be checked against this release
- Displayed browser version: **PF 0.0.41.0**
- GitHub Pages validation: **success**, run [38392739208](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38392739208), including exact-SHA publish, deployment, and live-browser smoke. Source PR CI [38390183995](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38390183995) also succeeded.
- Main 3D planet: **new opt-in fully molten starting-state** at `?visualTest=1&magmaOcean=1`; original `?visualTest=1&regionalRock=1` regional contrast reference still exists. Regular `?visualTest=1` is the separate Snowball experiment, **not** the primordial-Earth timeline.
- Human iPhone visual acceptance: **PF 0.0.40.2** thermal HUD/controls and T+1/T+2 accepted, but that earlier regional reference looked like tinted bedrock with one bright hotspot. **PF 0.0.41.0 magma-ocean visual quality is NOT YET HUMAN-ACCEPTED**: user was invited to inspect the new fully molten globe at T+0/T+1; wait for screenshots before closing the visible stage.

## Completed work

| PR | Verified outcome |
| --- | --- |
| [#195](https://github.com/pjotrcasteel/PlanetForge/pull/195) | Seam-safe spatial interpolation of existing thermal cells; merged |
| [#197](https://github.com/pjotrcasteel/PlanetForge/pull/197) | Conservative lateral spherical heat exchange optional in canonical epoch; merged; existing visual scenario does **not** enable lateral conductivity |
| [#198](https://github.com/pjotrcasteel/PlanetForge/pull/198) | Blazor isolated CSS loaded; compact iPhone HUD; merged |
| [#199](https://github.com/pjotrcasteel/PlanetForge/pull/199) | Production smoke now checks version dynamically and verifies actual scoped CSS; merged |
| [#200](https://github.com/pjotrcasteel/PlanetForge/pull/200) | iPhone viewport/control recovery and bounds/hit tests; merged and deployed as PF 0.0.40.2 |
| [#201](https://github.com/pjotrcasteel/PlanetForge/pull/201) | Persistent cross-chat handover and PR checklist; merged and deployed |
| [#202](https://github.com/pjotrcasteel/PlanetForge/pull/202) | PF 0.0.41.0: separately funded fully molten surface-shell initial scenario, main-globe liquid material, deterministic and browser tests; merged, production smoke green; **iPhone appearance pending acceptance** |

**After #201 and #202:** persistent documentation is merged and verified; the PF 0.0.41.0 magma-ocean feature is also merged as `74ba0612`, with full PR CI and exact-head GitHub Pages smoke green. The explicit molten starting scenario at `?visualTest=1&magmaOcean=1` is **deployed but not yet iPhone visually approved**. The old `?regionalRock=1` and Snowball remain available. Read [scientific limitations](../earth-origin-magma-ocean-initial-reference.md).

## Current task: credible molten starting surface

User's visual feedback: the regional planet is mostly orange-tinted existing barren bedrock, plus one small brighter spot. This is not the target. Investigated source in `src/PlanetForge.Application/Planets/PlanetRegionalCoolingReference.cs`: initial surface starts at 1600 K, with material solidus 1300 K and liquidus 1900 K, then shifts only 1.2% of initial enthalpy into a small hotspot. This intentionally creates a scientific **contrast/reference** and a 48–100% melt range rather than a globally molten scenario. Renderer `src/PlanetForge.Presentation.Web/wwwroot/js/planetRenderer.js` uses melt-dependent orange blend over `barrenRockMaterial`, so molten regions currently look like reddish terrain, not a magma-dominated surface.

**Implemented and deployed (PF 0.0.41.0; iPhone acceptance pending):** separately funded molten surface-shell enthalpy starts at 2200 K, bulk mantle at 2400 K, with 8% conserved broad regional redistribution. Canonical 100% initial melt drives a liquid optical material, replacing earlier dry-bedrock tint. C#/Playwright physics/browser tests and Pages smoke are green. **NEXT:** evaluate user's new T+0/T+1h iPhone screenshots for whole-globe molten credibility, camera/LOD and UI correctness; fix any issues, then continue mantle-surface coupling and permanent **primary crust** in small physical steps. Avoid fake black crust masks, artist-created heat or hardcoded evolutionary times.

The exact Earth Hadean thermal state is uncertain, so label this an explicit scenario/hypothesis with parameters, not the true history. Earth-scale magma circulation, deep convection, crust differentiation, steam EOS, outgassing, precipitation and first life remain future physical milestones.

## Next ten high-level gates

See [roadmap issue #196](https://github.com/pjotrcasteel/PlanetForge/issues/196): (1) molten regional visual integrity, (2) spatial heat transport, (3) primary crust, (4) geology/tectonic regimes, (5) radiative hot atmosphere, (6) volatile delivery/outgassing, (7) physical clouds/rain, (8) oceans/lakes/rivers, (9) climate/ice/erosion, (10) prebiotic conditions. A named milestone may require multiple PRs. The initial **magma-ocean scenario** is needed before claiming stage 1 visually accepted.

## Execution and communication

- Developer has delegated implementation sequencing, CI/CD, merging/release and testing. Keep working until a meaningful **new visual inspection** is ready or a genuinely essential user decision is needed; don't request a repeated “continue.”
- Scope: .NET 10, MSTest, Blazor WASM, WebGL2, Playwright, GitHub Actions/Pages. Preserve existing regional terrain/Hero geology and planet LOD 0–3.
- Always build/test, run browser checks, merge green PR, verify Pages SHA/visible version/production smoke. If any check fails, fix in code and re-run.
- Never assert future autonomous/background work unless a scheduling mechanism is explicitly used. Work synchronously when a session is active.
- Each milestone's PR updates **this file**. Create a separate snapshot when handing off chats.
