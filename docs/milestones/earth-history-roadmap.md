# Earth history roadmap — from magma ocean to today's Earth

Status: **E0–E4 implemented (first pass), E5+ planned**. Companion to `origin-of-life-zoom-roadmap.md` (life track, M-milestones). This
document is the **Earth track (E-milestones)**: the evolving planet must become *our* Earth — the real
supercontinent cycle (… Columbia/Nuna → Rodinia → Gondwana → Pangaea → today) ending on present-day
geography.

## Why physics alone cannot produce Earth
Mantle convection and plate motion are chaotic: a physically correct simulation produces *an* Earth-like
planet, never *the* Earth. Real geophysics solves this with **data assimilation** (e.g. mantle-flow models
driven by plate reconstructions, Bower et al. 2015). We do the same:

- **Kinematics (where plates move) come from published reconstructions** when they exist.
- **Physics produces everything else**: crust thickness and isostasy, orogens at convergent boundaries,
  ocean-floor age and depth, arcs and volcanism, rifts, climate, ice, sea level, erosion and rivers.
- Before reliable reconstructions, the sim is **emergent but conditioned** to grow Earth's real cratons
  at their real ages and deliver them to the first reconstructed configuration.
- The HUD always states the source: *"Reconstruction: Merdith et al. 2021"* vs *"Model (speculative)"*.

## Data sources (all open)

| Interval | Source | Licence | Use |
| --- | --- | --- | --- |
| 1.8–1.0 Ga | Cao et al. 2024, 1.8 Ga full-plate model (EarthByte `Cao_etal_2023`) | open (verify CC-BY) | Nuna/Columbia → Rodinia kinematics |
| 1.0 Ga–0 | Merdith et al. 2021, *ESR*, 1 Ga full-plate model (EarthByte) | open (verify CC-BY) | Rodinia → Gondwana → Pangaea → present |
| 540–0 Ma | Scotese & Wright 2018 PALEOMAP PaleoDEMs, 1°/0.1°, 5 Myr | CC-BY-4.0 | palaeo-elevation, coastlines, shallow seas |
| 0 Ma | NOAA ETOPO 2022 | public domain | exact present-day relief/bathymetry |
| 0 Ma | Seafloor age grids (Seton et al. 2020) | open | validation of ocean-age physics |
| Archean | craton inventory (Pilbara, Kaapvaal, Superior, Slave, Yilgarn, Wyoming, NAC, …) with formation ages | literature | seeding emergent era |
| events | Huronian (~2.4–2.2 Ga), GOE (~2.4 Ga), Sturtian/Marinoan snowballs (~717–635 Ma), LPIA, Quaternary ice, Chicxulub (66 Ma), LIPs (Siberian Traps 252 Ma, Deccan) | literature | climate/volcanic forcing |
| constants | faint young Sun (~70% L☉ at 4.5 Ga, Gough 1981), day length and Moon distance history, Moon-forming impact | literature | astronomy |

Tooling: **pyGPlates** (`pip install pygplates`) in an **offline bake pipeline**; the browser never runs
GPlates.

## Eras and how each is driven

| Era | Time | Driver | Observable |
| --- | --- | --- | --- |
| Moon-forming impact & magma ocean | 4.51–4.4 Ga | physics (existing) | glowing ocean, rafts, Moon close/large |
| Hadean | 4.4–4.0 Ga | physics (existing) | lid, steam rain-out, water world, protocrust |
| Archean | 4.0–2.5 Ga | emergent + craton conditioning | Vaalbara, Superia/Sclavia (speculative), greenstone belts, TTGs |
| Paleoproterozoic | 2.5–1.8 Ga | emergent → hand-off to Cao | GOE, Huronian glaciation, assembly of Nuna/Columbia |
| Meso–Neoproterozoic | 1.8–1.0 Ga | Cao 2024 | Nuna break-up, Rodinia assembly (Grenville) |
| Neoproterozoic–Paleozoic | 1.0 Ga–335 Ma | Merdith 2021 | Rodinia break-up, snowballs, Gondwana, Iapetus/Rheic, Caledonides |
| Pangaea | 335–175 Ma | Merdith 2021 + PaleoDEM | Appalachian/Variscan/Ural orogens, Tethys, Siberian Traps |
| Break-up to today | 175–0 Ma | Merdith 2021 + PaleoDEM + ETOPO | Atlantic opens, India→Himalaya, Andes, Alps, ice ages, today's map |

