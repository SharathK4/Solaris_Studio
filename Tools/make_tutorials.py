"""Rebuilds the Scene 4 / Scene 5 tutorial mock-ups as layers.

For each scene:
  tut{n}_plate.png   background with every card, title and button removed (smooth)
  tut{n}_card{i}.png glass card i exactly as designed (hand, icons, glow), with its text, divider
                     and chord diagram lifted out; 2x Lanczos + gentle sharpening, rounded-rect alpha
  tut{n}_meta.json   card boxes and the measured text lines / diagram boxes (reference px)

The app re-typesets the text and diagrams crisply at the measured positions, so the screen looks
like the mock-up at any resolution, and cards can lift independently.

    python Tools/make_tutorials.py [4|5 ...] [--preview]
"""
import io
import json
import os
import sys

import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REF = os.path.join(os.path.dirname(ROOT), "Inspiration_Images")
OUT = os.path.join(ROOT, "Assets", "Solaris", "Art", "Tutorials")

SCENES = {
    4: {
        "file": "Scene4.jpeg",
        "cards": [(43, 162, 263, 541), (279, 162, 500, 541), (517, 162, 737, 541), (754, 162, 974, 541), (991, 162, 1211, 541)],
        "radius": 18,
        "text_zone": 338,           # rows below this (inside a card) hold text / diagrams
        "divider_band": (418, 446),
        "title_zone": (380, 60, 870, 135),
        "button": (512, 572, 740, 632),
    },
    5: {
        "file": "Scene5.jpeg",
        "cards": [(45, 153, 259, 573), (274, 153, 499, 573), (514, 153, 737, 573), (753, 153, 978, 573), (993, 153, 1208, 573)],
        "radius": 18,
        "text_zone": 392,
        "divider_band": (434, 456),
        "title_zone": (380, 55, 870, 128),
        "button": (512, 592, 738, 652),
    },
}


def harmonic_fill(img, mask, iters=1200):
    """Laplace fill of masked pixels. Large holes are solved coarse-to-fine for speed."""
    out = img.copy()
    if not mask.any():
        return out
    ys, xs = np.nonzero(mask)
    if (ys.max() - ys.min()) * (xs.max() - xs.min()) > 120 * 120 and iters > 200:
        f = 4
        small = np.stack([ndimage.zoom(img[..., c], 1 / f, order=1) for c in range(img.shape[2])], -1)
        sm = ndimage.zoom(mask.astype(np.float32), 1 / f, order=1) > 0.01
        coarse = harmonic_fill(small, sm, iters=iters)
        up = np.stack([ndimage.zoom(coarse[..., c], (img.shape[0] / coarse.shape[0], img.shape[1] / coarse.shape[1]), order=1)
                       for c in range(img.shape[2])], -1)
        out[mask] = up[mask]
        return _relax(out, mask, 150)
    known = (~mask).astype(np.float32)
    w = ndimage.gaussian_filter(known, 8)
    return _relax(_warm(out, img, known, w, mask), mask, iters)


def _warm(out, img, known, w, mask):
    for c in range(img.shape[2]):
        v = ndimage.gaussian_filter(img[..., c] * known, 8)
        ch = out[..., c]
        ch[mask] = (v / np.maximum(w, 1e-6))[mask]
    return out


def _relax(out, mask, iters):
    ys, xs = np.nonzero(mask)
    y0, y1 = max(ys.min() - 1, 0), min(ys.max() + 2, out.shape[0])
    x0, x1 = max(xs.min() - 1, 0), min(xs.max() + 2, out.shape[1])
    sub = out[y0:y1, x0:x1].copy()
    m = mask[y0:y1, x0:x1]
    for _ in range(iters):
        avg = 0.25 * (np.roll(sub, 1, 0) + np.roll(sub, -1, 0) + np.roll(sub, 1, 1) + np.roll(sub, -1, 1))
        sub[m] = avg[m]
    out[y0:y1, x0:x1] = sub
    return out


def rounded_mask(w, h, r, inset=0.0, ss=4):
    """Anti-aliased rounded-rectangle coverage (supersampled)."""
    yy, xx = np.mgrid[0:h * ss, 0:w * ss].astype(np.float32)
    xx = (xx + 0.5) / ss
    yy = (yy + 0.5) / ss
    hx, hy = w / 2 - inset, h / 2 - inset
    px, py = np.abs(xx - w / 2), np.abs(yy - h / 2)
    qx, qy = px - hx + r, py - hy + r
    d = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r
    cov = (d <= 0).astype(np.float32)
    return cov.reshape(h, ss, w, ss).mean((1, 3))


