# E7 / Life-M0 — witnessed special events, regional focus, and 1× local time

**Workstream:** PF-EARTH-20261013-A. **Status:** horizontal auto-focus implementation candidate; automated checks and human device review pending.

## Accepted interaction

The existing S0 GPU Earth-history planet is the parent. A named event enters the timeline only when
the evolving model produces an event with an actual location and parent geological clock.
The first admitted type is the existing seeded magma-ocean impact. The timeline is capped at the
eight most recently witnessed events; restarting the planet clears these bookings.

The bottom-mounted horizontal timeline uses **small circular event markers** and a moving
present-time indicator. Their positions are on a clearly labelled **logarithmic
4.567-Gyr scale**, to keep early impacts visible rather than collapsing them
at the origin. The markers preserve actual measured event times, with no
invented date or geological occurrence.

When the playing S0 model produces a new witnessed impact, the camera
**automatically** enters the regional close-up; a user can also select a
still-inspectable marker. This transition:
1. Captures the parent epoch, camera pose, and playback state.
2. Pauses S0 without replacing or cloning the evolving planet.
3. Eases the existing GPU globe toward the model event's spherical location
   during the first 1.2 seconds, holds the view and eases back over the
   final 2 seconds of a 14-second viewing window.
4. Advances a **separate local event clock at one simulated second per real
   visible second**. Geological time stays frozen. A hidden tab never
   accumulates catch-up.
5. Preserves the next impact's scheduled visual countdown during inspection, so
   close-ups cannot chain immediately merely because local event time passed.
6. Restores the camera and prior playback automatically after the window;
   **Return now** allows the viewer to exit early. Past optical states cannot
   be replayed as if they still existed.


The event clock is not an independent invented geological timeline: it is explicitly a
bounded read-only inspection session. The impact emitter is itself a declared *visual hypothesis*
biased toward the visible hemisphere, not a calibrated global impact-rate model. No claim of
physical real-time crater formation, local LOD equivalence, fluid chemistry or abiogenesis.

## Acceptance gates

- Pure tests: no duplicate events, eight-event retention, stable geographical orientation,
  invalid direction rejection, 1× clock and background elapsed-time safety.
- Browser: the real seeded impact emitter produces a dot and automatically enters the original
  globe's local close-up; the parent epoch is stable and the camera/playback auto-restore.
- Existing Earth/Snowball research routes and seed replay must remain untouched.
- Full .NET build, WASM, browser shards, production exact-SHA smoke and iPhone/PC visual review
  before labeling a release complete.

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
