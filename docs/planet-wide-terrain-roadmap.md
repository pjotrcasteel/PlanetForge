# PlanetForge — from Hero research tiles to one planetary terrain

**Status:** A1, A2.1 and A2.2 completed and deployed. A2.3 adds bounded, atomic region restoration. Unreleased geological realism work remains isolated in draft PR #113.

## Decision

The Hero Terrain Lab is an observer and test environment, **not a second planet generator**. Every point on the sphere must have one stable geological identity regardless of camera, mesh resolution, save/load, seed, or whether a research tile has been inspected before.

A planet-wide geological elevation source already exists in `ProceduralPlanetElevationSource`. The cube-sphere `PlanetSurfaceTileSampler` and local `PlanetLocalSurfacePatchSampler` both sample `IPlanetElevationSource`. `PlanetRegionalGeologyAtlas` already blends physical, geographically anchored evolved-height deltas for overlapping neighbors and nested scales.

The missing integration contract was publication and invalidation. `PlanetTerrainRegionProvider` is the shared, application-registered gateway: it starts with the unchanged canonical source and may atomically publish a complete immutable geological atlas. Publication increments a transient revision. Both orbit and local meshes use the same provider; the orbital GPU mesh cache and local ground cache must invalidate when its revision changes. The rendered snapshot's terrain revision must change too, so water/normal overlays refresh. The original canonical source remains directly available for generating new regional research baselines; otherwise the same erosion history could be applied twice.

Research regions are **not automatically applied to gameplay** just because they were generated. The provider remains canonical-only until a higher-level simulation owner explicitly calls `ReplaceLayers`. It is intentionally not wired to a Hero button or save file in A1.

## Milestones

### A1 — Shared planetary geology provider (completed)

- Single source for globe, adaptive cube-sphere LOD and near-ground terrain.
- Atomic opt-in publication of immutable, nested regional delta layers.
- Correct publication revision propagation and geometry cache invalidation.
- Preserve existing worlds when no overlays have been published; no save schema change.
- Tests for overlapping region compositing, seed isolation, coarse/ground identity, and mesh regeneration.

### A2 — Geographically addressed regional evolution (in progress)

- Durable region identity based on canonical planet addresses, scale, and seed rather than transient Hero screen coordinates.
- Bound reusable region storage/cache with explicit loading and eviction; no global 257² grids.
- Replayable regional erosion snapshots tied to canonical generation and model schema versions.
- A spatial index for region queries (not an O(all regions) iteration per planet vertex).
- Deterministic overlap ownership and neighboring edge agreement independent of load order.

### A2.5 — Moving plate and crust model (planned)

- Fixed geographical region IDs are **Eulerian observation windows**, not tectonic plates.
- Persistent crust parcel IDs represent moving material; plate IDs represent changing kinematic groups. A plate may contain both oceanic and continental crust, and parcels may change plate ownership in a rift or collision.
- Epochs identify time snapshots but do **not** calibrate erosion passes to years.
- Begin with deterministic plate motions as sphere rotations about Euler poles; add rifting, subduction, collisions and mass conservation in separately validated phases.
- A region samples its material at the requested epoch; crust can cross region boundaries without being re-identified.

### A3 — Streaming and geographic LOD

- Demand-driven regional geology and tile streaming across orbit-to-ground travel.
- Cross-LOD morphing/stitching while retaining identical shared-source boundary positions.
- Mobile frame-time/memory budgets and bounded CPU sampling.
- Hero becomes a selectable region inspector on the real planet, not the proprietor of simulation state.

### A4 — Connected global hydrology and sediment

- Planet-wide drainage connectivity and correct ocean outlets.
- Cross-region runoff, sediment import/export, lakes and rivers tied to real physical terrain.
- Closed accounting for transport across simulation-region boundaries.
- Only then promote regional erosion histories to normal gameplay planet generation.

### A5 — Additional Hero inspectors

- Independent inspectable landmark types: mountain systems, river basins, lakes, volcanic provinces, coastlines.
- Use the same shared region provider, never separate source elevation or decorations masquerading as geology.

### A6 — Landform realism and world acceptance

- Multiseed, 128/32/8 km full-region comparisons against geological references.
- Remove numerical terraces and isolated trenches, maintain physical mass budgets.
- Check stable landmark identity across all zoom levels and repeated visits.
- Gate releases on visible realism and mobile performance in addition to tests.

## Out-of-scope for A1

A published atlas is a height composition, **not** global erosion physics. Seamless height blending does not prove sediment flux conservation. Region publication is in-memory and opt-in; no implicit persistence, background generation, multi-user synchronization, or full-world dense mesh. Until A4 and the visual quality gate pass, the gameplay planet continues to use the original canonical terrain by default.


## A2.1 — Fixed region/epoch checkpoint contracts

The first A2 slice keeps geographical identity in `PlanetGeologicalRegionId` (canonical generation version, seed, cube face, quadtree level and tile indices) and **separates** it from `PlanetGeologicalEpoch` (absolute years-before-present). Neither identity is a tectonic plate. `PlanetCrustParcelId` represents future moving material and is not derived from the tile address.

`PlanetGeologicalRegionHistory` couples this address/epoch to validated original and evolved physical snapshots, a planet radius and immutable reconstructed overlay semantics. The archive has a distinct version, stores both grids, retains the existing erosion snapshot schema and generator identity, and validates all metadata plus sediment balance on reload. It does not infer geological years from the simulation's integer erosion iteration count.

`PlanetGeologicalRegionCache` is a bounded, explicitly controlled in-memory LRU holding defensive copies of the archived region states. Eviction never mutates canonical geology. `PlanetGeologicalRegionArchive.Serialize/Deserialize` provides portable JSON for an eventual durable repository; **it is not yet an automatically persisted browser database**. Users of A2 can save/load this JSON in their own storage, but cloud/device persistence and migration are later work.

`PlanetTerrainRegionProvider.PublishHistories` selects one compatible epoch and one generated world and composes loaded, previously evolved geographic overlays in stable order. Incompatible generator versions, world identities, duplicate region IDs or mixed epochs are rejected before replacing the current atlas. Publishing remains **opt-in**, with a single transient revision update and A1 cache invalidation. The method does no erosion, tectonic evolution or streaming.

### Remaining A2 acceptance gates

- Durable storage adapter, resumable region loading and bounded eviction across sessions (not just portable JSON).
- Spatial indexing for large numbers of regions; the current atlas still scans each region per sampled vertex.
- Deterministic edge ownership and neighboring sediment exchange are not implied by overlapping height fades.
- Epoch simulation physics and actual moving plate/crust state belong to A2.5 and later; the epoch labels alone do not move continents.

 
## A2.2 — IndexedDB durability and bounded region lookup