def line_clusters(mask, min_gap=3):
    """Row clusters of a binary mask -> [(top, bottom)] (bottom exclusive)."""
    rows = mask.sum(1) > 0
    out, start, gap = [], None, 0
    for y, on in enumerate(rows):
        if on:
            if start is None:
                start = y
            gap = 0
            end = y + 1
        elif start is not None:
            gap += 1
            if gap > min_gap:
                out.append((start, end))
                start = None
    if start is not None:
        out.append((start, end))
    return out


def measure_lines(content, x0, y0):
    """Split a content mask (one card's text zone) into lines and measure ink box + baseline."""
    res = []
    for t, b in line_clusters(content):
        band = content[t:b]
        cols = np.nonzero(band.sum(0))[0]
        r = band.sum(1)
        strong = np.nonzero(r > r.max() * 0.33)[0]
        # split widely separated groups on one row band (e.g. chord letter vs. diagram)
        groups, gs = [], cols[0]
        for a, c in zip(cols[:-1], cols[1:]):
            if c - a > 24:
                groups.append((gs, a + 1))
                gs = c
        groups.append((gs, cols[-1] + 1))
        for g0, g1 in groups:
            sub = band[:, g0:g1]
            for st_, en_ in line_clusters(sub, min_gap=2) if g1 - g0 < 120 else [(0, sub.shape[0])]:
                part = sub[st_:en_]
                cols2 = np.nonzero(part.sum(0))[0]
                rr = part.sum(1)
                ys = np.nonzero(rr)[0]
                st = np.nonzero(rr > rr.max() * 0.33)[0]
                res.append({"x0": int(x0 + g0 + cols2.min()), "x1": int(x0 + g0 + cols2.max() + 1),
                            "top": int(y0 + t + st_ + ys.min()), "baseline": int(y0 + t + st_ + st.max() + 1),
                            "bottom": int(y0 + t + st_ + ys.max() + 1)})
    return res