## Milestones

### E0 — Data pipeline (offline bake)
**Progress (2026-10-14):** E0 complete — `bake.py` produces the binary assets (rotation self-test 0.026°). Python 3.12 + pyGPlates 1.0 installed locally; `tools/earth-bake/preview.py` (data cached in git-ignored `tools/earth-bake/.cache/`) reconstructs Cao et al.
continents at 0–1800 Ma. Verified visually: present-day map, Pangaea at 250 Ma, Rodinia-era at 1000 Ma,
Nuna assembly at 1600 Ma. The Cao model already embeds the Merdith 1 Ga files, so **one continuous
1.8 Ga → 0 model** drives the reconstructed era. Remaining: plate-ID/rotation rasters, binary format,
PaleoDEM/ETOPO ingestion, manifest.

- `tools/earth-bake/` (Python): pyGPlates reconstructs Cao + Merdith at 1–5 Myr steps; rasterises plate
  IDs, plate Euler poles/angular velocities, continental/cratonic masks with present-day coordinates,
  PaleoDEMs and ETOPO into compact binary textures (equirectangular 512×256 or cube-map; u8/u16, gzip).
- Asset manifest with source, licence and citation per file; total download budget ≲ 30 MB, streamed by era.
- **Exit:** baked 200 Ma frame renders Pangaea correctly in a debug image; rotations match pyGPlates within
  0.1°; licences recorded.

### E1 — Present-day anchor
**Done (first pass):** the last 60 Myr are assimilated toward ETOPO 2022; the run ends on today's Earth
(land 29.1%, correct orientation, 45 plates).

- Load ETOPO-derived elevation, ocean age and real plate IDs into the GPU state at 0 Ma; render today's
  Earth with the existing ocean/cloud/climate shaders.
- **Exit:** recognisable present-day Earth; land fraction ≈ 29%; hypsometry matches ETOPO (bimodal peaks).

### E2 — Reconstruction-driven kinematics (1.0 Ga → 0)
**Done (first pass, 1.8 Ga → 0):** `tools/earth-bake/bake.py` → `wwwroot/data/earth/` (361 frames,
579 plates, ~9.5 MB); `wwwroot/js/evolution/earthHistory.js` takes over from the emergent planet at
1.8 Ga (40 Myr cross-fade). GPU passes: tectonics (semi-Lagrangian ocean formation age, trenches, arcs,
collision/rift uplift from Euler-pole closing rates), relief (present-referenced continental relief
accumulating uplift, relaxing τ = 150 Myr) and derived (ocean depth 2.6 + 0.35√age km, continents
carried rigidly). Sea level = constant freeboard + coarse eustatic curve (HUD liberty). Open: continental
area before ~1 Ga is low (COB polygons cover 14% at 1.8 Ga); oceans at 1.8 Ga show polygon-facet
artefacts; orogen timing not yet validated against geology.

- Replace free plate motion with prescribed per-plate Euler rotations; plate partition from baked topologies;
  continental crust is carried as rigid present-day-referenced material (no smearing).
- Existing physics generates: oceanic crust aging/subsidence (depth ∝ √age), arcs, collisional orogens
  (crustal thickening → Airy isostasy), rifts and passive margins.
- **Exit:** Pangaea at 250 Ma, Gondwana at 500 Ma, Rodinia at 900 Ma recognisable against references;
  modelled ocean age at 0 Ma correlates with Seton 2020; orogens appear where and when the geology says
  (Himalaya after ~50 Ma, Appalachians ~460–260 Ma).

### E3 — Elevation assimilation (540 → 0 Ma)
- Physics-computed elevation nudged toward PaleoDEMs with a relaxation time (~10–20 Myr) so shallow seas,
  coastlines and mountain ranges match; ends exactly on ETOPO at 0 Ma.
