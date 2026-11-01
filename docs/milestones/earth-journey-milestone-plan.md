# PlanetForge — the journey milestone plan

**Updated:** 2026-11-01  
**Owner:** PlanetForge Earth-history and Life workstreams  
**North star:** Start in orbit, travel along an enticing horizontal timeline of Earth's actual major transitions, and automatically dive into a **physically supported** local simulation when a key moment occurs. Eventually play the uncertain chemistry and evolution of life from molecule scale back to Earth scale.

This is the **delivery checklist and order of work**, not a replacement for the scientific designs in [Earth history](earth-history-roadmap.md), [special-event policy](special-events-realtime-timeline.md), and [origin-of-life zoom](origin-of-life-zoom-roadmap.md). IDs **E0–E7** and **M0–M11** retain their original meanings.

## What exists today

- [x] An evolving GPU Earth with early conditions and a long-run Earth-history reconstruction (E0–E4 first passes; later fidelity still open).
- [x] A state-derived, automatic **planet-wide** 1× close-up for rock-lid formation, the model's 50% ocean-inventory condensation, and simplified plate-regime onset (E7 first slice).
- [x] Ordinary illustrative meteor impacts remain **ambient**; no artificial meteor impact is a milestone or automatic zoom trigger.
- [x] A horizontal logarithmic time rail shows model-witnessed events. **Past states are not yet replayable.**
- [x] A refreshed, more cinematic timeline treatment with distinct live cursor, touch-sized small-event balls, deep-time ticks, eon label and most recent milestone (E7 visual slice, pending live device review).
- [x] E7-B first slice: an immutable three-event S0 catalogue with explicit model-observed authority, physics-state trigger, time-step interval, unresolved historical uncertainty, scope, priority and source. The latest-moment strip expands into an accessible recorded-event log, collapsed by default on mobile.
- [x] E7-A second mobile pass (0.0.55.0): compact top phase chip; four icon/preset controls with confirmation before restart; compact chapter rail with pre-placed planned milestones and a distinct final Present-day dot. Event-based zoom behavior is unchanged. **Real-device visual acceptance remains open.**
- [ ] Regional ocean, hydrothermal vents, molecular simulation, life and land-animal interactions **are not implemented**.

## Delivery sequence

| Priority | Milestone | Deliverable | Definition of done |
| --- | --- | --- | --- |
| **Now** | **E7-A — Cinematic timeline** | Layered horizontal deep-time track; separate cyan *now* marker and golden event balls; real logarithmic tick labels; responsive latest-moment card; no impact spam. | iPhone and desktop review show legible labels/no collisions, current and past dots distinguishable, auto-zoom unaltered, keyboard/screen-reader state intact. |
| **Next** | **E7-B — Event catalogue and provenance** | Typed event metadata: trigger, model/reconstruction/hypothesis authority, time interval/uncertainty, scene/scale, priority, source. Curate giant impact, lid/oceans, GOE, extinctions, life and land transitions as **candidates**, not fake simulation results. | Every active marker has traceable evidence; events are not invented by decorative effects; global transitions cannot claim a precise local site. |
| **Next** | **E7-C — Replay and era lens** | Physical state checkpoints and a tap-to-revisit event flow; optional chapter/era zoom of the time axis so late events do not crowd into its final 10%. Pause/resume and seek remain deterministic. | Replay restores the actual model state; scroll and focus work on 375px screens; no visual transition changes geological time invisibly. |
| **Foundation** | **E5/E6 + M0 — Habitats and handoff** | Historical ocean/atmosphere/geochemistry checks, regional sea-floor/crust context, terrain/hydrology where needed; immutable S0→S1 boundary snapshot and time/units contract. | Parent planet freezes, regional entry and return preserve state, sources and conservation budgets reported. |
| **Zoom 1** | **M1 — Orbit → ocean** | Continuous camera travel via cloud, water surface, darkening water column to a real S0-derived ocean-floor patch. | No hard-coded generic vent; regional bathymetry, thermal state and coordinates agree with S0; ≥30 fps target and return/replay test. |
| **Zoom 2** | **M2 — Hydrothermal vent field** | Simulate water/rock reactions, heat flow, chemical gradients and chimney growth for a defensible Hadean scenario. | S2 local temperatures, fluids and budgets check out; an honest vent marker appears **only** where physical preconditions hold. |
| **Zoom 3** | **M3 — Mineral pores** | Move into a chimney wall to watch concentration, pH/redox and thermal gradients. | Advection/diffusion/reaction are validated and material conserved; local seconds/hours clock clearly differs from planet time. |
| **Life experiment A** | **M4 — Polymer chains** | Stochastic ligation, hydrolysis, templating and chain growth/breakage with precursor supply. | Quantitative distributions/failure cases and scientific limitations displayed; chains are never guaranteed to persist. |
| **Life experiment B** | **M5 — First membranes** | Amphiphile self-assembly, micelles, vesicles, competitive growth/division within validated chemical ranges. | Membranes form or fail depending on chemistry; membrane and polymer paths can run **in either order or in parallel**. |
| **Life experiment C** | **M6 — Protocells** | Encapsulation, replicator error, osmotic growth, competition, inheritance and possible collapse. | First viable protocell is a **run-specific outcome**, never presented as the proven first life on Earth. |
| **Playable loop** | **M7/M8 — Biosphere + game** | Feed local biological success/failure back to the planet. Observe/Experiment/Challenge controls, scientific codex, retry/share seed. | A fair full loop orbit → vent → pore → membrane/polymer → protocell → planet; no predetermined success, calibrated feedback and usability. |
| **Deep-time life** | **M9–M11 — Evolution to land** | Microbial mats and oxygenic ecosystems; eukaryotes and multicellularity; marine animals, plants/arthropods on land, later tetrapods; events anchored to fossil/geochemical evidence. | Distinct first-life, ecosystem, oxygenation, animal, plant, arthropod and tetrapod zooms with relevant habitats and uncertainty; present-day Earth returns. |

