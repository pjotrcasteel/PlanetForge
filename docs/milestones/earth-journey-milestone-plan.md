# PlanetForge — the journey milestone plan

**Updated:** 2026-11-01  
**Owner:** PlanetForge Earth-history and Life workstreams  
**North star:** Start in orbit, travel along an enticing horizontal timeline of Earth's actual major transitions, and automatically dive into a **physically supported** local simulation when a key moment occurs. Eventually play the uncertain chemistry and evolution of life from molecule scale back to Earth scale.

This is the **delivery checklist and order of work**, not a replacement for the scientific designs in [Earth history](earth-history-roadmap.md), [special-event policy](special-events-realtime-timeline.md), and [origin-of-life zoom](origin-of-life-zoom-roadmap.md). IDs **E0–E7** and **M0–M11** retain their original meanings.

## F0 — Birth of Earth and Moon (new opening; 2026-11-01)

The player begins with **accretion of proto-Earth**, not a fully formed plate-covered globe. A single **exceptional Moon-forming giant impact** is presented as a scientifically informed **reconstruction**, followed by orbital ejecta, Moon assembly and a hot post-impact Earth. This is fundamentally separate from the model's routine background impact effects.

| Gate | Status | Technical/scientific contract |
| --- | --- | --- |
| **F0.1 Accretion** | [x] First visual slice | Deterministic seed, growth phase, time proportional to 1×–50× control, mobile WebGL two-body framing. Visual growth is scenario-driven, not an N-body accretion solver. |
| **F0.2 Giant impact** | [x] First visual slice | Theia-sized collider, incandescent collision close-up, GPU ejecta, unique explicit reconstruction event at selected +60 Myr (uncertain, not a model observation). Planetary time pauses during the short cinematic. |
| **F0.3 Moon assembly** | [x] First visual slice | Ejecta disk and separate Moon visual, seeded deterministic 12-second display sequence. Body composition, angular momentum and trajectories are artistic reconstructions, not SPH/gravitation output. |
| **F0.4 Thermal handoff** | [x] First integration slice | Geological clock hands off just after +60 Myr (scenario-local Moon-assembly year) to the existing GPU magma ocean, preserving total Earth-history chronology. Thermal state is an initial hot scenario, **not** conserved output from simulated collision energetics. |
| **F0.5 Scientific + visual acceptance** | [ ] **OPEN** | On-device iPhone and desktop screenshots/recording; robust WebGL shader compile, framing, cinematic lighting and FPS; a vetted conservation-calibrated impact model or independently sourced reference reconstruction, and science review of uncertain Moon chronology. |

**F0.5a — proto-Earth surface quality gate (0.0.58.3, first coded pass; art acceptance OPEN):** Isolate the pre-impact Earth's surface treatment from Theia and the Moon. Use irregular short-lived quenched rafts, rough low-albedo silicate material, coherent thermal convection, narrow molten raft margins, and restrained glints. Remove the unsupported uniform pre-atmosphere rim glow. NASA describes an early Earth without a stable solid surface; these rafts are a transient illustrative texture, **not tectonic plates or measured crust coverage**. Khronos PBR metallic-roughness principles inform light/roughness contrast, but the current shader is not yet an energy-conserving BRDF or a spectrally calibrated melt model. The mobile screenshot gate now checks actual rock/melt contrast. Human visual evaluation on iPhone, device FPS, and source-calibrated material temperatures remain open.

**Timing is scenario-based.** A +60 Myr event within a broad `50–150 Myr` plausible range is **not** a measured exact date. The Moon may have emerged from a post-impact disk or by a much faster dynamical pathway; current implementation does not choose between those as a scientific conclusion. NASA Moon Formation and National Academies (2023) Q3.3 are the review references. A scenario-local year is shown during Moon assembly. The precise duration and physical route remain uncertain; the handoff just after +60 Myr is a **model-coupling choice**, not an established formation date.

The post-impact magma ocean reuses the existing thermal and raft model. An **8-second visual-only blend** now opens with continuous turbulent silicate melt before the inherited cooling-raft texture fades in, so coherent seams do not masquerade as mature tectonic plates in the first hot frames. This blend is **not** a modeled geological cooling interval; on-device material/shader fidelity is still an acceptance gate. All F0 work must preserve the four-control mobile dock, proportional timeline, and model/reconstruction provenance.

