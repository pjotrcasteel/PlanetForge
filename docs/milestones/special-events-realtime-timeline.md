# E7 / Life-M0 — witnessed special events, regional focus, and 1× local time

**Workstream:** PF-EARTH-20261013-A. **Status:** implementation candidate, not deployed or visually accepted.

## Accepted interaction

The existing S0 GPU Earth-history planet is the parent. A named event enters the timeline only when
the evolving model produces an event with an actual location and parent geological clock.
The first admitted type is the existing seeded magma-ocean impact. The timeline is capped at the
eight most recently witnessed events; restarting the planet clears these bookings.

Selecting a bookmark:
1. Records the exact parent epoch, the camera pose, and whether S0 was playing.
2. Pauses S0 without replacing or cloning the evolving planet.
3. Eases the same main globe's camera to the event's *actual* spherical direction, limited to a
   regional orbital close-up because the S0 GPU simulation resolves approximately 39 km/texel.
4. Advances a **separate event time counter at 1 second per real visible second**.
   The pre-existing impact optical sequence may progress on this local clock; the geological
   epoch, tectonics and atmosphere do not advance. A hidden tab does not accumulate catch-up.
5. Restores the prior camera and playback state on return. The parent geological epoch is
   unchanged except when normal S0 playback was running before and is resumed afterward.

The event clock is not an independent invented geological timeline: it is explicitly a
bounded read-only inspection session. The impact emitter is itself a declared *visual hypothesis*
biased toward the visible hemisphere, not a calibrated global impact-rate model. No claim of
physical real-time crater formation, local LOD equivalence, fluid chemistry or abiogenesis.

## Acceptance gates

- Pure tests: no duplicate events, eight-event retention, stable geographical orientation,
  invalid direction rejection, 1× clock and background elapsed-time safety.
- Browser: real seeded impact emitter creates an event, selecting it stops the identical
  physical epoch, eases the camera, advances local seconds, and exit restores prior orbit/playback.
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
