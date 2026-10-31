# E7 / Life-M0 — curated geological turning points and future life zooms

**Workstream:** PF-EARTH-20261013-A. **Status:** E7 model-triggered turning points and 1× S0 camera handoff shipped in 0.0.51.0; the more expressive horizontal timeline is the next visual slice, pending iPhone/desktop review. Follow delivery order and acceptance gates in [the journey milestone plan](earth-journey-milestone-plan.md).

## Accepted interaction

A **key moment is not every simulated visual effect**. Hadean impact storms may include
enormous numbers of impacts in reality. The existing S0 magma-impact renderer is an
illustrative, viewer-biased display hypothesis, not a measured bombardment record.
Those ordinary impact flashes continue as **ambient planet activity**: no marker,
no automatic camera hijack, no false “historic meteor event.”

The horizontal rail shows a moving epoch indicator and small circular markers on a
clearly labelled logarithmic scale from planetary formation to 4.567 Gyr of elapsed
history. Each admitted event has a source classification and its actual model or
reconstructed epoch. Key moments are limited to material changes, not arbitrary
timed cutscenes.

| Class | Trigger | Auto zoom? | Scientific claim |
| --- | --- | --- | --- |
| Ambient (ordinary impacts, eruptions) | Existing visual emitter | No | Display hypothesis only |
| Planetary turning point (first slice) | Actual S0 rocky-lid formation, 50% ocean-inventory rain-out, simplified plate-regime onset | Yes, planetary orbital close-up | Exact **model** threshold, not a historically observed location |
| Exceptional geological occurrence (future) | Documented Moon-forming/Chicxulub-scale event plus appropriate forcing/location | Only with state/reconstruction handoff | Label inference, reconstruction, and date uncertainty |
| Origin-of-life experiment (future) | M2–M6 local environment and kinetic thresholds | Only once the real S1–S4 solver exists | A **hypothesis-dependent outcome**, never Earth's proven one-time historical origin |
| Evolutionary landmark (future) | Biosphere model or sourced historical constraint with geographic context | Only with corresponding habitat/organism simulation | Distinguish reconstruction, fossil evidence, and model hypothesis |

### Implemented first-slice events

1. **Persistent rocky lid forms** when `lithosphereReady` changes false → true.
2. **Steam rains out into oceans** when S0's normalized condensed water inventory
   crosses 0.5. This does **not** claim to date Earth's first water.
3. **Plate tectonics begins (model)** when the thermal regime first becomes
   `plates`. Earth's true plate-tectonics onset remains debated.

The camera eases from the existing orbit toward a closer view of the **same**
GPU planet for 14 visible local seconds (1.2 s approach, 2 s retreat). An event
without a local spatial coordinate gets a **planet-wide** close-up—not a fabricated
specific ocean, rock formation, vent or tectonic boundary. Parent geological
time is frozen; the independent inspection clock runs 1× only while visible.
On exit, the original camera, geological playback and ambient-impact countdown
resume. Past states cannot be replayed without a stored physical checkpoint.

A current event marker can be inspected while its model state is still current.
Older dots are archival until physical snapshot/replay support exists. Restarting
the seeded planet resets the milestone history. The key-moment list is bounded
to 64 markers; ordinary impacts do not consume slots.

## Acceptance gates

- Pure tests: ambient impacts do not enter the key-moment list; state-driven
  thresholds admit each geological event once and in chronological order.
- Browser: actual evolving S0 stage change produces the dot, a planetary
  auto-zoom, a fixed geological epoch during local time, and a restored
  original orbit with resumed playback. Confirm ordinary impacts remain visual.
- Existing seed replay, Earth history, performance readbacks and Snowball/rock
  research routes stay unchanged.
- Full .NET/WASM, browser shards, published exact-SHA smoke, and separate
  iPhone/PC visual review before full device acceptance.
- No fabricated early atmosphere chemistry, vents, protocells, polymers,
  individual animal organisms, or geological history.

### Scientific provenance

- Experimental protocell membranes are a plausible hypothesis, not direct
  fossils of Earth's first cell: https://www.nature.com/articles/s43247-024-01372-0
- Polymer synthesis in water faces genuine thermodynamic/kinetic constraints:
  https://www.nature.com/articles/s41570-024-00648-5
- Earliest life evidence and land colonization dates have uncertainty:
  https://astrobiology.nasa.gov/news/oldest-evidence-for-early-life-on-earth-dated-to-at-least-377-billion-years/
  and https://pubmed.ncbi.nlm.nih.gov/14731304/

## Next slices

- **E7:** grounded geological-era bookmarks, inspectable uncertainty, time scrub and transition
  policy. Event occurrence must be state-derived or tied to explicitly documented historical
  reconstructions, not claimed to be the result of the physics if it is not.
- **M0:** parent/child immutable handoff snapshot with regional height, thermal, ocean and
  chemistry boundary fields. The current special-event M0 works only at S0 close-up scale.
- **M1:** dynamic S0→S1 bathymetry/ridge region, mesh/morphing from orbit into surface and sea
  without the 39 km texel limit; pass mass/energy/geographic constraints explicitly.
- **M2 onward:** validated serpentinization vent field, pore chemistry and molecular hypotheses
  on per-scale numerical clocks and documented scientific validity ranges. User-facing
  true surface/chemistry real-time simulation must wait for these solvers.

No synthetic vents, plausible-looking polymers, arbitrary molecule success or apparent geological
time advancement are accepted as implemented features at this first gate.
