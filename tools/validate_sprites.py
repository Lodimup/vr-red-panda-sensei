# /// script
# requires-python = ">=3.11"
# dependencies = ["pillow>=10"]
# ///
"""Validate object images against PRD section 7.

RULING(Handler 2026-10-27): objects are realistic high-resolution images shown
as flat 2D cardboard cutouts, not 32x32 pixel art.

Each catalog object has two files in assets/sprites/objects/:
  <name>.png       the object alone on a transparent background
  <name>_card.png  the same cutout as a die-cut cardboard standee
Both are 1024x1024 RGBA PNGs.
"""
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent / "assets" / "sprites" / "objects"
NAMES = {
    "water", "tea", "cup_blue", "cup_red", "apple", "banana", "rice_ball",
    "book_green", "book_yellow", "pen", "bag", "box", "key", "umbrella",
}
SIZE = (1024, 1024)


def load(path: Path, errors: list[str]) -> Image.Image | None:
    img = Image.open(path)
    if img.mode != "RGBA":
        errors.append(f"{path.name}: mode {img.mode}, expected RGBA")
        img = img.convert("RGBA")
    if img.size != SIZE:
        errors.append(f"{path.name}: size {img.size}, expected {SIZE}")
        return None
    return img


def margin_clear(img: Image.Image, margin: int) -> bool:
    a = img.getchannel("A")
    w, h = img.size
    for box in ((0, 0, w, margin), (0, h - margin, w, h), (0, 0, margin, h), (w - margin, 0, w, h)):
        if a.crop(box).getextrema()[1] != 0:
            return False
    return True


def check(name: str) -> list[str]:
    errors: list[str] = []
    cut_path, card_path = ROOT / f"{name}.png", ROOT / f"{name}_card.png"
    if not cut_path.exists():
        return [f"{name}.png: missing"]
    if not card_path.exists():
        errors.append(f"{name}_card.png: missing")
    cut = load(cut_path, errors)
    if cut is None:
        return errors
    if not margin_clear(cut, 48):
        errors.append(f"{name}.png: pixels within 48 px of the edge must be fully transparent")
    solid = cut.getchannel("A").point(lambda v: 255 if v >= 128 else 0)
    bbox = solid.getbbox()
    if bbox is None:
        return errors + [f"{name}.png: no opaque pixels"]
    span = max(bbox[2] - bbox[0], bbox[3] - bbox[1])
    if span < 700:
        errors.append(f"{name}.png: object spans {span} px; must span at least 700 px")
    colours = len(set(cut.convert("RGB").crop(bbox).resize((256, 256)).getdata()))
    if colours < 2000:
        errors.append(f"{name}.png: only {colours} distinct colours in a 256x256 sample; expected a realistic image (>= 2000)")
    if card_path.exists():
        card = load(card_path, errors)
        if card is not None:
            if not margin_clear(card, 16):
                errors.append(f"{name}_card.png: pixels within 16 px of the edge must be fully transparent")
            card_solid = card.getchannel("A").point(lambda v: 255 if v >= 128 else 0)
            cut_px = solid.histogram()[255]
            card_px = card_solid.histogram()[255]
            missing = Image.composite(Image.new("L", SIZE, 255), Image.new("L", SIZE, 0), solid)
            missing = Image.composite(Image.new("L", SIZE, 0), missing, card_solid).histogram()[255]
            if missing > cut_px // 200:
                errors.append(f"{name}_card.png: does not cover the cutout ({missing} cutout pixels are transparent on the card)")
            if card_px - cut_px < 10000:
                errors.append(f"{name}_card.png: card border too thin ({card_px - cut_px} border pixels, need >= 10000)")
    return errors


def main() -> int:
    errors: list[str] = []
    present = set()
    for p in sorted(ROOT.glob("*.png")):
        stem = p.stem[:-5] if p.stem.endswith("_card") else p.stem
        if stem not in NAMES:
            errors.append(f"{p.name}: unknown name (allowed: {', '.join(sorted(NAMES))}, each with an optional _card)")
        else:
            present.add(stem)
    for name in sorted(present):
        errors += check(name)
    for e in errors:
        print(f"ERROR {e}")
    print(f"checked {len(present)} objects, {len(errors)} errors")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
