# PlanetForge handover — canonical entry point

**Active workstream ID:** `PF-EARTH-20261013-A`

> This identifier is a stable **project-side conversation/workstream label**, not an internal ChatGPT conversation UUID. Chat IDs are not reliably available to the assistant. Use this label unchanged when moving to a new chat. GitHub is the source of truth for the work; chat messages are not.

## Start here (for every new chat or engineer)

1. **[CURRENT.md](CURRENT.md)** — brief actively maintained state, exact production release, recent user acceptance, current PR, blockers, and next action. Read this first.
2. **[Molten main-globe vertical slice](PF-EARTH-20261013-A-20261013-molten-main.md)** — PF 0.0.45.0 continuous primary simulation, exact release/validation evidence and open human/performance gates. Also read **[Latest solidification checkpoint](PF-EARTH-20261013-A-20261013-solidification.md)** — graded boundary/front solver, conservation recovery, matched-grid and PC evidence; verify CURRENT for deployment. Also read **[depth-column checkpoint](PF-EARTH-20261013-A-20261013-rock-columns.md)** — user visual rejection, #207–#209 formation foundation, numerical/architectural limits and recovery. Also read the [heat-coupling release snapshot](PF-EARTH-20261013-A-20261013-heat-coupling.md) and [original snapshot](PF-EARTH-20261013-A.md); preserve all history.
3. **[CONTINUE.md](CONTINUE.md)** — paste-ready prompt to resume autonomously.
4. [Depth-column scientific/architectural contract](../earth-origin-depth-resolved-rock-columns.md) — active formation foundation and declared limitations.
5. [Roadmap issue #196](https://github.com/pjotrcasteel/PlanetForge/issues/196), [scientific issue #143](https://github.com/pjotrcasteel/PlanetForge/issues/143), [scientific foundation](../earth-origin-scientific-foundation.md), [older long-form model handover](../model-handover.md).

## Update protocol (not optional for substantial milestones)

- **Before coding:** read CURRENT.md and query GitHub for the exact live `main` SHA, open PRs, CI runs and latest Pages deployment. Old snapshots may be stale.
- **With each milestone / substantial PR:** update CURRENT.md *in the same PR* with objective, what changed, tests, known gaps and what comes next. Keep it compact and avoid false completion claims. Mark a release **LIVE VERIFIED** only when GitHub Pages build, deploy and live smoke all passed for the exact commit.
- **At a handoff or full chat:** create a dated immutable snapshot, update CURRENT.md, keep all existing decisions, and reference the originating workstream ID. Never erase a useful history to make docs shorter.
- **At a new chat:** use CONTINUE.md; the successor should independently verify repository state and resume at the earliest uncompleted acceptance gate, not restart prior milestones.
- **Visual gates:** only request iPhone Safari checks when the exact new release is deployed and there's something genuinely different to inspect; preserve screenshots' observed results and the seed / clock / version. CI's Chromium emulation is not an iPhone Safari measurement.
- **Truthfulness:** PR open, CI success, merged, Pages deployed, production smoke passed, and human visual accepted are **distinct statuses**. No conflation.

## Active release identity

See [CURRENT.md](CURRENT.md) for the latest verified snapshot and check `main` before trusting it.

Repository: https://github.com/pjotrcasteel/PlanetForge

Published app: https://pjotrcasteel.github.io/PlanetForge/

## Release versioning and human visual feedback protocol (2026-10-13 agreement)

These are mandatory for **every feature release** in workstream `PF-EARTH-20261013-A`:

1. **Bump the PlanetForge version with the feature release**, consistently in application metadata, displayed UI, and any generated version identifiers. Verify that the iPhone-visible version and deployed production commit correspond to the exact release. A change is not release-complete if it ships under the previous version.
2. Verify **full CI** (build, automated tests and browser regressions), merge to `main`, successful GitHub Pages build/deploy, and **production smoke checks for the exact main SHA**. Never present draft/unmerged code as live. Avoid redundant notifications for a release already reported.
3. **Every meaningful visible release must have a specific human visual inspection request**. Tell the user exactly what changed, what to test, which URL/feature flag to open, which seed and simulation times to use, and what expected behavior and likely defects to look for. Prefer fixed seed `24061984`, same camera angle/zoom, before/after screenshots; preserve Hero and LOD 0–3.
4. **Use the user's screenshots and observations as engineering input** for the next milestone: compare expected vs observed, identify plausible root causes, record acceptance/rejection and actionable issues in CURRENT.md and a dated handover, then prioritize fixes. Do not interpret passing CI as proof of acceptable visual quality on iPhone Safari.
5. Only notify for **new, validated, meaningfully visible main-planet deployments**. Model-only PRs, documentation, previously tested oceans and nonvisual physics changes are not reasons to request another visual check. If nothing qualifies, do not notify.
6. Tailor checks to the feature: for changed Snowball ice rendering, ask for **Year 0 and melting Year 50** at the same seed/angle; for molten/rock columns, use declared T+0h/T+1h/T+4h as appropriate; for terrain and hydrology, inspect mountains, rivers, lakes and coastline continuity. Always invite observations on iPhone load time, blurry zoom/LOD, flicker, HUD overlap and off-globe rivers where relevant.
7. The visual inspection is a **feedback gate**, not an excuse to halt engineering between milestones. Continue implementation, diagnosis and CI/deployment autonomously until a genuine user decision or visual acceptance is required.

This protocol records the user's explicit request that every release include both a correct visible version and precise testing guidance, so the next development direction is driven by real device feedback.
