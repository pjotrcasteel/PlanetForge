# Origin of Life — multi-scale zoom roadmap

Status: **plan only**, nothing below is implemented. Follows stage 8 ("Habitability and life") of
`planetary-evolution-visible-integration.md`, and keeps its rule: **no deterministic claim about how life
began**. The open hypotheses stay explicit, and every simplification goes into a per-scale liberties list,
the way `DEMO_LIBERTIES` works today.

## Goal
From the evolving planet, pause at a **key moment** and zoom without a cut from orbit to a Hadean or
Eoarchean (~4.4–3.8 Ga) deep-ocean hydrothermal system. Keep zooming to mineral pores, then to
self-assembling membranes and growing polymer chains ("polychains"). Every scale is a simulation seeded
by the scale above it, not a canned animation. Gamify it later, on top of the same models.

## Core design principles
1. **Scale stack with handoff contracts.** Each scale runs its own model and receives boundary
   conditions from its parent: temperature, pH, pressure, dissolved CO2/Fe2+/H2, flow and mineralogy.
   The parent's state is frozen while you are zoomed in.
2. **Time is per scale.** Planet: Myr/s. Vent: years/s. Pore: s–hours/s. Molecular: µs–ms/s.
   The HUD always shows the scale and the time rate.
3. **Coarse-graining over brute force.** Use reaction networks, lattice and particle models (DPD) and
   stochastic kinetics instead of all-atom molecular dynamics. These are real methods; we state their
   limits.
4. **Hypotheses are first-class.** Alkaline hydrothermal vents are the primary deep-ocean scenario. We
   contrast them with black smokers, and later with warm little ponds, which need wet–dry cycles.
   Outcomes can fail. That is scientifically honest and a good basis for gameplay.
5. **Deterministic and seeded**, so moments can be replayed, tested and shared.
6. **No asserted one-true-origin chain.** Membrane self-assembly and polymer formation
   can be explored in either order or together. Each requires real precursor chemistry;
   a run's success is a hypothesis-driven model result, not a historical fact.

## Scale ladder

| Scale | Size | Model | Seeded by |
| --- | --- | --- | --- |
| S0 Planet | 10^7 m | existing GPU evolution | — |
| S1 Ocean region | 10^5 m | regional bathymetry, ridge, crust age, heat flow | S0 lithosphere/ocean state |
| S2 Vent field | 10^2–10^3 m | serpentinisation fluid flux, chimney precipitation, buoyant plume | S1 ridge distance, mantle rock, ocean chemistry |
| S3 Chimney wall/pores | 10^-2–10^-5 m | advection–diffusion–reaction grid, thermophoresis, pH/redox gradients across FeS walls | S2 fluid and ocean compositions |
| S4 Molecular assembly | 10^-6–10^-9 m | DPD amphiphiles (micelle→vesicle), stochastic polymerisation/hydrolysis, templating | S3 local concentrations, T, pH, ions |

## Milestones

### M0 — Foundations (architecture and research)
- Write a research brief with sources. Topics: Russell & Martin (alkaline vents), Lane & Martin (proton
  gradients), Kelley 2001/2005 (Lost City), Baaske 2007 (thermophoretic trapping in pores), Hanczyc &
  Szostak 2003 (vesicles on clay), Chen & Szostak 2004 (competitive vesicle growth), Ferris
  (montmorillonite RNA oligomers), Preiner 2020 (CO2 → formate/pyruvate on minerals), Eigen error
  threshold, and the contested timing evidence (4.1 Ga graphite, ~3.7–3.5 Ga stromatolites).
- Define the scale-stack interface: `enter(scaleState, boundaryConditions)` and `exit() → summary`,
  plus a time-rate contract and per-scale liberties.
- Decide on compute: WebGL2 (current) vs WebGPU compute for particle scales (see open decisions).
- **Exit:** reviewed design doc, an interface stub, and a S0→S1 "freeze and hand off" round-trip test.

### M1 — Key moments and the orbital → seafloor zoom
- Bookmarks on the timeline (e.g. "first oceans", "first stable vents ~4.3 Ga"), placed by S0 state.
  Ridges and serpentinising crust must exist; they are not hard-coded.