def build(n, preview):
    cfg = SCENES[n]
    I = np.asarray(Image.open(os.path.join(REF, cfg["file"])).convert("RGB"), np.float32) / 255.0
    H, W, _ = I.shape
    smooth = np.stack([ndimage.gaussian_filter(I[..., c], 0.8) for c in range(3)], -1)
    os.makedirs(OUT, exist_ok=True)

    work = smooth.copy()
    cards_meta = []
    for i, (x0, y0, x1, y1) in enumerate(cfg["cards"]):
        zone_top = cfg["text_zone"]
        # Text/diagram ink: departs from a wide median of the glass behind it.
        sub = smooth[zone_top:y1 - 6, x0 + 6:x1 - 6]
        L = sub.mean(-1)
        bg = ndimage.median_filter(L, size=31)
        ink = (L - bg) > 0.05
        ink = ndimage.binary_opening(ink, iterations=1) | ((L - bg) > 0.09)
        lab, cnt = ndimage.label(ink)
        sizes = ndimage.sum(np.ones_like(L), lab, range(1, cnt + 1))
        ink = np.isin(lab, [k + 1 for k, s in enumerate(sizes) if s >= 3])
        lines = measure_lines(ink, x0 + 6, zone_top)
        # Divider: the row whose thin bright line spans most of the card's inner width.
        d0, d1 = cfg["divider_band"]
        Lz = smooth[d0 - 6:d1 + 6, x0 + 30:x1 - 30].mean(-1)
        hp = (Lz - ndimage.gaussian_filter(Lz, 2.0)).mean(1)[6:-6]
        divider = int(d0 + int(np.argmax(hp)))
        # Remove: dilated ink + every measured box + the divider strip, inpainted from the glass.
        hole = np.zeros((H, W), bool)
        hole[zone_top:y1 - 6, x0 + 6:x1 - 6] = ndimage.binary_dilation(ink, iterations=3)
        for l in lines:
            hole[l["top"] - 4:l["bottom"] + 4, l["x0"] - 4:l["x1"] + 4] = True
        hole[divider - 3:divider + 4, x0 + 14:x1 - 14] = True
        work = harmonic_fill(work, hole)
        lines = [l for l in lines if l["bottom"] - l["top"] > 3]      # drop divider fragments
        cards_meta.append({"box": [x0, y0, x1, y1], "lines": lines, "divider": divider})
        print(f"scene {n} card {i}: {len(lines)} ink groups")
        for l in lines:
            print("   ", l)

    # Title, subtitle and button: measure, then remove from the background.
    tx0, ty0, tx1, ty1 = cfg["title_zone"]
    sub = smooth[ty0:ty1, tx0:tx1]
    L = sub.mean(-1)
    ink = (L - ndimage.median_filter(L, size=31)) > 0.05
    title_lines = measure_lines(ink, tx0, ty0)
    print(f"scene {n} title lines", title_lines)

    bx0, by0, bx1, by1 = cfg["button"]
    bsub = smooth[by0:by1, bx0:bx1].mean(-1)
    bm = bsub > 0.75
    ys, xs = np.nonzero(bm)
    button = [int(bx0 + xs.min()), int(by0 + ys.min()), int(bx0 + xs.max() + 1), int(by0 + ys.max() + 1)]
    print(f"scene {n} button", button)
    # Label and arrow inside the pill (dark ink on cream), so the crisp pill matches the design.
    Lb = I[button[1]:button[3], button[0]:button[2]].mean(-1)
    dm = np.zeros_like(Lb, bool)
    dm[4:-4, 12:-12] = Lb[4:-4, 12:-12] < 0.5
    cols = np.nonzero(dm.sum(0))[0]
    groups, gs = [], cols[0]
    for a, c in zip(cols[:-1], cols[1:]):
        if c - a > 9:
            groups.append((gs, a + 1))
            gs = c
    groups.append((gs, cols[-1] + 1))
    a0, a1 = groups[-1]
    ay = np.nonzero(dm[:, a0:a1].sum(1))[0]
    button_arrow = [int(button[0] + a0), int(button[1] + ay.min()), int(button[0] + a1), int(button[1] + ay.max() + 1)]
    lm = dm.copy()
    lm[:, a0:] = False
    rr = lm.sum(1)
    ly = np.nonzero(rr)[0]
    lc = np.nonzero(lm.sum(0))[0]
    strong = np.nonzero(rr > rr.max() * 0.33)[0]
    button_label = {"x0": int(button[0] + lc.min()), "x1": int(button[0] + lc.max() + 1),
                    "top": int(button[1] + ly.min()), "baseline": int(button[1] + strong.max() + 1),
                    "bottom": int(button[1] + ly.max() + 1)}
    print(f"scene {n} button label", button_label, "arrow", button_arrow)

    bg_hole = np.zeros((H, W), bool)
    for (x0, y0, x1, y1) in cfg["cards"]:
        bg_hole[y0 - 3:y1 + 3, x0 - 3:x1 + 3] = True
    t_hole = np.zeros((H, W), bool)
    t_hole[ty0:ty1, tx0:tx1] = ndimage.binary_dilation(ink, iterations=3)
    bg_hole |= t_hole
    pad = 26
    bg_hole[button[1] - pad:button[3] + pad, button[0] - pad:button[2] + pad] = True
    plate = harmonic_fill(smooth, bg_hole, iters=2500)
    plate = np.stack([ndimage.gaussian_filter(plate[..., c], 1.5) for c in range(3)], -1)
    Image.fromarray((np.clip(plate, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, f"tut{n}_plate.png"))

    # Cards: the cleaned glass, 2x with a touch of sharpening, rounded-rect alpha (inside the border).
    for i, (x0, y0, x1, y1) in enumerate(cfg["cards"]):
        crop = work[y0:y1, x0:x1]
        w, h = x1 - x0, y1 - y0
        im = Image.fromarray((np.clip(crop, 0, 1) * 255 + 0.5).astype(np.uint8))
        im = im.resize((w * 2, h * 2), Image.LANCZOS).filter(ImageFilter.UnsharpMask(radius=1.6, percent=55, threshold=1))
        a = rounded_mask(w * 2, h * 2, cfg["radius"] * 2, inset=2.5)
        rgba = np.concatenate([np.asarray(im, np.float32) / 255, a[..., None]], -1)
        Image.fromarray((rgba * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(OUT, f"tut{n}_card{i}.png"))

    meta = {"imageWidth": W, "imageHeight": H, "radius": cfg["radius"], "cards": cards_meta,
            "titleLines": title_lines, "button": button, "buttonLabel": button_label, "buttonArrow": button_arrow}
    with io.open(os.path.join(OUT, f"tut{n}_meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=1)

    if preview:
        comp = plate.copy()
        for i, (x0, y0, x1, y1) in enumerate(cfg["cards"]):
            card = np.asarray(Image.open(os.path.join(OUT, f"tut{n}_card{i}.png")).resize((x1 - x0, y1 - y0), Image.LANCZOS), np.float32) / 255
            a = card[..., 3:4]
            comp[y0:y1, x0:x1] = comp[y0:y1, x0:x1] * (1 - a) + card[..., :3] * a
        side = np.concatenate([I, comp], 1)
        Image.fromarray((np.clip(side, 0, 1) * 255).astype(np.uint8)).resize((W, H // 2)).save(os.path.join(ROOT, "Tools", f"tut{n}_compare.png"))


if __name__ == "__main__":
    which = [int(a) for a in sys.argv[1:] if a.isdigit()] or [4, 5]
    for n in which:
        build(n, "--preview" in sys.argv)