- `IPlanetGeologicalRegionDocumentStore` is the asynchronous checkpoint document contract. `PlanetGeologicalRegionRepository` uses versioned, validated `PlanetGeologicalRegionArchive` JSON and the existing bounded defensive LRU cache. Durability completes **before** a write is inserted into memory. Reload checks that world, fixed region address and geological epoch match the requested key. Deletion removes both the durable document and its cache entry.
- The browser registers `IndexedDbGeologicalRegionStore` in WebAssembly DI and persists each archive with an atomic IndexedDB transaction, isolated from gameplay saves. Reloading the same region and epoch can reconstruct identical geological heightfields with a newly created repository. The archive remains explicitly opt-in; opening PlanetForge does **not** publish research geology or auto-load every saved region.
- **Safari Private Browsing may discard IndexedDB when the private session ends**. Browser storage can also be cleared or become quota-limited. Later import/export and explicit user-visible storage controls are required before long-term work is considered reliably backed up.
- The `PlanetRegionalGeologySpatialIndex` creates immutable, seed-aware conservative buckets around each published region's spherical support cap. Each physical sample now checks the small bucket-specific candidate list instead of scanning every archived region worldwide. The cap includes the entire gnomonic support and perimeter fade, and per-layer source ordering is preserved so weighted overlap blending remains numerically unchanged. The index does not invent shared hydrology.
- C# tests cover persistence across repository/cache recreation, no cache pollution on corrupt archives or failed writes, deletion by epoch, and index equivalence to a complete brute-force globe-wide scan including near-boundary and polar positions. A browser test writes two epochs to IndexedDB, reloads the page and verifies independent restoration/deletion.

### What remains before A2 can be called complete

- IndexedDB quota management, backup/export, and an explicit UI workflow for inspecting/restoring stored regions.
- Demand-driven loading/eviction and streaming near the camera, with revision-aware sampling snapshots across asynchronous transitions (A3).
- Exact edge ownership and inter-region physical sediment conservation (A4).
- Orbital image sharpness: current reddish globe shows broad features but lacks clearly resolved mountain provinces at full-disc distance. Treat this as a separate **orbital LOD, normals and physical-relief quality gate**, not a reason to apply artificial shader-only mountains.

## A2.3 — Atomic region restoration (PF 0.0.37.17)

`PlanetGeologicalRegionSession` is an explicit owner for loading an archived
selection into the shared terrain provider. It validates one current generated
world, unique region addresses and one requested epoch before loading. At most
eight regions and 524,288 grid samples may be active in one selection. Original
and evolved arrays both exist for each grid; the sample limit is not a total
process-memory budget. The repository's separate bounded LRU still applies.

Only one selection materializes archives at a time. All archives must exist,
deserialize and pass their identity/model checks before an immutable atlas is
built and atomically published. Missing, corrupt or oversized selections leave
the previously published source and revision unchanged. Loading can populate
the disposable repository cache even when publication is subsequently canceled.

A newer valid request supersedes every older request; an obsolete completion
throws `OperationCanceledException` and cannot publish, even if the storage
adapter ignored cancellation. `Clear` invalidates pending restorations and
returns to canonical terrain. Publication and request ordering share a lock,
so a delayed callback cannot resurrect cleared terrain. Callers should use one
session owner per shared provider; direct provider mutations bypass this
request-ordering contract. Already captured sampling sources remain immutable.

Tests cover deterministic leave/return after repository/cache reconstruction,
epoch switching, missing/corrupt archives, selection/sample budgets,
superseding requests, cancellation with a noncooperative storage adapter and
clear-during-load. This is DI-registered infrastructure, not a new Hero button
or automatic gameplay activation. No geological model or save schema changes.
Version 0.0.37.16 remains associated with unreleased draft PR #113; this release
uses 0.0.37.17 without incorporating that experimental terrain rewrite.

Next: archive inspection and backup/import/export controls, then camera demand
selection and streaming. Cross-region sediment exchange and the visible
landform realism gates remain separate requirements; the latest 8 km Hero
screenshots are still evidence of unresolved trench/shelf artifacts.
 
## A2.4 — Inspectable archive workflow (PF 0.0.37.18)

The dedicated Geological Archives screen (`?geologyArchive=1`) lets a player browse a bounded paginated list of locally saved archives, inspect fixed geographic and epoch metadata, import a validated geological JSON backup, export the unchanged validated bytes, and explicitly delete one archived epoch.

Import verifies the versioned archive, geographic identity, grid dimensions, physical sample values and sediment conservation through the existing C# deserializer. Data is never published to the active planet simply by opening, saving or exporting an archive. The importer warns when a backup's canonical generation version is not the current source model. Files must be <=40 MB and are validated before persistence.

When an archive already exists at the same planet/region/epoch, importing requires affirmative replacement permission. Save and delete both use IndexedDB **compare-and-swap inside a single readwrite transaction**, based on the previously inspected stored bytes. Another tab updating or removing a record causes the operation to fail safely, rather than overwriting a newer checkpoint. The in-memory region cache is cleared after a successful mutation.

Keys are paginated (ten per page) without deserializing large archives; a standalone index screen never generates or loads a dense planet terrain. A dedicated Playwright flow checks validation, reload, export, replacement, concurrent-edit conflict, pagination and mobile layout.

Safari Private Browsing may delete local IndexedDB content when the session ends, so downloadable backups are an essential part of the experience. This is supporting tooling, **not** the main gameplay feature. The primary visual acceptance target remains the **whole globe and LOD 0–3**. Canonical geology, moving crust, recognizable mountain ranges/volcanoes and connected snowmelt rivers/lakes/oceans are future physical milestones. No shader-generated pseudo-geography is accepted in place of actual planetary simulation.

 
## A3.1 — Camera-driven archived geology (PF 0.0.37.19)

The planet can opt into a research preview with `?regionalStreaming=1`. The normal game remains canonical-only, and Hero research does **not** implicitly become the whole planet. At boot, the preview queries the browser's IndexedDB **key catalogue only** for the current world generation/seed and geological present (YBP 0), capped at 256 keys; historical epochs are never chosen automatically.

When the camera enters adaptive orbit (distance <1.35 planet radii), the C# selector seeks the nearest geographically addressed saved region within a conservative short angular range, rather than scanning and decoding large archives on every frame. The streamer loads only **one** archive candidate, verifies the camera actually intersects its physical erosion support, then restores via A2.3 atomic publication. Leaving that location or zooming back out restores canonical terrain, and revision-aware globe/ground meshes re-sample the shared geology. Duplicate views reuse the already loaded selection; superseded or cancelled loads cannot republish a stale region.

The preview is intentionally behind a query flag until the visual and device-performance gates are satisfied. If there are no archived geological regions for the current seed, no geology is fabricated or loaded. Archived YBP values are not calibrated geological evolution. Further milestones must refine LOD 0–3 visuals, handle more than one adjacent region with stable ownership and sediment flow, implement camera-friendly asynchronous disk prefetch, and add physically consistent tectonic motion, ice and drainage.


## A2.5.1 — Canonical plate kinematics (PF 0.0.37.20)

The existing `ProceduralPlanetElevationSource` has eighteen canonical seeded geological plate centres with an instantaneous tangent motion field. This milestone **reuses those exact 18 identities**. `PlanetTectonicPlateId` distinguishes the generated-world identity from a plate index. `PlanetTectonicPlateKinematics` exposes immutable present-day plate states and rotations backwards to a requested `PlanetGeologicalEpoch`.

An Euler pole is obtained from each canonical centre and existing tangent motion vector; rotation uses Rodrigues' formula on normalized spherical coordinates. Present (YBP 0) exactly matches the existing static plates, and mapping a material direction backwards in geological time and forward again recovers the original location. Moving material and fixed `PlanetGeologicalRegionId` locations are deliberately separate.

