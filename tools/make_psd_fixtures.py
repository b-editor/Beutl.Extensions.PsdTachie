"""Builds layered PSD fixtures with psd-tools and renders their expected composites.

Usage: python -I make_psd_fixtures.py <output-dir>
"""
import sys
from pathlib import Path

from PIL import Image
from psd_tools import PSDImage
from psd_tools.api.layers import Group, PixelLayer
from psd_tools.constants import BlendMode, Compression

out = Path(sys.argv[1])
out.mkdir(parents=True, exist_ok=True)

W, H = 64, 48


def solid(w, h, rgba):
    return Image.new("RGBA", (w, h), rgba)


def build(compression):
    psd = PSDImage.new("RGB", (W, H), color=0, depth=8)

    def pixel(name, img, left, top, visible=True):
        layer = PixelLayer.frompil(img, psd, name, top, left, compression)
        layer.visible = visible
        return layer

    background = pixel("背景", solid(W, H, (200, 200, 200, 255)), 0, 0)
    psd.append(background)

    body = pixel("胴体", solid(20, 20, (220, 40, 40, 255)), 10, 20)
    shadow = pixel("影", solid(30, 10, (0, 0, 255, 128)), 5, 30)
    body_group = Group.group_layers(parent=psd, layers=[body, shadow], name="体")
    shadow.clipping = True
    shadow.blend_mode = BlendMode.MULTIPLY

    eye_open = pixel("*開き", solid(8, 4, (0, 160, 0, 255)), 40, 4)
    eye_half = pixel("*半目", solid(8, 2, (0, 100, 0, 255)), 40, 5, visible=False)
    eye_closed = pixel("*閉じ", solid(8, 1, (0, 60, 0, 255)), 40, 6, visible=False)
    Group.group_layers(parent=psd, layers=[eye_open, eye_half, eye_closed], name="目")

    mouth_closed = pixel("*閉じ", solid(6, 1, (90, 0, 0, 255)), 41, 14)
    mouth_half = pixel("*半開き", solid(6, 3, (140, 0, 0, 255)), 41, 13, visible=False)
    mouth_open = pixel("*開き", solid(6, 5, (190, 0, 0, 255)), 41, 12, visible=False)
    Group.group_layers(parent=psd, layers=[mouth_closed, mouth_half, mouth_open], name="口")

    outline = pixel("!輪郭", solid(2, 40, (0, 0, 0, 255)), 0, 4, visible=False)
    psd.append(outline)

    hidden = pixel("非表示", solid(10, 10, (255, 255, 0, 255)), 50, 30, visible=False)
    psd.append(hidden)

    half = pixel("半透明", solid(12, 12, (255, 255, 255, 255)), 48, 34)
    half.opacity = 128
    psd.append(half)
    return psd


def build_depth(depth):
    psd = PSDImage.new("RGB", (32, 24), color=0, depth=depth)
    psd.append(PixelLayer.frompil(Image.new("RGBA", (32, 24), (200, 200, 200, 255)), psd, "bg", 0, 0, Compression.RLE))
    red = PixelLayer.frompil(Image.new("RGBA", (10, 10), (220, 40, 40, 255)), psd, "red", 5, 5,
                             Compression.ZIP_WITH_PREDICTION)
    psd.append(red)
    Group.group_layers(parent=psd, layers=[red], name="grp")
    return psd


for depth in (16, 32):
    path = out / f"depth{depth}.psd"
    build_depth(depth).save(path, encoding="shift_jis")
    PSDImage.open(path).composite(force=True).convert("RGBA").save(out / f"depth{depth}_expected.png")

for name, compression in [("rle", Compression.RLE), ("raw", Compression.RAW), ("zip", Compression.ZIP)]:
    psd = build(compression)
    path = out / f"tachie_{name}.psd"
    psd.save(path, encoding="shift_jis")
    reread = PSDImage.open(path)
    reread.composite(force=True).convert("RGBA").save(out / f"tachie_{name}_expected.png")
    print(path, [l.name for l in reread.descendants()])
