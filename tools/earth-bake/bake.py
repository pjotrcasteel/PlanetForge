"""Bake the reconstructed Earth history into browser assets (milestone E0).

Outputs (to src/PlanetForge.Presentation.Web/wwwroot/data/earth/):
  manifest.json   frame ages, plate ids, raster sizes, frame offsets, sources
  rotations.bin   Float32 [frame][plate][4] quaternions (x, y, z, w) rotating a
                  present-day position to its position at the frame age, in
                  the renderer frame (x = cos(lat) cos(lon), y = sin(lat),
                  z = cos(lat) sin(lon)).
  frames.bin      concatenated gzip blobs, one per frame, each Uint16
                  [2][H][W]: layer 0 = topological plate index + 1 (gaps
                  filled), layer 1 = continental material plate index + 1
                  (0 = no continental crust). Row 0 is the south pole row.
  present.bin     gzip Int16 [PH][PW] ETOPO 2022 surface elevation (m),
                  row 0 = south.

Sources: Cao et al. (2024) 1.8 Ga full-plate model (incl. Merdith et al. 2021);
NOAA ETOPO 2022 (public domain). Run: python bake.py
"""
import gzip
import json
import math
import sys
from pathlib import Path

import numpy as np
import pygplates
from PIL import Image, ImageDraw

ROOT = Path(__file__).parent
CACHE = ROOT / ".cache"
MODEL = CACHE / "cao" / "1.8Ga_model_submit"
OUT = ROOT.parent.parent / "src" / "PlanetForge.Presentation.Web" / "wwwroot" / "data" / "earth"
W, H = 1024, 512
PW, PH = 2048, 1024
STEP_MYR = 5
OLDEST_MA = 1800

ROTATION_FILES = ["Rotation_file_1800_1000_no_ncc_new_SaoFrancisco.rot", "1000_0_rotfile_Merdith_et_al_Cao_new_ncc.rot"]
TOPOLOGY_FILES = [
    "1800-1000Ma-plate-boundary_new_valid_time_and_subduction_polarity.gpml",
    "1000-410-Topologies_Merdith_et_al_Cao.gpml", "1000-410-Convergence_Merdith_et_al_Cao.gpml",
    "1000-410-Divergence_Merdith_et_al_Cao.gpml", "1000-410-Transforms_Merdith_et_al_Cao.gpml",
    "410-250_plate_boundaries_Merdith_et_al.gpml", "250-0_plate_boundaries_Merdith_et_al.gpml",
    "TopologyBuildingBlocks_Merdith_et_al.gpml",
]
CONTINENT_FILE = "COBfile_1800_0.gpml"


def direction(lat, lon):
    # Renderer frame: longitude increases toward -z (its equirectangular
    # maps run east-west mirrored relative to pyGPlates), y is north.
    la, lo = math.radians(lat), math.radians(lon)
    return np.array([math.cos(la) * math.cos(lo), math.sin(la), -math.cos(la) * math.sin(lo)])


def quaternion(rotation):
    lat, lon, angle = rotation.get_lat_lon_euler_pole_and_angle_degrees()
    # (x, y, z)_pygplates -> (x, z, -y) is a proper rotation: the sense of
    # rotation is preserved.
    half = math.radians(angle) * 0.5
    axis = direction(lat, lon)
    return np.array([*(axis * math.sin(half)), math.cos(half)])


def rotate(q, v):
    u, w = q[:3], q[3]
    return v + 2.0 * np.cross(u, np.cross(u, v) + w * v)


def rasterise(polygons, wrapper):
    image = Image.new("I", (W, H), 0)
    draw = ImageDraw.Draw(image)
    for value, polygon in polygons:
        for wrapped in wrapper.wrap(polygon):
            points = [((p.get_longitude() + 180.0) / 360.0 * W, (90.0 - p.get_latitude()) / 180.0 * H)
                      for p in wrapped.get_exterior_points()]
            if len(points) > 2:
                draw.polygon(points, fill=int(value))
    # Row 0 = south; columns run with the renderer's longitude (u = 0.5 - lon/360).
    return np.fliplr(np.flipud(np.asarray(image, dtype=np.int32)))


def close_gaps(raster):
    """Fill pinholes and slivers between adjacent continental polygons."""
    out = raster.copy()
    for _ in range(2):
        shifts = [np.roll(np.roll(out, dy, axis=0), dx, axis=1) for dy in (-1, 0, 1) for dx in (-1, 0, 1) if dx or dy]
        stack = np.stack(shifts)
        filled = (stack > 0).sum(axis=0)
        candidate = (out == 0) & (filled >= 5)
        # Majority label among the neighbours (max is a fine proxy: slivers
        # border at most two blocks).
        out[candidate] = stack.max(axis=0)[candidate]
    return out


def fill_gaps(raster):
    """Nearest-neighbour fill of uncovered texels (slivers between topologies)."""
    out = raster.copy()
    for _ in range(64):
        empty = out == 0
        if not empty.any():
            break
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            shifted = np.roll(out, dx, axis=1)
            if dy:
                shifted = np.roll(out, dy, axis=0)
                shifted[0 if dy > 0 else -1, :] = 0
            take = empty & (shifted > 0)
            out[take] = shifted[take]
            empty = out == 0
    return out