- Erosion (stream-power law) and sediment balance so old orogens decay (Appalachians) — needed anyway for rivers.
- **Exit:** RMS elevation misfit vs PaleoDEM below a set bound; visual timeline matches PALEOMAP atlas.
- *Progress (first pass):* `tools/earth-bake/paleodem.py` present-references the 109 PaleoDEMs (0–540 Ma,
  1°) — each present-day texel is rotated with the PALEOMAP model to its palaeo-position and samples the DEM,
  so relief rides on the crust and Cao/Merdith kinematics carry it (no frame mismatch between models).
  Runtime nudges relief toward the bracketing DEMs (τ 5 Myr, fades in 560→540 Ma, model uplift scaled
  down accordingly); before 540 Ma the baseline is the compressed 540 Ma DEM so no Himalaya/Andes exist in
  the Proterozoic. ETOPO nudging over the final 30 Myr. Sea level is pinned to the DEM datum (0) because
  the reconstructed basins are idealised; land fraction comes out at ~14–24 % through the Phanerozoic and
  29 % today. Orogen uplift saturates at ~4.5 km excess, relaxes over ~150 Myr; convergence capped at
  150 km/Myr. Before 540 Ma continental blocks are floored at +0.12 km and narrow inter-block polygon gaps
  are bridged (no deep 'channels' through Rodinia; flagged in `EARTH_LIBERTIES`). Stream-power erosion and
  RMS-misfit exit check still open.

### E4 — Precambrian conditioning (4.0 → 1.8 Ga)
- Seed Earth's cratons at their real formation ages from the inventory; current emergent tectonics moves
  them, with a soft attractor that delivers them to Cao's 1.8 Ga configuration (hand-off without jump).
- Labelled speculative; Vaalbara/Superia as optional hypotheses.
- **Exit:** seamless 1.8 Ga hand-off (no visible teleport); craton ages and positions consistent.
- **Implemented (first pass):** the Earth regime starts at plate onset (at the latest 3.0 Ga). The 24 real
  continental blocks of the 1.8 Ga frame are rigid cratons on synthetic rotations (35–80° swings about hashed
  poles per 800 Myr cycle) composed with their 1.8 Ga rotation, so they converge exactly on Nuna (no teleport).
  A GPU raster pass places the blocks each frame; each craton is a low-lying submerged plateau until its
  approximate emergence age (oldest nuclei ~3.25 Ga), then surfaces as islands that coalesce. Pre-Nuna relief
  is compressed (hot, thin lithosphere). Land colour follows the Great Oxidation (~2.45–2.2 Ga): anoxic
  greenstone/TTG grey-green before, ferric red beds after. Liberties are flagged in the HUD.
  Open: real craton outlines/ages from an inventory instead of present-day polygons; Vaalbara/Superia.

### E5 — Earth system history
- Faint young Sun, CO2/greenhouse history (GEOCARB/COPSE-like box model), GOE (sky/ocean colour, red beds),
  snowball Earth episodes (global ice from ice-albedo feedback under forcing), Phanerozoic ice ages, LIPs,
  sea-level curve, vegetation from 470 Ma, Chicxulub event.
- **Exit:** each event emerges from forcing + feedback in the box model, timed within literature ranges.
- Interim (done): prescribed global temperature anomaly curve (`CLIMATE_CURVE`, after Scotese et al. 2021) drives
  sea ice, snow and the snowball episodes; to be replaced by the box model.

### E6 — Rivers, lakes, ice sheets (geometry)
- Drainage network from the elevation field (flow routing, lakes in depressions), river geometry on the
  globe; ice sheets from the climate balance; ties to the existing world-engine terrain LODs.
- **Exit:** at 0 Ma, major basins (Amazon, Nile, Mississippi, Congo…) emerge from the elevation data.

### E7 — Earth-history time UI and curated key moments

**Implementation sequence and visual/device gates:** [Journey milestone plan](earth-journey-milestone-plan.md). The E7 first release is state-driven; the cinematic timeline is a visual enhancement, not new invented geological history.

**First slice in place (0.0.50.x):** a horizontal logarithmic 4.567-Gyr rail,
live-epoch cursor and small dot markers. The initial release zoomed on every
seeded Hadean impact; the next slice corrects this: **ordinary impacts remain
ambient, not milestones**. State-derived planetary turning points now drive
the orbital focus, without invented location-specific geometry.