The initial speed calibration of 0.35–1 × 60 km/Myr (about 2.1–6 cm/year) is a **model assumption**, not tectonic evolution measured from real data. Rotation preserves the unit-sphere norm and is deterministic for seed, identity and epoch. The nearest-moving-centre helper is a diagnostic Voronoi proxy only; it does **not** advect complete crust polygons or conserve plate area. Continental crust still requires a parcel/crust-composition model, and changing the plate-centre positions alone does not generate physical mountain uplift, trenches, volcanoes or ocean basins.

**No gameplay or renderer behavior changes:** the orbital and regional geometry continues to use the canonical present-day static geological source. The new model stays available for further evolution work and is covered by regression tests for canonical identity/motion agreement, seeded determinism, sphere preservation, inverse epoch mapping and invalid indices.

### Next physical gates

1. Model persistent crust material and plate membership separately from Eulerian geographical regions; preserve material through plate-boundary interactions.
2. Use relative plate velocities to classify convergence/divergence/shear, then derive uplift, rifting, subduction and volcanism over time.
3. Propagate the resulting physical surface through orbit and LOD 0–3 before expanding local Hero inspectors; avoid shader-only landforms.
4. Resolve world-wide drainage and inter-region sediment flow before allowing moving mountains and meltwater to restructure lakes and oceans.

## A2.5.2 — Relative motion and plate-boundary diagnostics (PF 0.0.37.21)

`PlanetTectonicBoundaryKinematics` computes contact-relative velocity from the **same canonical plate Euler poles** introduced in A2.5.1. For one geographic position and a requested geological epoch, it finds the two nearest moving plate centres and evaluates their velocities at that *position*, rather than incorrectly subtracting only the centre velocities.

The great-circle bisector between two centre points is a **provisional Voronoi contact**, not a topologically conserved crust boundary. The local tangent normal is oriented by ascending persistent plate ID, so swapping the input order cannot reverse the signs. Compression is positive when plates approach, negative when they separate. Signed shear measures lateral motion; a neutral boundary, transform contact, convergence, divergence and oblique combinations are classified separately. The influence envelope is bounded to 0–0.035 radians from that bisector, with a smooth falloff beyond 0.010 radians. Widths and classification thresholds are explicit **research assumptions**, not measured elastic properties or physical forces.

C# tests cover synthetic convergent, divergent, transform, oblique and neutral motions; plate-order invariance; locality and undefined coincident-centre contacts; invalid cross-world ownership; seed/epoch determinism; and unchanged canonical surface sampling. The diagnostic is not called by globe or LOD meshes, and it does not change planet generation, world saves or Hero.

**Next gate:** persistent crust parcels and genuinely evolving boundary topology, followed by an explicit material/energy-budgeted uplift, extension, trench and magma source solver. Its fields must publish through the shared planetary terrain source and be checked at orbital and LOD 0–3 scales. Boundary labels or colour overlays alone do not satisfy the geological realism goal.

## A2.5.3 — Persistent sampled crust material (PF 0.0.37.22)

`PlanetCrustMaterialParcel` gives the geological material an **immutable, world-scoped identity**, recorded as the pair `(PlanetWorldIdentity, PlanetCrustParcelId)`. Its stable reference composition, mass, representative area, density, thickness and canonical present-day position are distinct from a plate's kinematic identity and the fixed `PlanetGeologicalRegionId` where we observe it. Invalid material properties and mismatched generated-world/plate identities are rejected.

`PlanetCrustParcelCatalog.Create(seed)` constructs a bounded **1,024-sample, Fibonacci-spaced representative quadrature** over the canonical globe. Every parcel's starting plate is the nearest of the existing 18 seeded present-day centres. Its oceanic/continental starting composition uses `ProceduralPlanetElevationSource.SampleTerrainFields().ContinentalPotential`, rather than another competing land generator or a sea-level-only classification. Present-day reference thickness (oceanic ~5.5–7.5 km, continental ~30–42 km) and density are provisional physical assumptions, not a coupled lithospheric solver. Seed and current generator version reproducibly define the complete catalogue across process/browser reloads; **no terrain or gameplay save migration** is introduced.

`PositionAt(id, epoch)` moves each sample on its original plate's Euler rotation from A2.5.1, preserving its identity and material quantities. `FindNear(fixedGeographicDirection, epoch, radius)` explicitly samples a bounded geographic observation window, so the *same parcel* can leave one region and appear elsewhere over geological time without its ID changing. Mass and area totals remain fixed because no transfer, erosion or material creation/destruction occurs yet. Diagnostic lookup is **O(1,024)**, deliberately not a per-render-vertex sampler. Independent catalogues built for the same world and epoch yield identical states; the canonical elevation renderer and opt-in Hero work remain unchanged.

**Important limitation:** representative points are **not contiguous crust polygons**, do not prove coverage with no overlaps/gaps, and cannot resolve a convergent contact or transfer material between plates. Reference mass conservation is not physical boundary-flux conservation. Plate ownership remains the initial rigid frame through all reconstructed epochs. The next milestones must introduce evolving parcel topology, explicit ownership/historical checkpoints and budgeted collision/rifting/subduction deformation before publishing real uplift through the shared orbit/LOD 0–3 surface. The existing draft PR #113 remains isolated. No iPhone visual acceptance yet.

## A2.5.4 — Boundary-constrained plate-frame ownership history (PF 0.0.37.23)

The deployed A2.5.3 material catalogue provides deterministic, world-scoped parcel IDs, physical reference mass and present-day composition. A2.5.4 adds `PlanetCrustOwnershipHistory` as an **immutable, explicit, replayable** timeline of material **plate-frame transfers**. Each proposal carries the world identity, parcel ID, positive geological epoch (years-before-present), old and new plate IDs. Validation rejects cross-world transfers, unknown parcels, self-transfers, incorrect source ownership and duplicate/non-increasing epochs for the same parcel. Historical observations use the last accepted checkpoint, then rotate the material on its owning plate's *existing* Euler pole; a transfer captures the same spherical direction before and after reassignment. The original material, mass, area and continental/oceanic identity are never replaced or silently regenerated.

The transfer gate requires the material to be inside the existing moving-centre Voronoi contact corridor of exactly the old/new plate pair at that epoch, with non-neutral measured relative velocity. The saved `PlanetCrustPlateTransferEvidence` includes the original diagnostic classification and signed compression/shear components; `SuggestTransfers(epoch, maxResults)` enumerates a bounded deterministic set of **proposals** without applying them. Reconstructing the same reference world and accepting the same events replays identical material states. The original history stays untouched when branching the timeline. `FindNear` observes moving material in fixed geographic windows, independent of the original region addresses.

**Strict scope:** this timeline reconstructs increasing years-before-present from the known present-day reference. It is **not forward geological time integration**, and centre Voronoi corridors still are **not reconstructed crust polygons**. The transfer event indicates a locally defensible reference-frame change, **not** a solved subduction/rift/transform outcome. Constant reference mass is not an area/volume-conserving deformation law. No plate boundary moves a continental shape, raises terrain, generates magma or changes gameplay. The existing shared elevation source, archive identities, mobile LOD and draft Hero PR #113 are untouched.

**Next physical gate:** resolve actual material neighborhoods/topology and forward-time contact evolution with conservative area/mass bookkeeping; introduce stress, rift and subduction mechanisms into explicit terrain-evolution snapshots. Only publish through the single globe-to-LOD3 elevation source once continuous visible mountain ranges and geology survive multi-seed correctness checks. No manual iPhone visual testing requested for this infrastructure-only milestone.

