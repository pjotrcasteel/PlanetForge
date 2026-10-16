"""Bake PALEOMAP PaleoDEMs into present-day-referenced palaeo-relief (milestone E3).

Each PaleoDEM (Scotese & Wright 2018, 1 deg, 540-0 Ma) is drawn in its own
palaeogeographic frame. Using the PALEOMAP plate model that produced it, every
present-day location is rotated to its palaeo-position and the DEM is sampled
there. The result says "how high was the crust that is here today, at age A",
which the browser carries with the Cao/Merdith kinematics. This keeps the
palaeo-relief tied to the crust even where the two reconstructions disagree
on where that crust was.

Outputs (to wwwroot/data/earth/):
  paleo.bin   gzip Int16 [N][H][W] metres, row 0 = south, columns in the
              renderer convention (u = 0.5 - lon/360), 20 m steps. Deep ocean
              (< -1 km) and crust that did not exist yet are -3000 m.
  paleo.json  { width, height, ages[N], source }

Run: python paleodem.py   (needs .cache/paleodem1 and .cache/paleomap_*)
"""
import gzip
import json
import re
from pathlib import Path

import netCDF4
import numpy as np
import pygplates
from PIL import Image, ImageDraw

ROOT = Path(__file__).parent
CACHE = ROOT / ".cache"
OUT = ROOT.parent.parent / "src" / "PlanetForge.Presentation.Web" / "wwwroot" / "data" / "earth"
W, H = 512, 256
NODATA_M = -3000


def grid_lat_lon():
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    lon = -(u - 0.5) * 360.0          # renderer columns are east-west mirrored
    lat = (v - 0.5) * 180.0           # row 0 = south
    return np.meshgrid(lat, lon, indexing="ij")


def rasterise(polygons):
    """Polygon index + 1 per texel, in the renderer convention."""
    image = Image.new("I", (W, H), 0)
    draw = ImageDraw.Draw(image)
    wrapper = pygplates.DateLineWrapper()
    for value, polygon in polygons:
        for wrapped in wrapper.wrap(polygon):
            points = [((p.get_longitude() + 180.0) / 360.0 * W, (90.0 - p.get_latitude()) / 180.0 * H)
                      for p in wrapped.get_exterior_points()]
            if len(points) > 2:
                draw.polygon(points, fill=int(value))
    return np.fliplr(np.flipud(np.asarray(image, dtype=np.int32)))


def rotation_matrix(rotation):
    lat, lon, angle = rotation.get_lat_lon_euler_pole_and_angle_degrees()
    la, lo, a = np.radians(lat), np.radians(lon), np.radians(angle)
    k = np.array([np.cos(la) * np.cos(lo), np.cos(la) * np.sin(lo), np.sin(la)])
    K = np.array([[0, -k[2], k[1]], [k[2], 0, -k[0]], [-k[1], k[0], 0]])
    return np.eye(3) + np.sin(a) * K + (1 - np.cos(a)) * K @ K


def sample_dem(z, lat, lon):
    """Bilinear sample of a 1-degree [-90..90] x [-180..180] grid."""
    y = np.clip(lat + 90.0, 0, 180)
    x = np.mod(lon + 180.0, 360.0)
    y0 = np.minimum(np.floor(y).astype(int), 179)
    x0 = np.minimum(np.floor(x).astype(int), 359)
    fy, fx = y - y0, x - x0
    return ((z[y0, x0] * (1 - fx) + z[y0, x0 + 1] * fx) * (1 - fy)
            + (z[y0 + 1, x0] * (1 - fx) + z[y0 + 1, x0 + 1] * fx) * fy)