## E7-A mobile layout revision (0.0.55.0)

The milestone rail is an **ordinal journey, not a mathematical time axis**. Ten chapters are spaced for touch readability: Formation, rocky lid, first oceans, model plate regime, first life, oxygenation, complex cells, animals, life on land, and Present day. Their on-screen spacing does **not** imply equal geological duration or a source-backed event age. The caption says “NOT TO SCALE.” Future chapters have muted hollow markers; tapping one discloses “PLANNED · NO SIMULATION YET” without changing the model clock or faking a zoom. Only actual `earth-milestone` crossings become golden recorded dots and can trigger an S0 automatic inspection. The final Present-day dot is distinct cyan and is not a false simulated milestone.

The phase HUD is one compact title/time chip with diagnostics behind a details icon. Four controls are Play/Pause, 1×/5×/10×/20×/50×, Clouds and Restart. The `×` is a multiplier of the existing `1 Myr/s` model-speed cap, **not** real-time evolution. The restart icon first opens a confirmation overlay; cancelling preserves the state and witnessed history.

This deliberately defers a quantitative interactive zoomable geological-time scale to E7-C, where eras and snapshots will give accurate context. It removes the earlier misleading logarithmic `1 kyr / 1 Myr / 1 Gyr` tick design.

## E7-B catalogue contract (implemented first slice)

An **active** marker is emitted only when a named S0 field transition is actually sampled during an integration step. Its JSON contract includes:

- `authority`: currently `model-observed`; `reconstruction` or `hypothesis` are reserved for future content with reviewed evidence and an appropriate experience.
- `trigger`, `source`, `limitations`: model field crossing and what the observation **cannot** establish historically.
- `interval.startSeconds` / `interval.endSeconds`: the preceding and following simulated model times. The rail dot sits at the end of this **detection interval**, not at a claimed exact geological age.
- `uncertainty`: interpretation limits distinct from the width of the integration step.
- `scope`, `scale`, `priority`: today always global S0 observations, with no fictional physical local site.
- `type: earth-milestone`: never `magma-impact`. Existing decorative impacts are separate diagnostics and cannot enter the event rail.

The timeline exposes the newest marker as one compact row; opening **LOG** reveals all recorded markers with provenance, detected model interval and an honest “replay unavailable” notice. The list shows at most 64 records. It does **not** fabricate the unwitnessed past if a browser opens midway through the geological model, nor does it support past time travel until E7-C.