## A2.5.5 — Contact graph and conservative reference columns (PF 0.0.37.24)

The canonical 1,024 material parcels now form a **deterministic, reciprocal neighbor graph** in *present-day reference material coordinates*. Each parcel finds its six nearest seeded crust samples by great-circle distance. All undirected edges are unique, ID-ordered and deterministic across rebuilds, and graph adjacency cannot be mutated by callers. Edges belong to material, **not** fixed geographical regions, plate centres, shader tiles or an invented continent polygon. The graph gives an explicit topology proxy needed to discover contacts but **does not** constitute an area-complete crust tessellation. Neighbors can separate over geological epochs and are then ineligible for contact strain; true re-bonding, splitting and remeshing are future work.

`PlanetCrustContactStrainSolver.Evaluate` creates an **isolated, immutable kinematic experiment** at one requested geological epoch and a bounded duration of at most one million years. It reads `PlanetCrustOwnershipHistory` and the *existing* `PlanetTectonicBoundaryKinematics`; only pairs from distinct owning plates matching an active, non-neutral canonical boundary contact can interact. Every active pair records compression, shear, contact influence and a rate-normalized strain contribution. Contacts that drift too far from their reference material neighbors cannot stretch across the planet to manufacture uplift. The model scales each contribution by the local number of neighbors and clamps the total signed logarithmic area strain per sample to ±0.12 per step.

Positive convergence gives **negative log-area strain (footprint shortening)** and a reciprocal thickening of the reference column. Divergence causes extension and thinning. Pure lateral shear is reported as a separate displacement exposure; it never creates imaginary vertical relief. Every material sample retains its original identity, composition, density and **exact reference mass**, with thickness derived from `mass / (area × density)`. Both per-parcel and aggregate reference-mass invariants are tested; no material is created or deleted. A zero-duration step must leave every reference column unchanged. Synthetic tests also verify compression, extension and shear signs independently of the generated world seed.

**Important physical limits:** this is *not* a full finite-volume or stress/force solver. Global surface area coverage and traction balance are **not conserved** by independently strained reference columns; no strain is yet accumulated into real solid-earth material meshes or terrain heights. Historical epochs retain the previous backward-reconstruction convention; a single evaluated step is **not a chronological forward integration**. No sediment transport, melting, subduction reservoir, crust recycling, hydrology or volcanism is claimed. Both the normal gameplay planet and the separate draft Hero work continue unmodified. Rendering a mountain from this incomplete contract would be premature.

**Next release gate:** replace the reference-point proxy with region-contiguous material neighborhoods and a balanced finite-volume area/volume ledger, implement continuous conservative tectonic deformation across neighboring geological regions, then publish the resulting heightfield to the **single planet-wide source**. Verify actual orbital mountain belts and zoomed LOD 0–3 landforms on multiple seeds; visual changes, not newly coloured diagnostics, justify the first manual iPhone test.

## A2.5.6 — Conservative material exchange and global thickness field (PF 0.0.37.25)

A2.5.5 produced bounded per-material column strain but isolated every material sample, leaving no inter-parcel exchange or connected deformation field. A2.5.6 introduces `PlanetCrustMassExchangeSolver`, which accepts one immutable 0.0.37.24 `PlanetCrustInteractionSnapshot` and its matching persistent material-neighbor graph. Every currently connected edge can transport a **bounded fraction of mass** from relatively compressed (shortened/thickened) samples towards less-compressed neighbors. The transport coefficient (0.10 per log-strain unit), per-edge cap and aggregate 1% per-donor cap are explicit **research assumptions**, not calibrated lithosphere rheology. Critically, all edge proposals are computed from the same source snapshot, and each donor is budgeted once across all its outgoing edges, so ordering cannot permit overdraw. Detached neighbors are not reconnected by fiat. No exchange occurs for a zero-duration or zero-strain step.

Each exchange is a source-ID → destination-ID transaction of **existing mass**. The `PlanetCrustDeformationSnapshot` includes immutable edge transaction records plus local retained, imported, exported and final column masses; transported solid volume preserves the **source material's density**, so an oceanic crust sample arriving inside a continental neighbor does not silently change its identity to continental material. A receiver's column thickness is computed from retained local solid volume plus incoming provenance-specific volume divided by its strained footprint area. No material is created, annihilated or covertly transmuted; the edge ledger debits exactly what it credits. Tests verify per-edge ledger matching, total source/destination exchange equality, aggregate mass closure, outgoing-budget bounds, deterministic replay, multiple seeds and source-density preservation.

`PlanetCrustColumnThicknessField` provides an immutable, geographically addressed **crust-thickness anomaly** sample with normalized smooth compact spherical kernels and a zero-anomaly background. Identical directions yield identical diagnostics regardless of imagined region/LOD evaluation order and there are no hard tile edges. It is a bridge towards consistently sampled physical fields, but is **not** connected to the gameplay height provider and is NOT mountain uplift. Lithospheric column thickening cannot simply be interpreted as topographic rise: the future solver needs isostatic compensation, material buoyancy, mantle/subduction reservoirs and genuinely area-complete spatial geometry.

**Current boundary:** this step conserves material *within its sampled graph ledger*, not the volume/area coverage of a continuous planetary crust. The 1,024 representative samples still lack watertight finite-volume polygons; neither arbitrary spherical kernels nor the 1% transport coefficient establish real continuum mechanics. No accumulating forward-time simulation, global solid-Earth equilibrium, volcano, snowpack, lakes, rivers or ocean basin evolution is claimed. Hero draft #113 remains isolated and the orbital/LOD 0–3 planet visuals stay unchanged.

**Next:** introduce conservative spherical cell areas/edge faces with remeshing/contact ownership, explicitly separate crust and mantle reservoirs, derive balanced uplift/subsidence from material displacement and buoyancy, and integrate it through the existing shared planetary terrain provider. Visual multi-seed whole-globe and zoomed LOD 0–3 checks—and then an iPhone test—become the required acceptance gates only when visible physical landforms actually change.

## A2.5.7 — Airy isostatic crust compensation (PF 0.0.37.26)

We now explicitly separate **solid lithosphere thickness**, **free-surface vertical displacement** and **mantle root change**. `PlanetCrustAiryIsostasySolver` operates on the conserved-material A2.5.6 deformation snapshot, never on a colour/shader terrain trick or an unrelated land generator. For each sample, it evaluates its final total solid volume, areal crust load and relative reference thickness. The local Airy model balances load at a common compensation depth: `ΔcolumnMassPerArea - ρ_mantle * Δroot - ρ_cover * Δsurface = 0` and `ΔsolidThickness = Δroot + Δsurface`. This yields `Δsurface = (ρ_mantle * ΔsolidThickness - ΔcolumnMassPerArea) / (ρ_mantle - ρ_cover)`. Air/continental samples use a zero-density cover, existing oceanic samples use seawater (1,030 kg/m³); mantle density defaults to 3,300 kg/m³ and must exceed local reference crust density. All values are explicitly calibrated research assumptions rather than solved fluid or mantle dynamics.

