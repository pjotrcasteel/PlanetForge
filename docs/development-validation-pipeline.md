# PF-DEVEX-01 — fast feedback, parallel regression and intentional releases

**Workstream:** `PF-EARTH-20261013-A`  
**Scope:** developer productivity / GitHub Actions only, no physics, renderer, saved-world or version changes.

## Measured baseline (2026-10-13)

[Full PR workflow 38418506407](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38418506407) passed **298 domain + 157 application + 260 infrastructure = 715 .NET tests** and **43 Playwright regressions**, with five deployment-only tests skipped. Based on GitHub job step timestamps (UTC):

| Stage | Start | End | Elapsed |
| --- | --- | --- | --- |
| Restore | 15:01:57 | 15:02:02 | 5 s |
| Build | 15:02:02 | 15:02:22 | 20 s |
| .NET test | 15:02:23 | 15:02:49 | 26 s |
| WASM publish | 15:02:49 | 15:03:18 | 29 s |
| Browser dependencies | 15:03:20 | 15:03:41 | 21 s |
| **Serial Playwright suite** | **15:03:41** | **15:22:57** | **19 min 16 s** |

[Pages run 38421665240](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38421665240) separately rebuilt, retested, published, deployed and smoke-checked source `6c48d2c`. Its build/deploy/live-verification jobs all succeeded. The previously configured Playwright runner used `fullyParallel: false` and `workers: 1`.

## New continuous integration topology

1. One **fast-checks** job builds the complete .NET 10 solution, validates JavaScript, runs **all** .NET test projects and publishes the Blazor WASM application. This is an early, individually visible check; a passing build is not a passing PR while browser shards are running.
2. Three independent **browser-regression** jobs download the *same compiled application artifact* and run `npx playwright test --shard=N/3` on distinct GitHub runners. Each keeps one Playwright worker, preserving bounded WebGL/WASM resource usage. `fail-fast: false` lets every shard report its failures; each uploads uniquely named diagnostic artifacts.
3. The **entire existing browser suite** remains required before merging. The five production-only smoke cases remain in the Pages deployment job. No individual test has been deleted, excluded, shortened or reduced in coverage.
4. A push to `main` launches Pages only for `src/**`, `PlanetForge.slnx`, `Directory.Build.props` or `.github/workflows/pages.yml`. Documentation, test-only and `csharp.yml`-only changes do not needlessly republish identical application bytes. `workflow_dispatch` retains manual deployment capability. Pages still verifies the exact deployed commit and aborts on failed production smoke.
5. **Milestone development:** use one feature branch with small focused commits, run targeted tests while iterating, let full PR CI finish before merge, and merge one coherent release rather than many test-only increments. Do not start the next user visual inspection until a genuinely new, exact-SHA deployment passes smoke.

## Measuring the improvement

Record step/job durations from GitHub Actions for this PR and subsequent real milestones. Compare **critical-path elapsed time** against the 19 min 16 s serial browser baseline and ~21 min 09 s full PR baseline; shard runtime is the **slowest** shard, not the sum of runner-minutes. Three runners may increase total billed compute time even if human waiting time falls. Target a 3–8 minute browser critical path initially, then investigate slower scenarios and flaky tests from actual traces without silently weakening gates.

**Acceptance:** all three browser shards green with 43 passed and five production-only cases skipped in total, .NET build/tests green, Pages release check green when a release-triggering change is merged, and no Pages runs for a docs-only/test-only merge. If sharding exposes shared-state ordering or resource issues, fix test isolation or revert to a smaller parallel factor rather than ignoring failures.

## Isolated test-case scheduling and asynchronous transition waits

The original three-file-group shard run revealed a concentration of slow globe cases in one runner. Playwright now uses `fullyParallel: true` while keeping **one worker per runner**: sharding assigns independent *individual tests* instead of entire spec files, without causing overlapping test execution inside a runner. Before enabling this, we inspected the browser spec files for `describe.serial`, `beforeAll`, mutable shared top-level state and file ordering dependencies; none were found. The regular full suite must still pass from this scheduling order.

The initial shard run also reproduced a race after `SwitchRockReferenceAsync`: the final browser screenshot already showed the correct 1600 K uniform-rock screen but the selector's default five-second assertion window expired. The unchanged state assertion now has a 60-second bound (and likewise for the switch-back state); no retries, sleeps, relaxed acceptance conditions or skipped science checks are introduced. If a rerun still fails, collect the browser failure artifact and investigate DOM/state publication rather than silently disabling the assertion.
