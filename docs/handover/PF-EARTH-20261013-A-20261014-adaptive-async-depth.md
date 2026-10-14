# Adaptive scheduling, nested depth and suspension checkpoint

Workstream **PF-EARTH-20261013-A**, 2026-10-14. This extends draft PR #231; it is not a new live demo.

## Verified preceding candidate

Commit `c178a3ffa52a31e39847da89d84490f6ce658140` passed full CI **38529393417**: build, .NET tests, WASM publication and all three browser shards. It remains unmerged. The released main remains `cc4c19c` / PF 0.0.47.0 and its human visual gate remains failed.

## New implementation

- Synchronous and asynchronous column integration now consume the same controller iterator. Adaptive caps and trial history survive scheduling yields. Accepted and rejected work both provide cooperative scheduling boundaries; only the final candidate is published. The primary formation application's asynchronous path uses that same domain API. Tests compare exact layers, clock, revision and outgoing-energy counters and verify cancellation leaves the original state unchanged.
- Optional fixed parent-mesh grading conservatively subdivides parent layers. The coupled upper-magma factory uses a 64-layer quartic parent for its 64/128-layer references. Doubling resolution now halves the first cell instead of making it sixteen times thinner. Other layer counts retain direct grading. Tests verify every parent's mass/enthalpy and total physical extent.
- Integer timestep reduction previously changed a failed three-microsecond estimate straight to one, skipping the supported two-microsecond estimate. That is corrected. The seeded nested 128-layer one-second transient now completes at 0.1-K local error tolerance and conserves mass/energy. This does not establish long-time front convergence.
- An explicitly optional crystal-suspension thermal hypothesis extends transport into unjammed crystal-bearing fluid. The original fully-liquid-only hypothesis remains selectable. This follows the suspension viscosity branch in Monteux et al. (2016), with material-derived mixture density, latent effective capacity and phase expansion for the thermal closure. It is not an elastic crust, resolved velocity field, or a complete reproduction of that paper's solid creep/pressure/composition model.
- Strengthened the phase-barrier tests: the old 0.1-second fixture had an owned top layer at about 2058 K, still fully molten. A lower emitting-face temperature did not prove mixed owned material. The replacement three-second conductive fixture has about 1807 K / 0.845 owned melt and actually tests the coverage distinction. It is a fixture state, not an accepted cooling timeline.

## Evidence and remaining rejection cases

Local builds have zero warnings/errors. Nineteen targeted application cases and 72 broader domain/infrastructure cases pass (91 total), through the in-process runner because normal local VSTest sockets are unavailable. All scientific behavior remains subject to full new-commit CI.

The level-one seed-24061984 exploratory suspension run, with 64 layers and 0.1-K local error tolerance, completed four physical hours within a 10,000-trial request budget. At four hours its emitting temperature range is about 1784–2533 K; no emitting cell is fully solid and six are fully liquid. That differs materially from the earlier fully-liquid-only scope's apparent solid-skin onset. It must not be advertised as coherent crust formation.

At 128 nested layers, the suspension run reached one hour (about 1844–2770 K), but its subsequent request to four hours exhausted the 10,000-trial budget: 5072 accepted controller steps, about 4842 requested seconds still remaining. The requested candidate was rejected, not partially published. The fully-liquid-only fine reference also exhausts that research budget before completing four hours. Onset/front convergence therefore remains unresolved. Raising a budget alone is not scientific acceptance.

Reproduce those exploratory settings with `PlanetFormationInitialHeat.CreateUpperMagma(6371000, 24061984, gridLevel: 1)`, `CreateCoupledUpperMagmaInitial(..., layerCount: 64 or 128, useCrystalSuspensionRheology: true)`, and integration options `(30.0, 10000)` with `MaximumThermalErrorKelvin = 0.1`. Advance successive targets 1/10/30/60/180/3600/14400 physical seconds, passing the returned candidate into the next request. Do not infer command-partition-independent exactness from sync/async parity for an identical request.

## Next work

Establish supported long-time phase-front/onset convergence, including the numerical cost and crystallization rheology validity. Integrate material motion and crust interaction into the same canonical world; then inspect the actual main renderer at orbit/local LOD. No lava texture, invented geography, scripted stage, alternate preview planet, main activation, merge, deployment, or human test request is added by this checkpoint.