- Continuous camera path: orbit → cloud deck → ocean surface → water column with physical light
  attenuation (dark below ~200 m, aphotic below ~1 km) → abyssal seafloor.
- The S1 region is generated from S0 texels: bathymetry, ridge axis, crust age and thermal subsidence
  (depth ∝ √age), and sediment.
- **Exit:** a seamless zoom at ≥ 30 fps. Seafloor depth matches S0 within tolerance. Playwright test
  for enter/exit.

### M2 — Hydrothermal vent field (S2)
- Alkaline vent model: fluid at pH 9–11 and ~40–90 °C, H2-rich from serpentinisation, against an
  acidic (pH ~5.5–6.5), CO2- and Fe2+-rich Hadean ocean. Black smokers (~350–400 °C, acidic) on the
  ridge axis for contrast.
- Chimney growth from precipitation (carbonate/brucite, Fe(Ni)S, silica) with a buoyant plume and
  particle tracers, rendered as real geometry.
- **Exit:** chimney heights and growth rates in observed ranges (Lost City up to ~60 m). Fluid mass
  and heat budgets close. Visual check.

### M3 — Pore micro-environment (S3)
- 2D/3D reaction–diffusion grid in chimney micropores: thermal gradients, thermophoretic
  concentration, pH and redox across thin FeS membranes (a natural proton gradient).
- Tracked species: H2, CO2, formate, acetate, pyruvate, amino-acid and nucleotide proxies (lumped),
  fatty acids, Mg2+, Na+/K+.
- **Exit:** reproduces Baaske-type accumulation factors (orders of magnitude) and steady gradients.
  Mass is conserved. Unit tests on the kinetics.

### M4 — Polychains: polymerisation chemistry (S4a)
- Stochastic (Gillespie / tau-leap) polymer growth with ligation vs hydrolysis, and length
  distributions. Mineral-surface catalysis (montmorillonite) and template-directed ligation.
- Visualise individual chains growing, breaking and templating inside a pore.
- **Exit:** length distributions match published oligomerisation experiments qualitatively. Hydrolysis
  dominates in bulk water but not in concentrating pores. A failure case is shown.

### M5 — First membranes (S4b)
- Coarse-grained DPD amphiphiles. Self-assembly above the critical aggregation concentration:
  micelles → bilayers → vesicles. Sensitivity to pH (fatty acid/soap), salt, Mg2+ and temperature.
- Growth by micelle uptake, and shear-induced division (Szostak).
- **Exit:** vesicles form only inside the validated pH/ionic window. Vesicle sizes are plausible.
  GPU performance target met.

### M6 — Protocells
- Encapsulation of polymers in vesicles. Osmotic competitive growth (Chen & Szostak 2004).
  Template replication with error rates; Eigen error threshold. Selection emerges in a population.
- **Exit:** heredity and selection emerge rather than being scripted; above the error threshold the
  information collapses. Every result is labelled as model-dependent.

### M7 — Zoom back out: biosphere feedback
- Protocell/biosphere outcomes feed back to S0 as an early biosphere signal: microbial mats,
  stromatolites (~3.5 Ga), and later oxygenic photosynthesis → Great Oxidation Event (~2.4 Ga) in the
  atmosphere and ocean colour.
- **Exit:** the S0 atmosphere/ocean state changes because of the life model, not the clock.