### Later-event candidates — not markers yet

| Candidate transition | Evidence/engine gate before release |
| --- | --- |
| Moon-forming giant impact | Vetted uncertain collision chronology, forcing and modeled global response; not routine ambient rocks |
| Stable hydrosphere and regional ocean habitats | Water/atmosphere reconstruction plus S1 ocean heat/chemistry boundary fields |
| First plausible prebiotic membranes and polymers | S2–S4 chemical networks; hypotheses clearly distinguished from historical claims |
| Earliest microbial life and metabolic innovation | Geochemical/fossil evidence, confidence ranges and a playable non-unique origin pathway |
| Oxygenic photosynthesis and Great Oxidation Event | Atmospheric/redox conservation and temporal evidence ranges |
| Eukaryotic cells and multicellularity | Cell-state model, evolutionary branch and calibrated reconstruction uncertainty |
| First animals and Cambrian ecosystems | Fossil window and habitat/locomotion interaction support |
| First plants, arthropods and tetrapods on land | Differentiated shore/land habitats and evolutionary evidence gates |
| Mass extinction and recovery episodes | Selective extinction forcing, biodiversity/evolution engine and uncertain event window |
| Present day Earth and player impact | Supported reconstruction through late Earth history and explicit comparison to baseline |

**These are a scientific/gameplay backlog, not simulated or dated events.** A proposed period without a physically supported view gets no automatic zoom.

## Experience rules

1. **Only true turning points take the camera.** No zoom per routine meteor, eruption or sparkle. Exceptional Moon-forming/Chicxulub-like episodes require explicit scientific forcing and provenance before receiving a marker.
2. **A marker is a promise.** Gold means a documented key moment witnessed by the model or reconstructed from evidence; cyan means *current time*, not a second event. Future planned events never appear as if already simulated.
3. **The zoom must earn its scale.** Planet-wide events can zoom to the globe. Ocean, vent, molecule, cell, shoreline and animal shots require corresponding local solvers and boundary-field handoff.
4. **Keep the parent paused.** Planetary years, vent years, pore hours and microscopic rates are different clocks. The chosen scale, elapsed local time and provenance stay visible.
5. **History ≠ experiment.** Fossil dates are uncertain and broad. A model experiment that yields a membrane, polymer or protocell is a plausible branch, not proof of Earth's singular actual origin.
6. **Accessibility and devices are milestones too.** Check 375px phone, larger tablet/desktop, touch/keyboard, reduced-motion, framerate, contrast, screen readers and return to orbit.

## Immediate, actionable next three PRs

- [ ] **E7 device polish:** review the compact 0.0.55.0 chapter rail and icon-only controls on a real iPhone and desktop, including focus/confirmation overlays and 10 milestone dots at 375px. Check touch targets, text truncation, and no overlay collisions.
- [x] **E7 event architecture (first slice):** immutable three-event S0 catalogue, bounded witnessed history, model-step time intervals with explicit limitations, and opt-in compact event log; no ambient meteor entries.
- [ ] **E7 catalogue expansion:** historical sources and scientifically defensible uncertainty ranges for candidate reconstructions, once a corresponding physical simulation or reconstruction playback exists.
- [ ] **M0 handoff specification + vertical slice:** implement one immutable planetary ocean snapshot, validated field units and deterministic enter/exit/restart; demonstrate **orbital → regional ocean view** without fictional chemistry.

## Release gates (every milestone)

- Pure model tests for triggers, clocks, deterministic seed/restart, conservation and event source.
- Browser tests for automatic entry, focus pause, exit/restore and mobile touch handling.
- Visual QA: screen recording at early oceans, during auto-focus, later era; confirm no crowding or misleading milestones.
- Independent science review of dates, claims and "model / reconstruction / hypothesis" copy.
- Existing CI, WebAssembly build, GitHub Pages exact-commit deployment and live smoke remain green.

**Explicitly not done yet:** historical event rewind; S1–S4 scale simulation; first-life reconstruction; evolutionary organisms; gamified origin of life. Those are planned gates rather than silently pre-rendered cinematic scenes.