def present_elevation():
    text = (CACHE / "etopo_6min.txt").read_text()
    body = text.split("z.z[1080][2160]", 1)[1].split("z.lat", 1)[0]
    rows = []
    for line in body.strip().splitlines():
        if not line.startswith("["):
            continue
        rows.append(np.array(line.split(",")[1:], dtype=np.float32))
    grid = np.stack(rows)  # row 0 = south (lat ascending), lon from -180
    assert grid.shape == (1080, 2160), grid.shape
    image = Image.fromarray(np.fliplr(grid), mode="F").resize((PW, PH), Image.BILINEAR)
    return np.clip(np.asarray(image), -11000, 9000).astype(np.int16)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    rotations = pygplates.RotationModel([str(MODEL / f) for f in ROTATION_FILES])
    topologies = [pygplates.FeatureCollection(str(MODEL / f)) for f in TOPOLOGY_FILES]
    continents = pygplates.FeatureCollection(str(MODEL / CONTINENT_FILE))
    wrapper = pygplates.DateLineWrapper()
    ages = list(range(0, OLDEST_MA + 1, STEP_MYR))

    plate_index = {}

    def index_of(pid):
        if pid not in plate_index:
            plate_index[pid] = len(plate_index)
        return plate_index[pid]

    blobs, offsets, stats = [], [], []
    offset = 0
    for age in ages:
        resolved = []
        pygplates.resolve_topologies(topologies, rotations, resolved, float(age))
        topo = [(index_of(r.get_feature().get_reconstruction_plate_id()) + 1, r.get_resolved_boundary()) for r in resolved]
        reconstructed = []
        pygplates.reconstruct(continents, rotations, reconstructed, float(age))
        crust = [(index_of(r.get_feature().get_reconstruction_plate_id()) + 1, r.get_reconstructed_geometry())
                 for r in reconstructed if isinstance(r.get_reconstructed_geometry(), pygplates.PolygonOnSphere)]
        layer0 = fill_gaps(rasterise(topo, wrapper))
        layer1 = close_gaps(rasterise(crust, wrapper))
        data = np.stack([layer0, layer1]).astype(np.uint16)
        blob = gzip.compress(data.tobytes(), 9)
        blobs.append(blob)
        offsets.append([offset, len(blob)])
        offset += len(blob)
        stats.append((age, len(topo), float((layer1 > 0).mean())))
        if age % 100 == 0:
            print(f"{age:5d} Ma  topologies {len(topo):3d}  continental texels {100 * (layer1 > 0).mean():5.1f}%  blob {len(blob) // 1024} KB", flush=True)

    plates = sorted(plate_index.items(), key=lambda kv: kv[1])
    quats = np.zeros((len(ages), len(plates), 4), dtype=np.float32)
    for f, age in enumerate(ages):
        for pid, i in plates:
            q = quaternion(rotations.get_rotation(float(age), pid))
            if f > 0 and np.dot(q, quats[f - 1, i]) < 0:
                q = -q
            quats[f, i] = q

    # Self-test: the quaternion frame must reproduce pyGPlates.
    worst = 0.0
    for pid, i in plates[:: max(1, len(plates) // 40)]:
        for f in (36, 100, 300):
            p = pygplates.PointOnSphere(23.0, 47.0)
            moved = rotations.get_rotation(float(ages[f]), pid) * p
            lat, lon = moved.to_lat_lon()
            mine = rotate(quats[f, i].astype(np.float64), direction(23.0, 47.0))
            worst = max(worst, math.degrees(math.acos(min(1.0, float(np.dot(mine, direction(lat, lon)))))))
    print(f"rotation self-test: worst error {worst:.4f} deg")
    if worst > 0.1:
        sys.exit("rotation convention mismatch")

    (OUT / "frames.bin").write_bytes(b"".join(blobs))
    (OUT / "rotations.bin").write_bytes(gzip.compress(quats.tobytes(), 9))
    (OUT / "present.bin").write_bytes(gzip.compress(present_elevation().tobytes(), 9))
    manifest = {
        "version": 1,
        "width": W, "height": H,
        "presentWidth": PW, "presentHeight": PH,
        "stepMyr": STEP_MYR,
        "oldestMa": OLDEST_MA,
        "frameCount": len(ages),
        "plateIds": [pid for pid, _ in plates],
        "frames": offsets,
        "continentalFraction": [round(s[2], 4) for s in stats],
        "sources": [
            "Cao, X. et al. (2024) 1.8 Ga full-plate tectonic model (EarthByte Cao_etal_2023)",
            "Merdith, A. S. et al. (2021) A continuous, kinematic full-plate motion model from 1 Ga to present. Earth-Science Reviews 214, 103477",
            "NOAA NCEI ETOPO 2022 15 arc-second global relief model (public domain), 6' subset",
        ],
    }
    (OUT / "manifest.json").write_text(json.dumps(manifest, indent=1))
    total = sum(p.stat().st_size for p in OUT.iterdir())
    print(f"plates {len(plates)}  frames {len(ages)}  total {total / 1e6:.1f} MB -> {OUT}")


if __name__ == "__main__":
    main()