Compression can produce local potential **uplift with a deepening crustal root**, spreading can produce **subsidence and a reduced root**, and an ocean-cover load changes the balance compared to air. Actual incoming material remains density-aware because the earlier mass/solid-volume ledger retains donor density; a transfer does not magically transform oceanic crust into continental crust. `PlanetCrustIsostasySnapshot` exposes finite, immutable per-material displacement/root/load/pressure-residual diagnostics together with the unchanged global material-mass budget. Tests verify signed uplift and subsidence, pressure closure, cover-density contrast, multiple seeds, zero-duration identity, deterministic reconstruction, invalid densities, and source-material ownership preservation.

`PlanetCrustIsostaticReliefField` blends the solved *potential vertical displacement* in globally addressed, compact smooth spherical kernels; there are no per-tile coordinate resets. `PlanetCrustIsostaticResearchElevationSource` is an **explicit test adapter**, not registered in the application and not published into `PlanetTerrainRegionProvider`: it composes only for an exactly matching generated world and leaves every other seed's terrain unchanged. The current implementation scans 1,024 material samples per diagnostic query and is **not production-costed for dense whole-globe meshes or iPhone LOD**.

**Important limits:** Airy compensation assumes vertical hydrostatic local equilibrium, fixed overburden density and a deep mantle reservoir, but neither solves isostatic flexure, plate rheology, density-dependent sea-level crossings, actual continuum cell-area conservation, sediment transfer nor finite-volume mantle displacement. It is a bounded research diagnostic of the A2.5.6 one-step state, NOT an accumulated forward geological time integration. The fixed reference neighbor graph is still not a globally watertight plate tessellation. Smooth interpolation does not prove continuity of conservative solid-earth flux. No automatic mountain/volcano production, moving coastline or gameplay planet appearance change is claimed.

**Next release gate:** create region-contiguous spherical control volumes/face ownership, conservative mantle reservoir and distributed flexural load sharing, then a spatially indexed publication path through the *same orbital and local LOD elevation source*. Confirm multi-seed orbital mountain-belt shapes, regional rifts and zoomed LOD 0–3 agreement before asking for the first manual iPhone review; keep draft Hero research #113 isolated.

## A2.5.8 — Full-sphere control volumes and shared-face fluxes (PF 0.0.37.27)

Previous milestones provided persistent crust identities, their kinematic ownership history, contact-column strain, locally conserved mass transfer and a research Airy isostatic uplift/subsidence response. Their **equal-weight 1,024 material point samples had no contiguous geometric boundaries**, so they could not be interpreted as a finite-volume crust tessellation. A2.5.8 addresses that **reference-geometry gap** without asserting that an evolving plate simulation has been solved.

`PlanetCrustSphericalControlVolumes.Build(catalog)` partitions the **present-day reference sphere** into convex spherical Voronoi polygons using great-circle halfspace clipping around the existing globally distributed material seeds. Every cell has positive, physically measured geographic surface area and an immutable ordered polygon boundary. The sum of all geographic cell areas must equal `4πR²` within strict numerical tolerance. Internal great-circle faces are keyed by the canonical pair of persistent material IDs and must occur **exactly twice**, with matching physical edge endpoints and lengths—once for each adjacent cell. A half-open local Hero tile, quadtree LOD seam or longitude discontinuity **cannot invent a separate copy of an edge**. The enclosing initial clipping geometry never serves as a reported material boundary unless it is also geometrically paired and validated. Multiple seeded planets and rebuilds are verified.

The immutable spherical polygon's **actual geographic area is intentionally different** from each pre-existing parcel's equal quadrature reference area. We do not silently substitute it into the existing 0.0.37.22–0.0.37.26 material-mass/thickness equations: that would change crust mass without an explicit rebasing/mapping conservation proof. `Locate(direction)` uses a balanced 3-D nearest-reference-site spatial index, avoiding a per-query O(1,024) scan in the reference geometry model. Sampling arbitrary geographic positions, including poles, longitude wrap and differing LOD address conventions, returns consistent reference-cell material IDs.

`PlanetCrustSphericalFaceFluxLedger` accepts explicit **signed mass transfers** on canonical shared faces. One flux has exactly one debit and one credit, with globally validated donor budgets across *all* incident faces, deterministic ordering and no edges to nonexistent/nonadjacent cells. Tests verify numerical global mass closure, aggregate donor caps, direct shared-face validation, cross-world rejection and exact replay. This is a conservative **face-addressed ledger**, not yet a time-integrated flux constitutive law or automatically coupled to the older nearest-neighbor exchange solver.

**Important limitations:** Voronoi polygons are a valid full-sphere **present-day geographic control-volume partition**, not deforming material polygons that keep exact area during geological history. Their boundaries do not yet move under tectonics, and they have not been proven to track fault tearing, remeshing, subduction or crust creation. The 0.0.37.26 Airy isostasy field remains **unpublished research**; one-step thickening cannot be mistaken for validated mountain belts. No new shaders, hero files, world save migrations, rendering changes or unmeasured iPhone performance claims are introduced.

**Next acceptance gate:** advect/remesh conservative spherical faces through plate history, rebalance geometric volume vs material quadrature, attach signed flux to physically generated boundary actions, accumulate buoyancy/restoring forces over epochs, and only then publish a spatially bounded elevation snapshot through the shared canonical provider for meaningful whole-globe and LOD 0–3 visual tests.

## A2.5.9 — Geological-epoch moving-site spherical topology (PF 0.0.37.28)

A2.5.8 established a closed **present-day** spherical Voronoi reference partition. A2.5.9 extends that exact geometry solver with `BuildAtEpoch(catalog, history, epoch)`, where each geographic site comes from the real persistent parcel's `PlanetCrustOwnershipHistory.GetState(id, epoch).GeographicDirection`. **Plate ownership changes must be explicit, replayable history events**, not inferred from the nearest moving plate centre each render. A moving-site reconstruction has the same permanent (world, material parcel ID) key and exactly one cell per material, but may have new geographic areas, face lengths, neighboring material IDs and positions. The builder rejects indistinguishable moving sites and halfspace degeneracy instead of discarding a material sample.

At each requested epoch, the whole 1,024-cell globe is rebuilt and validated together: strictly positive spherical polygon areas, `4πR²` total geographic area, canonical shared faces appearing **exactly twice** with matching endpoints/length, and indexed geographic lookup against epoch-specific sites. The original `Build(catalog)` remains the unchanged present-day reference path. There is no half-published partial remesh or ad hoc per-LOD neighbor network.

`PlanetCrustEpochTopologyTransition.Compare` diffs two valid epoch-specific partitions in increasing geological years-before-present. It records each stable material ID's previous/new owning plate, sample displacement, geographic area, reference mass and mass per **geographic** square metre. It also records each canonical ID-pair face as **persisted, appeared or disappeared** and its previous/new length. Comparisons require identical generated worlds, monotonically ordered epochs and an **exact ownership-history provenance match** for every cell site; a geometry built from an alternate history cannot masquerade as compatible. The material's original reference mass and composition do **not** change because a Voronoi edge appears or moves; total geographic sphere area and global reference material mass are separate invariants.

The preexisting `PlanetCrustSphericalFaceFluxLedger` is also epoch-safe now: it **rejects** deformation and face geometry from different geological epochs, even when their world identity matches. This prevents transferring mass across an edge that ceased to exist in the actual geometry used for the state.

