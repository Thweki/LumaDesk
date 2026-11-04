"""Compose captured WPF frames; run after the UI harness's --demo mode.

Requires Pillow. Does not synthesize app controls or alter screen settings.
"""
from pathlib import Path
import shutil
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "docs/images"

for language, suffix in [("zh", ""), ("en", "-en")]:
    source = ROOT / f"artifacts/readme-{language}"
    font_path = "C:/Windows/Fonts/msyh.ttc" if language == "zh" else "C:/Windows/Fonts/segoeui.ttf"
    font = lambda size: ImageFont.truetype(font_path, size)
    captions = {
        "select": ("01", "点选屏幕", "按桌面位置，找到那块屏幕。"),
        "more": ("02", "更多设置", "常用调节在眼前，\n其他选项按需展开。"),
        "link": ("03", "一起调亮度", "勾选想调的屏幕。\n其余屏幕保持原样。"),
        "adjust": ("04", "拖动就好", "一次调整，多屏同步。"),
        "done": ("05", "回到单屏", "点一下“完成”，\n继续调整当前屏幕。"),
        "theme": ("06", "深浅皆宜", "随系统切换，\n也可以自己选。"),
    } if language == "zh" else {
        "select": ("01", "Pick a screen", "Find it by its position\non your desk."),
        "more": ("02", "More controls", "Keep the basics close.\nExpand when you need more."),
        "link": ("03", "Link brightness", "Select the screens\nyou want to adjust."),
        "adjust": ("04", "Just drag", "One adjustment.\nSeveral screens."),
        "done": ("05", "Back to one", "Choose Done to return\nto single-screen controls."),
        "theme": ("06", "Light or dark", "Follow your system,\nor choose your own."),
    }
    frames, durations = [], []
    for row in (source / "frames.tsv").read_text(encoding="utf-8-sig").splitlines():
        name, duration, stage = row.split("\t")
        frame = Image.new("RGB", (760, 740), "#eaf0f2")
        draw = ImageDraw.Draw(frame)
        draw.text((30, 32), "LumaDesk", font=font(25), fill="#16383d")
        number, title, detail = captions[stage]
        draw.text((30, 224), number, font=font(46), fill="#007e8b")
        draw.text((30, 294), title, font=font(27), fill="#182d34")
        draw.multiline_text((30, 346), detail, font=font(17), spacing=8, fill="#52646b")
        panel = Image.open(source / name).convert("RGBA")
        # Rounded clipping reproduces the window outline omitted by RenderTargetBitmap.
        mask = Image.new("L", panel.size, 0)
        ImageDraw.Draw(mask).rounded_rectangle((0, 0, panel.width-1, panel.height-1), radius=12, fill=255)
        frame.paste(panel, (368, 60), mask)
        footer = "实际界面 · 示例屏幕 · 模拟亮度写入" if language == "zh" else "Actual UI / sample screens / simulated brightness writes"
        draw.text((30, 698), footer, font=font(14), fill="#65777e")
        frames.append(frame.quantize(colors=128, method=Image.Quantize.MEDIANCUT))
        durations.append(int(duration))
    target = DEST / f"quick-demo{suffix}.gif"
    frames[0].save(target, save_all=True, append_images=frames[1:], duration=durations, loop=0, optimize=True, disposal=2)
    for name in ("quick-panel", "quick-panel-dark", "settings"):
        shutil.copyfile(source / f"{name}.png", DEST / f"{name}{suffix}.png")
    with Image.open(target) as check:
        assert check.n_frames > 40 and check.info["loop"] == 0
    print(f"{target.name}: {target.stat().st_size:,} bytes, {len(frames)} captured frames")
