# earth-bake

Offline pipeline that turns published plate reconstructions into compact assets for the browser
(Earth roadmap milestone E0, see `docs/milestones/earth-history-roadmap.md`).

## Setup
```powershell
py -3.12 -m pip install pygplates numpy pillow netCDF4 scipy   # or the full path to python.exe
```
Download data into the git-ignored `.cache/` folder:
```powershell
curl.exe -sSL -o .cache/cao.zip https://www.earthbyte.org/webdav/ftp/Data_Collections/Cao_etal_2023/1.8Ga_model_submit.zip
Expand-Archive .cache/cao.zip .cache/cao
# PaleoDEMs (1°, netCDF, 0–540 Ma) and the PALEOMAP plate model used to present-reference them
curl.exe -sSL -o .cache/paleodem1.zip "https://zenodo.org/records/5460860/files/Scotese-Wright2018_PALEOMAP_PaleoDEMs_nc.zip?download=1"
Expand-Archive .cache/paleodem1.zip .cache/paleodem1
foreach ($n in 'Rotations','StaticPolygons','Coastlines') {
  curl.exe -sSL -o ".cache/paleomap_$n.zip" "https://repo.gplates.org/webdav/pmm/paleomap/$n.zip"
  Expand-Archive ".cache/paleomap_$n.zip" ".cache/paleomap_$n" }
```
(Check the Zenodo record for the exact file name if the link changes.)

## Scripts
- `preview.py [age ...]` — reconstruct continents at the given ages (Ma) to `.cache/preview/*.png`.
- `bake.py` — full bake (~5–8 min) into `src/PlanetForge.Presentation.Web/wwwroot/data/earth/`:
  `manifest.json` (grid, plate ids, frame offsets, sources), `rotations.bin` (gzip, per 5 Myr frame
  × plate quaternion, present→age, in renderer axes), `frames.bin` (per-frame gzip; Uint16 planes:
  topology plate index+1, continental block index+1, 1024×512) and `present.bin` (ETOPO 2022 6′
  elevation, Int16 m, 2048×1024). Requires `.cache/etopo_6min.txt`. Runs a rotation self-test
  (worst error must stay < 0.1°) and writes debug renders to `.cache/bake-debug/`.
- `paleodem.py` — (~2–4 min) present-references the PaleoDEMs: rasterises the PALEOMAP static polygons
  at 512×256, rotates each present-day texel to its palaeo-position per age and bilinear-samples the DEM.
  Crust that did not exist yet is −3000 m; deep ocean is flattened to −3000 m; quantised to 20 m.
  Writes `paleo.bin` (gzip Int16 [ages][256][512], ascending ages) and `paleo.json` (width, height,
  ages, nodataMetres, source) plus `.cache/bake-debug/paleo-present-ref-*.png`.

Grid convention matches the renderer: row 0 = south, u = 0.5 − lon/360 (east–west mirrored), pyGPlates
(x, y, z) → (x, z, −y).

## Sources (cite in the app)
- Cao, X. et al. (2024) 1.8 Ga full-plate model (EarthByte `Cao_etal_2023`).
- Merdith, A. S. et al. (2021) *Earth-Science Reviews* 214, 103477 — 1 Ga full-plate model.
- Scotese, C. R. & Wright, N. (2018) PALEOMAP PaleoDEMs, Zenodo 5460860, CC-BY-4.0.
- NOAA ETOPO 2022 (public domain).