**Limits:** moving-site spherical Voronoi cells are **Eulerian geographic neighborhoods**, not true moving **Lagrangian crust polygons**. Reassigning geographic area to a material site does NOT demonstrate volume-preserving advection or legitimate mantle exchange; the new density-per-geographic-area is a diagnostic quotient, not conserved crust thickness. A changed Voronoi neighbor is a **topological reconstruction**, not evidence of a physical fault split/subduction/creation of new crust. This milestone does not claim solved mantle flux, re-meshing interpolation of real solid material, accumulated tectonic forces, volcanoes, landform evolution or a visible planet change. No modification to hero lab, normal canonical elevations, save files or LOD 0–3 rendering. Additional remeshing/transfer conservation and physically balanced elevation snapshots remain essential gates before the first human iPhone visual test.

**Next:** implement explicit conservative state remapping when geographic faces/owners change, distinguish rigid material advection from actual strain and mantle material transfer, and add a time-directed geological evolution ledger with reproducible per-epoch world snapshots. Only after that should relief be published atomically through the existing shared planet elevation source and visually tested at orbital and LOD 0–3 scales.

## A2.5.10 — Conservative geographic regridding between tectonic epochs (PF 0.0.37.29)

A2.5.9 reconstructs **moving-site** spherical Voronoi cells but does not say how a scalar geographic field should survive when old and new cells change area, connectivity and faces. This release introduces `PlanetCrustEpochConservativeRemapper` as an explicitly **Eulerian geographic regridding** component. For each old cell, it intersects the spherical great-circle polygon with candidate new cells (bounded by site-centred spherical caps). It calculates spherical surface **overlap area** rather than assuming a nearest-centre assignment. Every old cell's complete overlap is verified against its physical geographic area and every new cell's accumulated overlap against its area; absent coverage or incompatible geometry throws rather than silently inserting material. The guard permits at most `1e-4` relative numerical overlap-area discrepancy and reports the worst observed discrepancy. Equal partitions produce an exact identity mapping.

The remapper accepts an **explicit passive tracer mass per geographic cell**, assumed uniformly distributed *within the old geographic polygon*. It moves that tracer into each new cell proportionally to actual shared spherical surface area, renormalizes only within the numerically validated source cell and assigns the final transfer the rounding remainder. Thus every old cell's mass is debited once across its overlap recipients, every new cell receives only actual transfers, and **global passive-tracer mass closes** on every step. Immutable `PlanetCrustGeographicRemapSnapshot` includes original mass, final per-cell tracer mass and density per geographic area, each directed source-cell/destination-cell transfer with overlap area and its mass, world identity, ordered geological epochs, and worst geometric coverage error. Each remap requires the same exact generated world, chronological reconstruction order and **matching persistent ownership history** enforced by `PlanetCrustEpochTopologyTransition.Compare`. Multi-seed coverage, a spatially uniform tracer, localized tracer, two-step epoch replay and rejection of incompatible histories are tested.

**Critically, the geographic tracer is not lithospheric or mantle material.** This operation does *not* change persistent parcel IDs, reference crust mass, composition, crust age, ownership or the earlier source-density material ledger; instead it provides the correct conservation contract for **Eulerian values sampled at moving geographic cells**. A Voronoi face appearing/disappearing is still a geometric regridding event, not a physical oceanic-crust creation event. Because the current geological time model reconstructs **backward** from present toward older years-before-present, sequential regridding of a tracer into older geography is not automatically a physical forward-time evolution simulator. This research operation is independent of the normal canonical planet elevation provider, Hero #113 and world save format. It does not alter the orbital globe or LOD 0–3 terrain.

**Next physical gate:** introduce explicit dual-reservoir mass accounting for *crust and mantle*, a time-oriented solver (older→younger) that links face flux to measured plate interactions and subduction/rifting rules, and a conservative remap of stored scalar/vector fields that preserves their material provenance. Integrate accumulated pressure-balanced deformation into a globally indexed immutable elevation snapshot only when mountain-chain, rift and tectonic shoreline behaviors are physically traceable and cross-LOD/seed benchmarks pass. Then perform the first iPhone landscape review.

## A2.5.11 — Forward-time crust ↔ accessible-mantle reservoir ledger (PF 0.0.37.30)

The earlier geological epochs (A2.5.5–10) reconstruct moving material positions and nearest-site spherical Voronoi partitions **backward** from the present: `PlanetGeologicalEpoch.YearsBeforePresent` increases while reconstructing further into the past. Their geographic overlap remapper conserves a *passive Eulerian tracer* and does not move material between the real lithosphere and mantle. A2.5.11 introduces a separate, **forward-time** reservoir-accounting experiment: transitions must advance from an **older** source epoch to an equal or **younger** target epoch, and each physical step is at most 1 million years. Historical geometry and plate ownership remain fixed/read-only inputs, never reinterpreted as an earlier physical evolution solver.

`PlanetCrustMantleConservationSolver.CreateInitial(catalog, history, olderGeometry)` creates an immutable **two-reservoir budget per persistent material parcel**: an original crust mass equal to the catalogued reference mass and an explicit *finite exchangeable upper-mantle budget* initially **4× crust reference mass** for this numerical experiment. This artificial multiplier is a modeling bound and **not the mass of Earth's or the simulated planet's entire mantle**. The snapshot retains original world and per-material IDs, composition and provenance, original total material mass and a reference epoch. All runtime material remains separate from the old immutable reference catalogue and any geographic passive tracer.

The `SuggestExchanges` method derives optional deterministic, signed transfer candidates from real shared **target-epoch Voronoi cell faces**. Each candidate requires opposite owning plates, a matching active canonical moving-centre boundary classification at that face midpoint, and pre-existing oceanic crust material. Convergence/oblique convergence proposes **crust → accessible mantle (subduction budget)**; divergence/oblique divergence proposes **accessible mantle → crust (spreading budget)**. Magnitudes are based on signed relative plate speed, target face length, a reference column thickness and a clearly uncalibrated contact influence/scale, bounded to **1% of the source reservoir per step**. Transform-only or neutral contacts do not create material. These are proposals, not automatic time evolution or evidence of calibrated magma/rheological rules.

`Advance` records only explicitly accepted signed events; it checks exact world/material identity, compatible epoch-specific geometry and owner history, canonical existing cross-plate shared faces and correctly signed kinematics. It rejects invalid, duplicated, empty/nonfinite events, events that overdraw either reservoir, incompatible geometry/history and time reversal. All proposals are applied atomically against **one previous immutable state** with aggregate source budgets before the next snapshot is published. Each local event increases one reservoir by the exact amount subtracted from the other: **crust + accessible mantle mass remains constant per parcel and globally**. Every resulting snapshot retains immutable chronological event evidence and unchanged permanent reference material/plate identities. A zero-duration step is allowed only without transfers. Multi-seed, multi-epoch, replay, invalid-event and canonical-elevation regression tests document those invariants.

**Limits:** The oceanic-only constraint, 4× finite exchangeable mantle budget, 1% cap and effective interaction coefficient are **research assumptions**. Subducted material does not yet follow slab descent or transform into a typed mantle parcel; spreading does not yet allocate newly created material identities or model magma/temperature/density evolution. Although mass is conserved within this ledger, its local partition does not prove conservation of momentum, energy, full mantle volume or global crust surface area. A signed boundary classification is kinematic evidence, not a physically derived stress/viscosity/erosion law. No automatic lateral material transport, ocean depth, mountain chain, volcanic arc, hydrological drainage or actual rendered relief is claimed. The globe, Hero draft, persisted world format and LOD 0–3 elevations remain untouched.