### M8 — Gamification
- Modes: **Observe** (documentary, today), **Experiment** (adjust vent chemistry, temperature, minerals
  within physical bounds), **Challenge** (e.g. "form a stable vesicle", "sustain a replicator above the
  error threshold", "reach the GOE").
- Progress is gated by real thresholds. A science codex unlocks references. Seeds/moments can be shared.
- **Exit:** a playable loop from the planet to a protocell, with telemetry and usability tests.

## Key moments across prebiotic chemistry and the rise of life

The player should be able to follow a chain of progressively smaller,
scientifically constrained experiments—not a scripted sequence in which
Earth's **first** membrane definitely preceded its **first** polymer, or
vice versa. Those orderings and settings remain contested. The UI presents
parallel hypotheses and lets them combine when local physics supports it.

| Milestone | Moment worthy of a zoom | Trigger / what must be real |
| --- | --- | --- |
| M1–M2 | A habitable ocean setting and active vent | S0 water/crust + S1 ridge/bathymetry + S2 water/rock/heat flux |
| M3 | Stable pore gradients | S3 concentrations, pH, heat and mineral boundary fields |
| M4 | A polymer chain grows, breaks or templates | S4 stochastic chemistry with mass balance and competing hydrolysis |
| M5 | Amphiphiles self-assemble into a vesicle | S4 membrane phase behavior in a supported pH/salinity window |
| M6 | A protocell encapsulates a replicator, grows or fails | Both M4 and M5 outcomes; energy/material budget and inheritance |
| M7 | Early microbial ecosystem changes local chemistry | Explicit biosphere ↔ geochemistry coupling; outcome can fail |
| M8 | Gamified Observe / Experiment / Challenge | Progress from real thresholds; no guaranteed creation of life |

M4 and M5 are **parallel experimental branches**, not certified chronological
historical events. The first surviving replicating protocell in a run is
a *model outcome*, not a claim to have recreated the origin of terrestrial life.
The same rule applies to any synthetic first-metabolism/first-RNA milestone.

### M9 — Early evolution and oxygenic ecosystems (future)

- Keep lineage-level heritable variation, replication error, selection,
  growth, extinction and environmental feedback coupled. Track populations,
  not just the first successful protocell.
- Allow model-dependent microbial mats, novel metabolic pathways and eventual
  oxygenic photosynthesis. The source-backed Great Oxidation Event (~2.4 Ga)
  is a *historical Earth-history anchor*, not automatically the frame
  in which the first oxygen-producing microbe appears.
- **Exit:** meaningful ecological transitions emerge from feedback and remain
  explorable from orbit down to organisms with documented uncertainty.

### M10 — Eukaryotes, multicellularity and animal diversification (future)

- Transition from microbial communities to more complex cells, symbiosis,
  multicellular developmental programs and marine food webs.
- Historic markers (eukaryotes, multicellular life, Cambrian radiation)
  use fossil/phylogenetic constraints; a game's emergent lineage is a
  simulation and is labelled separately.
- **Exit:** visible evolutionary lineage history with replayable physical
  habitat context, rather than a sequence of predetermined monster reveals.

### M11 — Life conquers land (future)

- First land plants and terrestrial arthropods are separate evolutionary
  milestones; vertebrate tetrapod land entry comes much later.
  Earliest known terrestrial animal traces are roughly 450 Ma, while
  tetrapod transitions are associated with the Late Devonian (~375 Ma).
- Zoom from globe to coastline, soil, plant and small arthropod habitats
  through genuine elevation, atmospheric and ecosystem models.
- **Exit:** correctly ordered, sourced, visually explorable key moments from
  microbial life to early land ecosystems, with time uncertainty and
  species-specific environmental constraints.

## Cross-cutting
- **Performance:** each scale ≥ 30 fps on the demo laptop. Compute budgets set per milestone.
- **Testing:** unit tests for kinetics and conservation (C# or JS). Playwright tests for zoom transitions
  and HUD. Fixed-seed screenshot comparisons for visual milestones.
- **Honesty:** each scale shows its liberties and the hypothesis it is under in the HUD.
- **Order and parallelism:** M0 → M1 → M2 are sequential. M3–M5 can be prototyped as standalone
  sandboxes in parallel, then integrated. M6 needs M4+M5. M8 can start design after M2.
  M9–M11 require the validated ecological/lineage and habitat foundations; no fixed dates masquerade as model output.

## Decisions (2026-10-14)
1. **Compute:** WebGPU compute for the particle/reaction scales (S3–S4). The existing WebGL2 globe stays
   as it is; WebGPU support is detected and a clear message is shown if it is missing.
2. **Scenario:** alkaline deep-sea hydrothermal vents first (black smokers only as contrast). Warm
   little ponds stay out of scope for now.
3. **Fidelity:** named chemistry: nucleotides, amino acids, fatty acids, minerals. Lumping is allowed
   but must be documented.
4. **Gamification flavour:** open; decide during M2–M3, before M8.
