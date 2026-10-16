# PlanetForge — local ZIP snapshot inventory (2026-10-16)

> **Inventory only, not a source merge or release.** No production source has been overwritten. This entry records an uploaded workspace snapshot and the divergence against GitHub `main` to support deliberate reconciliation. Do not mistake this document for the underlying ZIP having been uploaded to GitHub.

## Provenance and scope

- Input: user-provided `PlanetForge.zip` (snapshot assessed on 2026-10-16).
- Raw ZIP SHA-256: `a0b15c7b7daee67c2d21f6c8a2bb0422e900fc068428c198ac25f64bbd72c118`.
- Raw archive: 1,527 entries (including directories), ~412 MB uncompressed, 148 MiB on disk.
- 454 source/document/test/asset files, 16,629,361 bytes uncompressed, after excluding caches, vendor dependencies, generated test artifacts, the local recording and .DS_Store.
- Inventory breakdown: `src/` 261, `tests/` 145, `docs/` 36, `tools/` 8, four root files.
- Relevant exclusions from any normal commit: `tools/earth-bake/.cache/` (~276 MiB), Playwright `node_modules/` (~12 MiB), browser test artifacts, an ~87 MiB recording. The snapshot's `.gitignore` does not list the scientific .cache or node_modules directories.
- There is **no `.github/workflows/` in the supplied ZIP**; preserve GitHub's workflows.
- GitHub `main` was `4e7ee78408dfc422f7c1c88e8b2301ea11e459cd` when assessed. This archive is **not** a clean descendant of that branch and must not replace `main`.

## Indexed architecture

| Area | What exists in the ZIP | Evidence / maturity |
| --- | --- | --- |
| Scientific .NET base | `PlanetForge.Domain` (formation, origins, atmosphere, climate, hydrology and planetary surface); `Application` (planet experience, rendering, run/mission orchestration); `Infrastructure` (geology/terrain); Blazor WASM presentation | Existing scientific infrastructure; do not assume it is identical to live main |
| Molten → crust evolution | `wwwroot/js/evolution/thermalHistory.js`, `crustRafts.js`, `planetEvolution.js` | Distinct WebGL2 model with explicit hypotheses and visualization liberties; 1024×512 lithosphere field |
| Earth reconstructions | `wwwroot/js/evolution/earthHistory.js` and `wwwroot/data/earth/` binary products | Cao/Merdith plate-history and PALEOMAP/ETOPO ingestion is described in roadmap. Provenance, licence and numerical validation require independent verification |
| Main globe & UI | `wwwroot/js/planetRenderer.js`, `Pages/Home.razor[.cs]`, `Home.razor.css` | Default planet renderer invokes GPU evolution; orbital/local interaction and older canonical components coexist in source |
| Offline baking | `tools/earth-bake/`, including `bake.py` and related scripts | Reconstructed pre-baked scientific data; downloaded source datasets retained only in excluded local cache |
| Automated validation | 3 .NET test projects; Playwright browser suite including `formation-simulation.spec.mjs` | Tests are included, but the archive is not CI-validated as an integrated candidate |

The GPU evolution layer introduces a **separate modelling/clock boundary** relative to the conservation-oriented .NET formation state. Its visual output must not be labelled an authoritative conservation-validated continuation of the .NET world without an explicit integration contract and corresponding tests. The current Earth-history documents disclose numerous visualization liberties and conditioned/reconstructed inputs.

## Documented progress in the local snapshot

- Molten magma ocean, quench-crust rafts, stagnant lid and tectonic regimes on a continuous rendered globe.
- GPU surface fields for crust and plate age/thickness, plate interactions, volcanism/impacts, material appearance.
- Steam rain-out, basins and ocean rendering; analytic cloud, sea-ice, climate, snow and vegetation effects.
- Historical Earth reconstruction data (1.8 Ga to present), present-day Earth/ETOPO anchor, PaleoDEM elevation influence and preliminary pre-Nuna craton conditioning.
- `docs/milestones/earth-history-roadmap.md` marks E0–E4 as first-pass/partial, with unresolved scientific/visual criteria. E5 remains a prescribed climate curve instead of a coupled forcing/feedback model. E6 routed river/lake/ice-sheet geometry and E7 key-moment timeline UI remain open.
- `docs/handover/CURRENT.md` contains *local* Edge and .NET test evidence (including a 4/4 evolution browser pass) but explicitly says this track is not released. These historical results are not independent CI, deployment or real Safari verification.

## Verified during this inventory (not a full build)

- Unpacked and indexed **454 retained files without editing their bytes**.
- `node --check` passed for the 21 shipped `wwwroot/js/` JavaScript modules.
- `dotnet` SDK was not available in the inspection environment: no fresh .NET restore/build/test run.
- No runnable WebGL browser/session or live deployment was validated in this inventory.
- The uploaded source snapshot does not include the current `main` content, and no automatic overwrite, cherry-pick, merge, release or Pages deployment has been performed.

## GitHub divergence as of this inventory

- Live `main` documents the PF **0.0.48** opt-in `?demo=1` playable prototype with its scientific default; the ZIP introduces a different, continuously GPU-driven main evolution experience. These should be treated as **competing integration candidates**, not silently mixed.
- In particular, `wwwroot/js/evolution/` has **four modules in the ZIP and does not exist on the inspected main tree**.
- Open PRs at inspection: **#218**, **#228**, **#229**, **#230**, **#231**, **#233**. They include scientific conservation/transport and PF 0.0.49 visual work. Inspect them before planning any source integration.
- The repository has newer scientific files and docs not present in the archive. Do not delete them or replace updated `CURRENT.md` / workflows with the ZIP copies.

## Recommended next decisions (no implementation in this inventory)

1. **Protect both tracks:** reconstruct the full ZIP change set as a separate source branch based on a documented common ancestor or as additive changes with manual conflict review; never bulk overwrite `main`. Keep cached datasets/recording/vendor packages outside Git.
2. **Choose the authority boundary:** either ship GPU Earth evolution as an explicitly experimental demo alongside .NET canonical physics, or first integrate model inputs/outputs, conserved quantities, deterministic clock and LOD 0–3 with .NET. Do not claim a single canonical physical model otherwise.
3. **Scientific verification:** confirm reconstruction asset licences/citations, age-to-frame transition at 3.0 and 1.8 Ga, conservation, erosion and palaeoelevation misfit, sea-level accounting, and climate-forcing assumptions.
4. **Visual and performance acceptance:** run fresh .NET + Node + browser suites, compare same seed and camera on main globe, profile PC/iPhone Safari, validate real orbit/local transitions, verify version/CI/Pages at an exact SHA only after reconciliation.
5. **Future vertical slices:** the highest-priority unresolved physical/visual work is E3's erosion/misfit evidence, E5 coupled climate feedback, E6 watershed/rivers/lakes/ice geometry, and E7 usable geological milestones. Preserve scientific transparency and a single authoritative world state as acceptance gates.

**Source-push status:** this branch indexes the supplied snapshot; its source ZIP and new GPU modules have **not** been transplanted into GitHub. Source upload must be a distinct, explicitly reviewed integration step so existing main and open PR work is not lost.