**Next:** connect these mass transfer budgets to evolving conservative crust-volume/cell-face quantities and mantle-root pressure support; introduce explicit composition/provenance of newly formed and recycled material; accumulate strain/thermal age/deformation over physically directed epochs; use a spatial index and a single terrain elevation source to publish coherent mountain belts/rifts only after whole-globe and local LOD 0–3 correctness/performance tests pass.

## A2.5.12 — Forward accumulated geology + first opt-in visual preview (PF 0.0.37.31)

A2.5.11 introduced a strictly audited **two-reservoir** crust ↔ exchangeable-mantle mass ledger with forward chronological steps (older geological ages → younger). Earlier A2.5.5 computed **bounded instantaneous** reference-column strain from actual contact motion, and A2.5.7 showed a separate **local Airy** pressure-compensation response. Those results did not accumulate into a single timeline. `PlanetCrustAccumulatedDeformation.Simulate` now performs a small, deterministic series of up to **six** 1 Myr forward steps for the canonical persistent material sample catalogue. It reconstructs complete moving epoch geometry, asks the finite crust–mantle model for optional contact-based exchange proposals, explicitly accepts those under budget/velocity checks, and accumulates bounded signed **logarithmic material-reference-footprint strain** and shear exposure from `PlanetCrustContactStrainSolver` for each persistent material ID.

Each immutable resulting `PlanetCrustEvolvingSnapshot` contains one coherent epoch, all parcels' actual reconstructed plate owners and geographic positions, current crust and exchangeable mantle mass, cumulative strain and shear, calculated lithospheric thickness, and **pressure-balanced local Airy surface displacement and mantle root**. Unlike naive visual height stacking, the solver recomputes local isostasy from the **full accumulated current column state** at each epoch; it does not add the same uplift repeatedly. Original material identity, composition, finite per-column reference budget and total crust+mantle mass are never changed by the visualizer. Deterministic multi-seed, zero-step, two-step replay, pressure closure and unmodified canonical elevation are independently tested.

In response to the need for a visual quality gate, an **opt-in geological deformation preview** is available at `?geologyPreview=1` directly from the live planet HUD (`GEOLOGY`) or Terrain Lab. It uses exactly the **C#-computed** accumulated physical columns and draws two **global, equirectangular and continuous** views: signed *potential Airy uplift/subsidence in actual metres*, and *crust thickness change from the same conserved material state*. A selectable geological snapshot (2 Ma, 1 Ma, present) and deterministic seed allow direct physical evolution inspection. The browser's canvas module implements only compact spherical-kernel interpolation and a per-view diverging colour scale; **all physics is computed in the domain/infrastructure C# pipeline**, and displayed numerical maxima remain unexaggerated metres. Status and measured legend explicitly label this as a prototype. The maps do not use fake continent shapes, independent tectonic terrain masks, invented mountains or offset the gameplay height mesh. The original globe, archive persistence, water and all LOD 0–3 rendering remain untouched. Playwright tests validate that the physical maps actually render from nonzero data, that the initial epoch is undeformed, that normal planet navigation remains intact, and that the preview fits an iPhone-width viewport.

**Interpretation gate:** the visual check is for **plausibility of the accumulated spatial signal**, not sign-off on production terrain or a claim of tectonically accurate continent collision. The 1,024 reference samples are representative quadrature points, not a fully advected solid crust mesh; independently accumulated reference-area strains do not conserve global continuum area/volume. Each step's crust ↔ mantle transfer still relies on simplified contact classification, and Airy compensation omits flexure, viscoelasticity, heat, compositional differentiation and physical subduction/slab descent. Smooth kernel-based preview interpolation does not establish coherent 3D orbital relief or actual 0–3 LOD elevation. Real mountain belts, rift valleys, volcanic arcs, ice and rivers remain future stages.

**Next acceptance milestones:** review first preview captures across at least two seeds and the 0/1/2 Ma snapshots; quantify whether signals cluster appropriately around real moving contacts or are too point-like; replace O(1,024) kernel sampling with indexed field delivery; reconcile accumulated deformation with physically conservative moving material area/cell face fluxes; and only publish actual relief through the existing single canonical `IPlanetElevationSource` after passing whole globe and LOD0–3 visual consistency/performance gates. Use iPhone review of the isolated diagnostic preview now, while protecting the normal game.

## A2.5.13 — Physics-derived relief on the actual main planet (PF 0.0.37.32)

First user visual inspection of 0.0.37.31 showed a promising but still spotty/crust-sample-shaped uplift/subsidence signal and an undesirable cramped **separate preview page**. Therefore **the main globe is now the sole primary visual acceptance surface**. The earlier `?geologyPreview=1`, Terrain Lab and Geological Archive remain directly addressable for historical diagnostic tests but are removed as navigation links from the regular planet HUD; future user-visible geology decisions should happen in the actual shared terrain, not yet another tab.

`PlanetCrustGlobalReliefAtlas` converts the immutable, pressure-balanced, two-million-year **present-epoch** `PlanetCrustEvolvingSnapshot` into one contiguous, indexed worldwide geographic elevation-delta atlas (256 longitude samples × 129 latitude samples; poles longitude-independent, longitude wrapped). The baked atlas interpolates the existing *measured physical surface displacement in metres*, using the bounded compact spherical kernels already established in the diagnostic preview. The atlas is immutable, cannot include incomplete/nonfinite parcels, and evaluates every subsequent geographic direction in constant time via continuous bilinear sampling. Its grid is computed once when requested, not by a 1,024-parcel scan at every LOD vertex. This is an experimental, smoothed **model signal** and does not yet prove solid-earth continuum material-area conservation.

`PlanetTerrainRegionProvider.PublishGlobalCrustDeformation` first constructs that complete atlas, then **atomically publishes** a seed-isolated `IPlanetElevationSource` composing it with the existing canonical `ProceduralPlanetElevationSource`. Only physically grounded sampled elevation changes; no colour overlay, duplicate continent generator, fake road-shaped mountains, special shader displacement or LOD-specific coordinate mapping is introduced. The existing revision-aware `PlanetSurfaceMeshCache`, `PlanetSurfaceTileSampler`, `PlanetLocalSurfacePatchSampler` and globe renderer all sample this same elevation path. Publishing/clearing advances the terrain revision so the existing GPU rock and coastline meshes cannot silently reuse stale geometry; previously captured snapshots remain immutable. The world seed, hydrology save format and reference material identity are unchanged.

The main HUD now contains **one on-planet GEOLOGY OFF/ON button** in place of the Lab/Geology/Archives links. Clicking ON explicitly reconstructs two million years of research geology for the current seed, publishes the single global terrain revision, and invalidates/rebuilds the main globe's real 3D geometry. Clicking OFF restores the original canonical planet for an unambiguous on/off comparison. The button keeps working across orbital zoom and local ground LOD 0–3; generating a different planet resets the active experiment. Simulation remains on-demand to avoid extra work on startup on mobile devices. The status and button disclose research nature and actual physically measured local column maxima; the existing GPU's documented visualization relief exaggeration is unchanged (this release adds no new exaggeration).