def main():
    files = sorted((CACHE / "paleodem1").rglob("*.nc"),
                   key=lambda f: float(re.search(r"_([\d.]+)Ma\.nc$", f.name).group(1)))
    ages = [float(re.search(r"_([\d.]+)Ma\.nc$", f.name).group(1)) for f in files]
    rotation_model = pygplates.RotationModel(str(CACHE / "paleomap_Rotations" / "Rotations" / "PALEOMAP_PlateModel.rot"))
    features = list(pygplates.FeatureCollection(
        str(CACHE / "paleomap_StaticPolygons" / "StaticPolygons" / "PALEOMAP_PlatePolygons.gpml")))
    polygons, plate_ids, begin = [], [], []
    for feature in features:
        geometry = feature.get_geometry()
        if not isinstance(geometry, pygplates.PolygonOnSphere):
            continue
        polygons.append((len(plate_ids) + 1, geometry))
        plate_ids.append(feature.get_reconstruction_plate_id())
        b = feature.get_valid_time()[0]
        begin.append(1e9 if pygplates.GeoTimeInstant(b).is_distant_past() else float(b))
    owner = rasterise(polygons) - 1          # -1 = not covered
    plate_ids, begin = np.array(plate_ids), np.array(begin)
    lat, lon = grid_lat_lon()
    la, lo = np.radians(lat), np.radians(lon)
    xyz = np.stack([np.cos(la) * np.cos(lo), np.cos(la) * np.sin(lo), np.sin(la)], axis=-1)
    texel_plate = np.where(owner >= 0, plate_ids[np.maximum(owner, 0)], -1)
    texel_begin = np.where(owner >= 0, begin[np.maximum(owner, 0)], -1.0)
    print(f"{len(files)} DEMs, {len(polygons)} polygons, coverage {np.mean(owner >= 0):.3f}")

    out = np.full((len(files), H, W), NODATA_M, dtype=np.int16)
    for n, (path, age) in enumerate(zip(files, ages)):
        with netCDF4.Dataset(path) as d:
            z = np.asarray(d.variables["z"][:], dtype=np.float32)
        frame = np.full((H, W), float(NODATA_M), dtype=np.float32)
        for pid in np.unique(texel_plate[texel_plate >= 0]):
            mask = (texel_plate == pid) & (texel_begin >= age)
            if not mask.any():
                continue
            m = rotation_matrix(rotation_model.get_rotation(age, int(pid)))
            p = xyz[mask] @ m.T
            frame[mask] = sample_dem(z, np.degrees(np.arcsin(np.clip(p[:, 2], -1, 1))),
                                     np.degrees(np.arctan2(p[:, 1], p[:, 0])))
        # Deep ocean is modelled by the GPU physics; 20 m steps keep the
        # asset small.
        frame = np.where(frame < -1000.0, NODATA_M, np.round(frame / 20.0) * 20.0)
        out[n] = np.clip(frame, -11000, 9000).astype(np.int16)
        if age in (0, 50, 100, 200, 250, 300, 400, 540):
            debug = CACHE / "bake-debug"
            debug.mkdir(exist_ok=True)
            e = out[n].astype(np.float32)
            rgb = np.where(e[..., None] > 0,
                           np.stack([90 + e / 30, 140 + e / 60, 60 + 0 * e], -1),
                           np.stack([20 + 0 * e, 60 + e / 100, 140 + e / 60], -1))
            Image.fromarray(np.flipud(np.clip(rgb, 0, 255).astype(np.uint8))).save(debug / f"paleo-present-ref-{int(age)}.png")
        print(f"  {age:6.1f} Ma land {np.mean(out[n] > 0):.3f}")

    blob = gzip.compress(out.tobytes(), compresslevel=9)
    (OUT / "paleo.bin").write_bytes(blob)
    (OUT / "paleo.json").write_text(json.dumps({
        "width": W, "height": H, "ages": ages, "nodataMetres": NODATA_M,
        "source": "Scotese, C. R. & Wright, N. (2018) PALEOMAP PaleoDEMs, Zenodo 5460860, CC-BY-4.0; "
                  "present-referenced with the PALEOMAP plate model (Scotese 2016).",
    }, indent=1))
    print(f"paleo.bin {len(blob) / 1e6:.2f} MB")


if __name__ == "__main__":
    main()