**Event authority (required for every marker and future S1+ zoom):**

- **Model observed:** an actual transition in S0/S1+ state, e.g. lid onset,
  half-inventory ocean rain-out, simplified plate-regime onset. The event's
  timestamp is that of the model state and the HUD says "Model-state threshold".
- **Historical reconstruction:** a scientifically sourced interval or
  location (never described as emergent from the model). The visual scene
  must carry the corresponding forcing/geometry, date uncertainty and source.
- **Experimental/hypothesis:** plausible local abiogenesis conditions and
  modelled reactions; this is an experiment, not a claim of reconstructing
  the single actual origin of terrestrial life.
- **Ambient:** ordinary impacts, meteor showers, volcanism, cloud changes
  and small fluctuations. Render normally but **do not pause or auto-zoom**.

**Zoom discipline:** global changes may produce an honest closer orbital
view of the S0 globe; a site-specific drop into an ocean, vent, cell or animal
requires a valid S0→S1→… geographic/material/energy handoff and the
relevant scale-specific solver. A historical marker is not automatically
a playable simulation. Old events need real snapshots before replay.

**Curated Earth-history event catalogue (future E7/M track, not yet simulated as
individual events):**

| Approximate age | Key moment | Authority / prerequisite | Target perspective |
| --- | --- | --- | --- |
| ~4.5 Ga | Moon-forming giant impact | Giant-impact hypothesis; Earth/Moon dynamics and sourced uncertainty | Planetary cinematic only when forcing exists |
| Hadean | Persistent lid, oceans and early heat/hydrothermal circulation | S0 rocky lid and ocean condensation first; S1–S2 for vents | Orbit → ocean floor |
| ≥3.5 Ga (older claims debated) | Evidence for early microbial life | Fossil/geochemical interpretation, distinct from **simulated** abiogenesis | Microbial habitat, only with biosphere model |
| ~2.4 Ga | Great Oxidation Event | Geochemical record + explicit oxygen budget; S0 E5 system model | Microbes ↔ atmosphere/ocean |
| Proterozoic | Eukaryotic complexity and multicellularity | Evolutionary/fossil evidence and biological population model | Cell populations/tissues |
| ~540 Ma | Cambrian animal diversification | Fossil record + ecology model | Shallow marine habitat |
| ~470–450 Ma | Plants and invertebrates colonize land | Plant record / early terrestrial arthropod traces | Coastline and terrestrial ecosystem |
| ~375 Ma | Tetrapods move onto land | Fossil reconstruction (not the first terrestrial animals) | Water-edge habitat |
| 252 Ma and 66 Ma | Mass extinctions; Chicxulub for 66 Ma | Geological forcing + biological survival/extinction model | Impact/climate/ecosystem |
| Present | Today's continents, ecosystems and geography | ETOPO and palaeo-reconstruction convergence | Return to planet |

These approximate dates are **research anchors**, not pretended one-frame
birthdates. Significant transitions span intervals, can be contested, and are
often not directly observable from the fossil record. The next E7 slices add
reconstruction provenance, uncertainty intervals, event priority, and scrub/
jump only with valid physical-state checkpoints.

**Exit:** a full 4.567-Gyr run is watchable in ~5 min with substantive events
visually legible, no ambient-event camera spam, and each zoom labelled by
model/reconstruction/hypothesis and available physical scale.

## Status
Planet generation (magma ocean → present) is frozen after the final refinement round; development moves to
the origin-of-life zoom (life track). E5 box model and E6 remain open; E7 has a first horizontal rail/curation implementation but historical and biological event integration remains open.

## Order
E0 → E1 → E2 → E3 is the critical path (gets real Earth on screen fastest, working back in time).
E4 and E5 follow; E6 runs alongside E3. Life track M0 (research) runs in parallel; M1 needs E2/E4 ridge
and ocean state.

## Non-goals
No hand-painted continents or keyframed textures standing in for physics; reconstructions only constrain
plate kinematics and (Phanerozoic) elevation targets — every constraint is visible in the HUD.
