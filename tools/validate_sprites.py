# /// script
# requires-python = ">=3.11"
# dependencies = ["pillow>=10"]
# ///
"""Validate object sprites against PRD section 7.

Every object sprite is a 32x32 RGBA PNG under assets/sprites/objects/, named
<catalog_id>.png or <catalog_id>_<variant>.png, drawn only in the PICO-8
palette, with fully opaque or fully transparent pixels and a 1 px transparent
margin. Each sprite has a 512x512 preview (16x nearest-neighbour) under
assets/sprites/objects/preview/ with the same name.
"""
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent / "assets" / "sprites" / "objects"
PALETTE = {
    (0x00, 0x00, 0x00), (0x1D, 0x2B, 0x53), (0x7E, 0x25, 0x53), (0x00, 0x87, 0x51),
    (0xAB, 0x52, 0x36), (0x5F, 0x57, 0x4F), (0xC2, 0xC3, 0xC7), (0xFF, 0xF1, 0xE8),
    (0xFF, 0x00, 0x4D), (0xFF, 0xA3, 0x00), (0xFF, 0xEC, 0x27), (0x00, 0xE4, 0x36),
    (0x29, 0xAD, 0xFF), (0x83, 0x76, 0x9C), (0xFF, 0x77, 0xA8), (0xFF, 0xCC, 0xAA),
}
ALLOWED = {
    "water", "tea", "cup_blue", "cup_red", "apple", "banana", "rice_ball",
    "book_green", "book_yellow", "pen", "bag", "box", "key", "umbrella",
}


def check(path: Path) -> list[str]:
    errors = []
    name = path.stem
    if name not in ALLOWED:
        return [f"{path.name}: unknown sprite name (allowed: {', '.join(sorted(ALLOWED))})"]
    img = Image.open(path)
    if img.mode != "RGBA":
        errors.append(f"{path.name}: mode {img.mode}, expected RGBA")
        img = img.convert("RGBA")
    if img.size != (32, 32):
        return errors + [f"{path.name}: size {img.size}, expected (32, 32)"]
    opaque = []
    for y in range(32):
        for x in range(32):
            r, g, b, a = img.getpixel((x, y))
            if a not in (0, 255):
                errors.append(f"{path.name}: pixel ({x},{y}) alpha {a}, expected 0 or 255")
            elif a == 255:
                opaque.append((x, y))
                if (r, g, b) not in PALETTE:
                    errors.append(f"{path.name}: pixel ({x},{y}) colour #{r:02X}{g:02X}{b:02X} not in PICO-8 palette")
    if not opaque:
        return errors + [f"{path.name}: no opaque pixels"]
    xs = [p[0] for p in opaque]
    ys = [p[1] for p in opaque]
    if min(xs) < 1 or min(ys) < 1 or max(xs) > 30 or max(ys) > 30:
        errors.append(f"{path.name}: opaque pixels must stay inside x,y 1..30 (1 px transparent margin)")
    if max(max(xs) - min(xs), max(ys) - min(ys)) + 1 < 20:
        errors.append(f"{path.name}: object spans less than 20 px; draw it larger")
    preview = ROOT / "preview" / path.name
    if not preview.exists():
        errors.append(f"{path.name}: missing preview/{path.name}")
    else:
        p = Image.open(preview).convert("RGBA")
        if p.size != (512, 512):
            errors.append(f"preview/{path.name}: size {p.size}, expected (512, 512)")
        elif p.resize((32, 32), Image.NEAREST).tobytes() != img.convert("RGBA").tobytes():
            errors.append(f"preview/{path.name}: is not a 16x nearest-neighbour upscale of the sprite")
    return errors


def main() -> int:
    sprites = sorted(ROOT.glob("*.png"))
    errors = [e for s in sprites for e in check(s)]
    for p in sorted((ROOT / "preview").glob("*.png")) if (ROOT / "preview").exists() else []:
        if not (ROOT / p.name).exists():
            errors.append(f"preview/{p.name}: preview without a sprite")
    for e in errors:
        print(f"ERROR {e}")
    print(f"checked {len(sprites)} sprites, {len(errors)} errors")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
