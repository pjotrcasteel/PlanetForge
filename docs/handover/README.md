# PlanetForge handover — canonical entry point

**Active workstream ID:** `PF-EARTH-20261013-A`

> This identifier is a stable **project-side conversation/workstream label**, not an internal ChatGPT conversation UUID. Chat IDs are not reliably available to the assistant. Use this label unchanged when moving to a new chat. GitHub is the source of truth for the work; chat messages are not.

## Start here (for every new chat or engineer)

1. **[CURRENT.md](CURRENT.md)** — brief actively maintained state, exact production release, recent user acceptance, current PR, blockers, and next action. Read this first.
2. **[Latest heat-coupling release snapshot](PF-EARTH-20261013-A-20261013-heat-coupling.md)** — 2026-10-13 verified #204/#205 release, scientific boundaries and exact continuation order. Preserve and also read the [original snapshot](PF-EARTH-20261013-A.md) for earlier decisions and visual feedback.
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
