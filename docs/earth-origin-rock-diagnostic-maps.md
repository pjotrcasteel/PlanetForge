# Earth-origin canonical rock diagnostic maps (prototype)

**Platform:** PC-first; iPhone Safari is the quick validation device.  
**Scope:** main globe and local terrain, read-only, opt-in, no new physics.

Use the **MAP** selector in the rock-column HUD to switch modes without resetting time, or start directly with `?visualTest=1&rockColumns=1&rockMap=temperature` or `?visualTest=1&rockColumns=1&rockMap=melt`. Without `rockMap`, the physical material renderer is unchanged.

- **Temperature:** blue = 500 K or colder, red = 2500 K or hotter; intermediate temperatures linearly interpolated in display space. The legend is fixed across snapshots; this is **not** a calibrated blackbody appearance.
- **Melt fraction:** blue = fully solid (0), red = fully molten (1); intermediate values linearly interpolated. This is phase, **not** solid thickness or mechanical crust.
- Both maps sample the **same canonical spatial rock texture** used by the physical renderer; they do not mutate rock state or introduce procedural surface noise.
- Compare the same seed, angle and simulation time in physical, temperature and melt modes. At T+0 expect a largely red melt map; at T+4h the top layer should be blue while deeper melt persists. Temperature must not change merely because the map is switched.
- Report seams, square patches, inconsistent globe/local colors, blurry LOD, UI obstruction and Safari performance. A fixed two-endpoint map can hide narrow differences; future PC UI should expose numerical legends, a toggle, ranges and physically separate overlays (solid thickness, heat flux, ice mass and tectonic stress) only when those canonical fields exist.

**Release gate:** prototype must pass full browser/WebGL shader tests, merged main deployment and production smoke before being described as live. This documentation does not claim visual acceptance or release.
