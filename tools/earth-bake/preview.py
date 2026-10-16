"""Preview bake: reconstruct continental polygons at key ages and rasterise
them to equirectangular PNGs, to verify the pyGPlates pipeline (E0).

Usage: python preview.py [age ...]   (Ma, default: key supercontinent ages)
Data:  Cao et al. (2024) 1.8 Ga full-plate model, extracted to .cache/cao.
"""
import sys
from pathlib import Path

import pygplates
from PIL import Image, ImageDraw

ROOT = Path(__file__).parent
MODEL = ROOT / ".cache" / "cao" / "1.8Ga_model_submit"
OUT = ROOT / ".cache" / "preview"
WIDTH, HEIGHT = 1024, 512


def rotation_model():
    return pygplates.RotationModel([
        str(MODEL / "Rotation_file_1800_1000_no_ncc_new_SaoFrancisco.rot"),
        str(MODEL / "1000_0_rotfile_Merdith_et_al_Cao_new_ncc.rot"),
    ])


def to_pixel(lat, lon):
    return ((lon + 180.0) / 360.0 * WIDTH, (90.0 - lat) / 180.0 * HEIGHT)


def render(age, rotations, continents, coasts):
    image = Image.new("RGB", (WIDTH, HEIGHT), (18, 40, 82))
    draw = ImageDraw.Draw(image)
    wrapper = pygplates.DateLineWrapper()
    for features, colour in ((continents, (120, 112, 96)), (coasts, (176, 160, 120))):
        reconstructed = []
        pygplates.reconstruct(features, rotations, reconstructed, float(age))
        for item in reconstructed:
            geometry = item.get_reconstructed_geometry()
            if not isinstance(geometry, pygplates.PolygonOnSphere):
                continue
            for wrapped in wrapper.wrap(geometry):
                points = [to_pixel(p.get_latitude(), p.get_longitude()) for p in wrapped.get_exterior_points()]
                if len(points) > 2:
                    draw.polygon(points, fill=colour)
    for lat in range(-60, 61, 30):
        y = to_pixel(lat, 0)[1]
        draw.line([(0, y), (WIDTH, y)], fill=(40, 60, 100))
    draw.text((8, 8), f"{age} Ma  (Cao et al. 2024 / Merdith et al. 2021)", fill=(255, 255, 255))
    return image


def main():
    ages = [int(a) for a in sys.argv[1:]] or [0, 200, 250, 500, 750, 1000, 1300, 1600, 1800]
    OUT.mkdir(parents=True, exist_ok=True)
    rotations = rotation_model()
    continents = pygplates.FeatureCollection(str(MODEL / "shapes_continents_Cao.gpmlz"))
    coasts = pygplates.FeatureCollection(str(MODEL / "shapes_coasts_Cao.gpmlz"))
    for age in ages:
        path = OUT / f"continents_{age:04d}Ma.png"
        render(age, rotations, continents, coasts).save(path)
        print(path)


if __name__ == "__main__":
    main()