**F0 cinematic visual pass (0.0.58.2, pending real-device approval):** Replace flat protoplanet shading with multi-scale granular silicate/magma materials, a hotter post-collision Moon, a widening vapor-and-melt ejecta curtain and an optically richer orbital debris disk with thousands of GPU sprites. The portrait camera moves closer at peak collision. This aims for a plausible *visual reconstruction*, not a verified SPH fluid simulation or unique Moon-assembly route. NASA's 2022 high-resolution giant-impact simulations explicitly show alternative formation pathways; no single one is treated as definitive here. The CI screenshot gate captures approach, flash, ejecta, Moon and thermal handoff at 390px, but **human visual acceptance and device FPS remain open**.

## What exists today

- [x] F0 first playable reconstructed accretion → exceptional giant impact → debris/Moon → magma-ocean handoff; high-fidelity visual and collision physics acceptance still open.
- [x] An evolving GPU Earth with early conditions and a long-run Earth-history reconstruction (E0–E4 first passes; later fidelity still open).
- [x] A state-derived, automatic **planet-wide** 1× close-up for rock-lid formation, the model's 50% ocean-inventory condensation, and simplified plate-regime onset (E7 first slice).
- [x] Ordinary illustrative meteor impacts remain **ambient**; no artificial meteor impact is a milestone or automatic zoom trigger.
- [x] A linear, moving geological-time lens and a full-history overview show true proportional spacing. **Past model states are not yet replayable.**
- [x] A refreshed, more cinematic timeline treatment with distinct live cursor, touch-sized small-event balls, deep-time ticks, eon label and most recent milestone (E7 visual slice, pending live device review).
- [x] E7-B first slice: an immutable three-event S0 catalogue with explicit model-observed authority, physics-state trigger, time-step interval, unresolved historical uncertainty, scope, priority and source. The latest-moment strip expands into an accessible recorded-event log, collapsed by default on mobile.
- [x] E7-A second mobile pass (0.0.55.0): compact phase chip and confirmed restart with 4 preset controls.
- [x] E7-A spatial/time correction (0.0.56.0): four controls on a slim **right-hand vertical dock**, timeline moved to the bottom and rebuilt as a **moving, proportional linear window** over a **full 4.567-Gyr proportional overview**; genuine model events sit at observed model times. **Real-device visual acceptance remains open.**
- [ ] Regional ocean, hydrothermal vents, molecular simulation, life and land-animal interactions **are not implemented**.

## Delivery sequence

| Priority | Milestone | Deliverable | Definition of done |
| --- | --- | --- | --- |
| **Now** | **E7-A — Cinematic timeline** | Time-proportional full-history overview plus dynamic linear close-up; actual year endpoints; cyan *now* and golden observed-event dots; vertical control dock; no impact spam. | Mobile and desktop review show linear year spacing, flowing time lens, no collisions, and unchanged auto-zoom and accessibility. |
| **Next** | **E7-B — Event catalogue and provenance** | Typed event metadata: trigger, model/reconstruction/hypothesis authority, time interval/uncertainty, scene/scale, priority, source. Curate giant impact, lid/oceans, GOE, extinctions, life and land transitions as **candidates**, not fake simulation results. | Every active marker has traceable evidence; events are not invented by decorative effects; global transitions cannot claim a precise local site. |
| **Next** | **E7-C — Checkpoint replay and interactive time navigation** | Physical state checkpoints and a tap-to-revisit event flow; optional manual time-window scrubbing without fake simulated snapshots. Pause/resume and seek remain deterministic. | Replay restores the actual model state, 375px navigation works, and moving the viewport alone never advances geology. |
| **Foundation** | **E5/E6 + M0 — Habitats and handoff** | Historical ocean/atmosphere/geochemistry checks, regional sea-floor/crust context, terrain/hydrology where needed; immutable S0→S1 boundary snapshot and time/units contract. | Parent planet freezes, regional entry and return preserve state, sources and conservation budgets reported. |
| **Zoom 1** | **M1 — Orbit → ocean** | Continuous camera travel via cloud, water surface, darkening water column to a real S0-derived ocean-floor patch. | No hard-coded generic vent; regional bathymetry, thermal state and coordinates agree with S0; ≥30 fps target and return/replay test. |
| **Zoom 2** | **M2 — Hydrothermal vent field** | Simulate water/rock reactions, heat flow, chemical gradients and chimney growth for a defensible Hadean scenario. | S2 local temperatures, fluids and budgets check out; an honest vent marker appears **only** where physical preconditions hold. |
| **Zoom 3** | **M3 — Mineral pores** | Move into a chimney wall to watch concentration, pH/redox and thermal gradients. | Advection/diffusion/reaction are validated and material conserved; local seconds/hours clock clearly differs from planet time. |
| **Life experiment A** | **M4 — Polymer chains** | Stochastic ligation, hydrolysis, templating and chain growth/breakage with precursor supply. | Quantitative distributions/failure cases and scientific limitations displayed; chains are never guaranteed to persist. |
| **Life experiment B** | **M5 — First membranes** | Amphiphile self-assembly, micelles, vesicles, competitive growth/division within validated chemical ranges. | Membranes form or fail depending on chemistry; membrane and polymer paths can run **in either order or in parallel**. |
| **Life experiment C** | **M6 — Protocells** | Encapsulation, replicator error, osmotic growth, competition, inheritance and possible collapse. | First viable protocell is a **run-specific outcome**, never presented as the proven first life on Earth. |
| **Playable loop** | **M7/M8 — Biosphere + game** | Feed local biological success/failure back to the planet. Observe/Experiment/Challenge controls, scientific codex, retry/share seed. | A fair full loop orbit → vent → pore → membrane/polymer → protocell → planet; no predetermined success, calibrated feedback and usability. |
| **Deep-time life** | **M9–M11 — Evolution to land** | Microbial mats and oxygenic ecosystems; eukaryotes and multicellularity; marine animals, plants/arthropods on land, later tetrapods; events anchored to fossil/geochemical evidence. | Distinct first-life, ecosystem, oxygenation, animal, plant, arthropod and tetrapod zooms with relevant habitats and uncertainty; present-day Earth returns. |

