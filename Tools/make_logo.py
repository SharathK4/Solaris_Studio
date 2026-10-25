"""Builds the SOLARIS wordmark as a signed-distance-field texture.

Italiana (OFL) provides the base letterforms. The A is rebuilt by hand: its crossbar is
dropped and a hollow teardrop swash grows out of the L's foot, as in the brand sheet.
Output: Assets/Solaris/Art/Logo/solaris_wordmark_sdf.png (R = distance, 0.5 = edge).

    python Tools/make_logo.py [--preview]
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FONT = os.path.join(ROOT, "Assets", "Solaris", "Art", "Fonts", "Italiana-Regular.ttf")
OUT = os.path.join(ROOT, "Assets", "Solaris", "Art", "Logo", "solaris_wordmark_sdf.png")

SIZE = 1000            # font px at master resolution
PAD = 160              # room for the distance field spread
SPREAD = 48            # distance (master px) that maps to the full 0..1 range
DOWNSAMPLE = 2         # master -> texture


def glyph_mask(ch, font):
    bb = font.getbbox(ch)
    w, h = bb[2] - bb[0], bb[3] - bb[1]
    im = Image.new("L", (w + 4, h + 4), 0)
    ImageDraw.Draw(im).text((-bb[0] + 2, -bb[1] + 2), ch, font=font, fill=255)
    return np.array(im) > 127


def paste(canvas, mask, x, y):
    h, w = mask.shape
    canvas[y:y + h, x:x + w] |= mask


def teardrop(tip, belly_x, bottom, width, n=900, bulge=1.25):
    """Slanted drop: sharp point at `tip`, round belly centred on `belly_x` resting on `bottom`."""
    tx, ty = tip
    h = bottom - ty
    pts = []
    for i in range(n):
        t = 2 * math.pi * i / n
        u = (1 - math.cos(t)) / 2                       # 0 at the tip, 1 at the belly
        x = 0.5 * width * math.sin(t) * math.sin(t / 2) ** bulge
        cx = tx + (belly_x - tx) * u                    # lean the drop like an A leg
        pts.append((cx + x, ty + h * u))
    return pts


def main():
    font = ImageFont.truetype(FONT, SIZE)
    masks = {c: glyph_mask(c, font) for c in "SOLRI"}
    cap = masks["L"].shape[0]

    # Horizontal-only dilation: fattens stems, keeps hairlines hairline (Didone contrast).
    def contrast(m, px=7):
        return ndimage.binary_dilation(m, structure=np.ones((1, px), bool))

    for c in masks:
        masks[c] = contrast(masks[c])

    track = -18
    a_w = int(cap * 0.60)
    widths = [masks["S"].shape[1], masks["O"].shape[1], masks["L"].shape[1], a_w,
              masks["R"].shape[1], masks["I"].shape[1], masks["S"].shape[1]]
    total_w = sum(widths) + track * 6
    H = cap + 2 * PAD
    W = total_w + 2 * PAD
    canvas = np.zeros((H, W), bool)

    x = PAD
    xs = []
    for i, c in enumerate("SOLARIS"):
        xs.append(x)
        if c != "A":
            paste(canvas, masks[c], x, PAD)
        x += widths[i] + track

    # --- The A: heavy right leg + a teardrop that *is* the left leg -------------------
    ax = xs[3]
    img = Image.fromarray((canvas * 255).astype(np.uint8))
    d = ImageDraw.Draw(img)
    base = PAD + cap - 2
    apex = (ax + a_w * 0.56, PAD + 2)
    stem = cap * 0.088
    right_foot = ax + a_w * 1.0
    d.polygon([(apex[0] - stem * 0.30, apex[1]), (apex[0] + stem * 0.62, apex[1]),
               (right_foot, base), (right_foot - stem * 1.08, base)], fill=255)

    belly_x = ax + a_w * 0.30
    outer = teardrop((apex[0] + 2, apex[1] + 4), belly_x, base, cap * 0.46, bulge=2.2)
    inner = teardrop((apex[0] - cap * 0.006, apex[1] + cap * 0.16), belly_x - cap * 0.030,
                     base - cap * 0.018, cap * 0.35, bulge=2.2)
    drop = Image.new("L", img.size, 0)
    dd = ImageDraw.Draw(drop)
    dd.polygon(outer, fill=255)
    dd.polygon(inner, fill=0)                            # heavy right wall, hairline left wall
    img = Image.fromarray(np.maximum(np.array(img), np.array(drop)))
    d = ImageDraw.Draw(img)

    # The L's foot runs on into the belly of the drop.
    hair = max(3, int(cap * 0.011))
    l_end = xs[2] + masks["L"].shape[1] - 30
    target = belly_x - cap * 0.06
    for k in range(60):
        t = k / 59
        px = l_end + (target - l_end) * t
        py = base - hair - (cap * 0.035) * t * t
        d.ellipse([px - hair, py - hair, px + hair, py + hair], fill=255)

    m = np.array(img) > 127

    # --- Signed distance field -----------------------------------------------------------
    inside = ndimage.distance_transform_edt(m)
    outside = ndimage.distance_transform_edt(~m)
    sdf = np.clip(0.5 + (inside - outside) / (2 * SPREAD), 0, 1)
    out = Image.fromarray((sdf * 255).astype(np.uint8))
    out = out.resize((W // DOWNSAMPLE, H // DOWNSAMPLE), Image.LANCZOS)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    out.save(OUT)
    print(f"wrote {OUT} {out.size}, cap {cap / DOWNSAMPLE:.0f}px, spread {SPREAD / DOWNSAMPLE:.0f}px")

    if "--preview" in sys.argv:
        a = np.array(out).astype(np.float32) / 255
        alpha = np.clip((a - 0.5) * 40 + 0.5, 0, 1)
        bg = np.array([111, 150, 112], np.float32)
        fg = np.array([246, 239, 226], np.float32)
        rgb = bg[None, None] * (1 - alpha[..., None]) + fg[None, None] * alpha[..., None]
        Image.fromarray(rgb.astype(np.uint8)).resize((out.size[0] // 2, out.size[1] // 2), Image.LANCZOS) \
            .save(os.path.join(ROOT, "Tools", "logo_preview.png"))


if __name__ == "__main__":
    main()
