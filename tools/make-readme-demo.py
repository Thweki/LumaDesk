"""Compose captured WPF frames; run after the UI harness's --demo mode.

Requires Pillow. Does not synthesize app controls or alter screen settings.
"""
from pathlib import Path
import shutil
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "docs/images"

for language, suffix in [("zh", ""), ("en", "-en")]:
    source = ROOT / f"artifacts/readme-{language}"
    frames, durations = [], []
    timeline = [row.split("\t") for row in (source / "frames.tsv").read_text(encoding="utf-8-sig").splitlines()]
    # Keep the display map and brightness thumb in the same place as the panel changes modes.
    height = max(int(bottom) for _, _, _, bottom in timeline) + 3
    for name, duration, _, _ in timeline:
        with Image.open(source / name) as image:
            panel = image.convert("RGBA").crop((0, 0, image.width, height))
        frame = Image.new("RGB", panel.size, panel.getpixel((panel.width // 2, 4))[:3])
        frame.paste(panel, mask=panel.getchannel("A"))
        frames.append(frame.quantize(colors=128, method=Image.Quantize.MEDIANCUT))
        durations.append(int(duration))
    target = DEST / f"quick-demo{suffix}.gif"
    frames[0].save(target, save_all=True, append_images=frames[1:], duration=durations, loop=0, optimize=True, disposal=2)
    for name in ("quick-panel", "quick-panel-dark", "settings"):
        shutil.copyfile(source / f"{name}.png", DEST / f"{name}{suffix}.png")
    with Image.open(target) as check:
        assert check.n_frames > 40 and check.info["loop"] == 0
    print(f"{target.name}: {target.stat().st_size:,} bytes, {len(frames)} captured frames")