Tests now demand: (1) numerically changed **IPlanetElevationSource** physical elevations and seed isolation, not merely a shader colour; (2) global longitude and both poles geometrically continuous; (3) identical sampling at the same cube-sphere physical vertex at LOD 0, 1, 2 and 3, and at the local ground patch; (4) actual mesh cache invalidation both when publishing and clearing; (5) browser-visible, revision-verified on/off switching on the main WebGL planet with mobile-safe controls; (6) earlier geology diagnostics still runnable by direct URL.

**What is not yet solved:** the terrain is still the existing procedural canonical baseline plus bounded, globally interpolated **research crust response**; the old procedural mountain priors have not yet been replaced by physically accumulated belts. The source is based on 1,024 representative samples, so detached circular local signals may remain. Two million years is short for substantial tectonic construction of an Earth-scale orogen, and the measured 300–500 m signals may be visually modest at orbit scale. This is intentionally a first **real 3D geometry bridge** for quality measurement, not completion of tectonic evolution, ocean-basin formation, active faults, lava, glacial carving or water-flow evolution.

**Next:** assess screenshots of the actual main globe and zoomed-in terrain with GEOLOGY ON/OFF, quantify plate-contact coherence and residual circular artefacts, then improve globally conservative spatial deformation at the source rather than adding extra visual modes. When scientifically warranted, gradually replace the placeholder canonical mountain priors with coherent time-integrated collision/rift uplift. Preserve one physical globe, correct LOD 0–3 and touch/mobile performance.


## A2.5b — Main-planet crust research epoch comparison (opt-in)

The existing `GEOLOGY ON` main-planet control now retains the same
`PlanetCrustAccumulatedDeformation` 2-million-year *research* timeline
instead of only displaying the final present-day result. Compact mobile
controls select 2 Ma, 1 Ma and 0 Ma **discrete physical-crust snapshots**.
Each selection publishes the unchanged solver's real Airy-compensated
metre displacement through `PlanetTerrainRegionProvider`, invalidating
both orbit and local LOD geometry on the same generated world and seed.
The canonical reference terrain is still restored with `GEOLOGY OFF`;
no saved world, history archive or erosion research is modified.

The provider's ordinary `PublishGlobalCrustDeformation` still accepts
only present-day research snapshots. A separately named
`PublishGlobalCrustResearchEpoch` explicitly allows earlier snapshots
for this *comparison*, without pretending those snapshots are full coupled
climate, hydrology or Earth-origin geological epochs.

**Do not confuse this with an evolving playable Earth.** Climate/water in
the current playable Frozen Climate Experiment does not travel back in time
when a user selects a crust research frame. The labels state
`CRUST RESEARCH ONLY`. The mantle/mass-exchange research integrates
1024 representative columns; interpolated heights are not fully conservative
moving polygonal crust and not yet accepted mountain-range realism.
There is no invented tectonic-animation interpolation between snapshots.

Acceptance covers C# seeded height identity at orbit/local LOD,
revision-safe switches, and an iPhone-sized WebGL browser comparison with
screenshots of 1 Ma and 2 Ma physical geometry. Scientific/geometric
calibration, planet-wide process coupling and real Safari evaluation remain
open before the research result is promoted to gameplay.


## A2.5c — Mobile-first six-million-year geological research comparison

The main planet's existing opt-in crust research timeline now uses **all six
already bounded** annual-million-year steps supported by
`PlanetCrustAccumulatedDeformation.MaximumResearchSteps`. Seven immutable
snapshots correspond to **6 → 5 → 4 → 3 → 2 → 1 → 0 Ma**, sharing one seed,
ownership history, material-budget invariants and the actual terrain provider
for orbital and local LOD 0–3. Do not scale the resulting surface relief for
the research comparison. The change expands the duration sampled by the
existing pressure-balanced solver; it does not claim conservative moving
spherical material polygons or complete dynamic mountains.

When the comparison is active, the iPhone HUD hides unrelated Snowball-year,
seed and planet-generation controls while keeping the geology toggle, physical
epoch navigation and actual displacement visible. The unrelated
Snowball NEXT action is unavailable during this research comparison, so
climate-stage progression cannot be confused with geological time travel.
Turning geology off restores the unchanged climate/generation interface. This is **not** a separate
tab/planet or a separate simulation clock. All histories remain research-only:
water, climate and Earth-origin physics are not rewound by these buttons.

iPhone visual approval is still required: verify short HUD, LOD 0–3
continuity, stable orbit/navigation, changed physical relief without a
sudden artifact, and acceptable compute time when first enabling the longer
research history. CI checks mass and pressure closure across all seven
snapshots and records smartphone-sized screenshots at 1, 2 and 6 Ma.


## A2.5d — Correct acceptance rendering of active local terrain (PF 0.0.38.3)

Safari captures of PF 0.0.38.2 show soft/repetitive terrain at very
close range. A narrowly attempted extra near-orbit LOD7 refinement in
unmerged #175 passed tests, but Playwright's screenshot remained nearly
uniform, **not evidence of improved actual terrain**; the PR was closed
without merge. A black screenshot at local altitude revealed a separate,
previously hidden regression in the *visual test instrumentation*: its
`measure()` function always called the orbital WebGL renderer, even
after `setPlanet` had entered local mesh mode and emptied active orbital
tiles. Thus black local captures could be generated by the test itself,
with `glError == 0` falsely interpreted as success.

This milestone centralizes selection of the **actual current view's**
orbit or local GPU rendering for the animation frame and test API
(`setYaw`, `setPitch`, `setDistance`, `measure`), without changing
terrain geology, the active shader algorithms, gameplay camera bounds,
material conservation, or tile budgets. A real iPhone-sized Playwright
test now asserts that a local snapshot contains **nontransparent rendered
surface pixels** as well as no WebGL errors, captures that state, and
verifies successful orbital return. Numeric pass alone is explicitly
insufficient: acceptance depends on the correct active framebuffer.

Neither a high LOD number nor a nonblack frame proves geological
realism. Real continent advection, evidence-based mountain morphology,
and canonical orbit/local scientific LOD alignment remain open in #162.


## A2.5e — Bounded local physical mesh sampling near the ground (PF 0.0.38.4)

The actual iPhone Safari PF 0.0.38.2 screenshots show broad, soft
interpolation near the coast and uplands. The canonical elevation source
already contains deterministic, shared bedrock relief at 2–32 km scales.
The local mesh previously sampled this same source on just **16 × 16**
vertices/cells for the 3–20 km-altitude bands, creating large triangles
that could not resolve narrower physically computed ridges and valleys.

For the **existing local surface only**, increase the coarse/medium
mesh to **24 × 24 cells** and detailed mesh to **32 × 32 cells**;
the finest existing mesh stays **32 × 32**. Maximum resulting geometry
remains **2048 triangles** per local patch, with no new noise fields,
artificial elevation amplification, independent terrain system or
changes to the 56-tile orbital mesh budget.

Tests verify bounded triangle counts, sub-kilometre physical spacing
near 3 km altitude and actual visible WebGL pixels on an iPhone-sized
viewport at 3 km and 100 m, with screenshots and seeded elevation
range. These are renderer fidelity gates, **not proof that the bedrock
physics is sufficiently realistic**. The original Airy displacement,
tectonic parcel provenance, global masses and water ledger remain
unchanged. Evaluate iPhone Safari CPU/interaction cost and visible
difference before accepting a larger mesh; revert if worse.
