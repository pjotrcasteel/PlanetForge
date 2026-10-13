# PlanetForge — copy/paste to resume in another ChatGPT chat

Use this entire prompt:

> Resume active PlanetForge development workstream **PF-EARTH-20261013-A**. GitHub repository: https://github.com/pjotrcasteel/PlanetForge. **First read** `docs/handover/README.md`, `docs/handover/CURRENT.md`, and the detailed `docs/handover/PF-EARTH-20261013-A.md`, plus roadmap issue #196. These are the canonical cross-chat handover, **not** a license to assume the documented head SHA is still current. Verify GitHub's live main branch, open PRs, GitHub Actions results and GitHub Pages deployment before changing anything.
>
> Continue autonomously with correctness-first incremental .NET 10 + WebGL2 implementation, MSTest/Playwright tests, PR review/merge and CI/CD. User has delegated sequencing, and wants no repeated requests to type "continue." Respect science and conservation: the planet must transition from physically funded molten rock to actual crust, atmosphere, water and possible first life using **one canonical world state** (never arbitrary timer-based colors or a separate fake planet). Main globe and LOD 0–3 are essential. Preserve Hero regions and separate Snowball scenario. No new diagnostic tabs.
>
> Most recent human feedback in this workstream: PF 0.0.40.2 mobile UI works (buttons, thermal HUD, accepted T+1/T+2 cooling); **the planet still looks like orange-tinted bedrock with a small bright spot**. An initial globally molten canonical scenario plus phase-faithful rendering is the next feature slice. Inspect `PlanetRegionalCoolingReference.cs` (1600 K + small 1.2% hotspot, solidus 1300/liquidus 1900) and `planetRenderer.js` (blends orange over barren rock) before coding. A branch `feature/magma-ocean-initial-reference-0.0.41` was created but might not contain code yet.
>
> Use feature PRs, full tests and verified Pages release with SHA/version before asking for a visual inspection. Keep `docs/handover/CURRENT.md` updated in each substantial PR. Do not claim CI passed, merged, deployed or user accepted until those independent events actually happen. Work during the active session as far as available, and stop only for a meaningful next visual check or essential question.

The historical snapshot `PF-EARTH-20261013-A.md` should not be overwritten; a future chat should create a new dated snapshot and keep the identifier in its predecessor link.