## E7-A mobile layout and proportional-time revision (0.0.56.0)

The simulation uses a **moving linear geological-time lens**, not ordinal equally spaced balls and not a logarithmic axis. Within the visible window, twice the distance always means twice the elapsed model time; the left/right ruler labels show the exact model-year bounds and the window size. The window grows continuously from 25 kyr to a maximum of 1.5 Gyr as the simulated planet ages. Once the *now* cursor reaches roughly 32% of the window it stays there while nodes slide past; approaching present day it moves toward the final cyan dot. The thin upper overview is also linear across Earth's **4.567 billion years**, with the viewport window highlighted so no other chapters are hidden conceptually.

Ten historical reference anchors are placed approximately at: Formation 0; Moon-forming impact 60 Myr; ancient crust evidence 167 Myr; early oceans evidence 267 Myr; earliest life signals 867 Myr; Great Oxidation Event 2167 Myr; complex cells 2567 Myr; first animals 3767 Myr; early land animals 4137 Myr; and Present day 4567 Myr. These are **rough evidence-led anchors with significant uncertainty**, not exactly dated point phenomena. In particular, the timing of the origin of life is unknown, and early water existed by roughly 4.3–4.4 Ga. The axis is quantitatively proportional to the chosen reference dates; its static anchors do not assert simulated physical state.

The S0 model's independent **rocky lid / rainout / plate** events are instead drawn only when measured by that simulation, at the exact observed model timestamp. These witnessed events retain the gold ball, automatic zoom, scientific provenance and interval, and non-replayable-past policy. Routine impacts are ambient and never enter this rail. The planned origin-of-life game remains unimplemented.

The top phase HUD remains compact. The four 44px controls now form a translucent **right-edge vertical dock**, leaving the lower screen clear. They are Play/Pause, 1×/5×/10×/20×/50×, Clouds, and confirmed Restart. The `×` still multiplies the existing 1 Myr/s speed cap, not real time.

Chronology references: [NASA—4.4 Ga zircon and early oceans](https://science.nasa.gov/earth/earth-observatory/ancient-crystals-suggest-earlier-ocean/), [NASA—Moon formation](https://science.nasa.gov/moon/formation/), [Smithsonian—History of Life](https://naturalhistory.si.edu/education/teaching-resources/life-science/early-life-earth-animal-origins), [Smithsonian—Ocean through time](https://ocean.si.edu/through-time/ancient-seas/ocean-throughout-geologic-time-image-gallery), [Natural History Museum—early land animals](https://www.nhm.ac.uk/discover/origin-of-life-on-earth.html).

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

- [ ] **E7 device polish:** review the 0.0.56.0 right-hand control dock and true-time moving lens on a real iPhone and desktop, including focus/confirmation, the physical year ruler, 10 full-history markers and a correctly positioned *now* cursor at 375px. Check touch targets, text truncation, and no overlay collisions.
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
