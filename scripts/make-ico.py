"""Build assets/AppIcon.ico from the original macOS app icon in the upstream submodule.

Usage: python -I scripts/make-ico.py
"""
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "upstream" / "assets" / "AppIcon.icns"
OUT = ROOT / "assets" / "AppIcon.ico"
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]

icon = Image.open(SOURCE).convert("RGBA")  # opens at the largest size (1024)
icon.save(OUT, sizes=[(s, s) for s in SIZES])
icon.resize((256, 256), Image.LANCZOS).save(OUT.with_suffix(".png"))
print(f"wrote {OUT.relative_to(ROOT)} and AppIcon.png")
