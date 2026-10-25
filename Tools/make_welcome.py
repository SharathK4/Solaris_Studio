"""Splits the Scene 2 key art into layers so the orb can bounce while everything else stays exact.

  welcome_plate.png   the warm horizon gradient alone (no orb, no text)
  welcome_detail.png  film grain as a signed offset (static, over everything)
  welcome_orb.png     the orb: straight-alpha RGBA, colour solved in linear light so that
                      orb-over-plate reproduces the original pixels at the reference position
  welcome_meta.json   orb centre/radius and text line boxes (image px)

    python Tools/make_welcome.py [--preview]
"""
import io
import json
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(os.path.dirname(ROOT), "Inspiration_Images", "Scene2.jpeg")
OUT = os.path.join(ROOT, "Assets", "Solaris", "Art", "Welcome")
DETAIL_SCALE = 1.6
ORB_TEX = 512           # orb texture size
ORB_MARGIN = 1.12       # texture covers this many radii from the centre


def to_lin(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def harmonic_fill(img, mask, iters=1500):
    out = img.copy()
    known = (~mask).astype(np.float32)
    for c in range(img.shape[2]):
        w = ndimage.gaussian_filter(known, 10)
        v = ndimage.gaussian_filter(img[..., c] * known, 10)
        ch = out[..., c]
        ch[mask] = (v / np.maximum(w, 1e-6))[mask]
    ys, xs = np.nonzero(mask)
    y0, y1, x0, x1 = max(ys.min() - 1, 0), ys.max() + 2, max(xs.min() - 1, 0), xs.max() + 2
    sub = out[y0:y1, x0:x1].copy()
    m = mask[y0:y1, x0:x1]
    for _ in range(iters):
        avg = 0.25 * (np.roll(sub, 1, 0) + np.roll(sub, -1, 0) + np.roll(sub, 1, 1) + np.roll(sub, -1, 1))
        sub[m] = avg[m]
    out[y0:y1, x0:x1] = sub
    return out


def main():
    I = np.asarray(Image.open(SRC).convert("RGB"), np.float32) / 255.0
    H, W, _ = I.shape
    os.makedirs(OUT, exist_ok=True)
    L = I.mean(-1)

    # ---- text: bright near-white glyphs ---------------------------------------------------------
    local = ndimage.median_filter(L, size=25)
    txt = (L - local > 0.03) & (I.min(-1) > 0.62)
    txt = ndimage.binary_dilation(txt, iterations=3)
    lab, n = ndimage.label(ndimage.binary_dilation(txt, iterations=6))
    lines = []
    for sl in ndimage.find_objects(lab):
        ys, xs = sl
        if (ys.stop - ys.start) < 6 or (xs.stop - xs.start) < 40:
            continue
        lines.append([int(xs.start), int(ys.start), int(xs.stop), int(ys.stop)])
    lines.sort(key=lambda b: b[1])
    # Split multi-line blocks at their emptiest row, then measure ink extents + baseline per line.
    measured = []
    for x0, y0, x1, y1 in lines:
        x0, x1 = max(x0 - 12, 0), min(x1 + 12, W)
        patch = L[y0:y1, x0:x1]
        ink = np.zeros_like(L, bool)
        ink[y0:y1, x0:x1] = (patch - np.median(patch, axis=1, keepdims=True)) > 0.1
        rows = ink[y0:y1, x0:x1].sum(1)
        cuts = [0]
        nz = np.nonzero(rows)[0]
        span = nz.max() - nz.min()
        if span > 60:                       # two lines
            mid = nz.min() + span // 2
            win = rows[mid - 12:mid + 12]
            cuts.append(mid - 12 + int(np.argmin(win)))
        cuts.append(len(rows))
        for a, b in zip(cuts[:-1], cuts[1:]):
            sub = ink[y0 + a:y0 + b, x0:x1]
            r = sub.sum(1)
            ys = np.nonzero(r)[0]
            if len(ys) == 0:
                continue
            xs = np.nonzero(sub.sum(0))[0]
            # baseline: last row that still carries a third of the peak ink (descenders are sparse)
            strong = np.nonzero(r > r.max() * 0.33)[0]
            measured.append({"x0": int(x0 + xs.min()), "x1": int(x0 + xs.max() + 1),
                             "top": int(y0 + a + ys.min()), "baseline": int(y0 + a + strong.max() + 1),
                             "bottom": int(y0 + a + ys.max() + 1)})
    print("lines", measured)
    clean = harmonic_fill(I, txt)

    # ---- background: horizontal bands (rows are uniform) ----------------------------------------
    side = np.concatenate([clean[:, 15:340], clean[:, 910:W - 15]], 1)
    row = np.median(side, axis=1)
    row = np.stack([ndimage.gaussian_filter1d(row[:, c], 2.0) for c in range(3)], -1)
    bands = np.repeat(row[:, None, :], W, axis=1)
    FINE = 1.2   # grain/structure split: only true film grain is finer than this
    base = np.stack([ndimage.gaussian_filter(clean[..., c], FINE) for c in range(3)], -1)

    # ---- orb geometry: fit a circle to where the art departs from the bands ----------------------
    smooth = np.stack([ndimage.gaussian_filter(clean[..., c], 2.5) for c in range(3)], -1)
    diff = np.abs(smooth - bands).sum(-1)
    m = diff > 0.10
    m[:, :330] = False
    m[:, 920:] = False
    m[:200] = False          # the top of the orb melts into the sky: fit on the sides and bottom
    m[620:] = False
    lab, n = ndimage.label(m)
    big = np.argmax(ndimage.sum(np.ones_like(diff), lab, range(1, n + 1))) + 1
    ys, xs = np.nonzero(lab == big)
    cx = (xs.min() + xs.max()) / 2.0
    R = (xs.max() - xs.min()) / 2.0
    bottom = ys.max()
    cy = bottom - R
    print(f"orb centre ({cx:.1f}, {cy:.1f}) radius {R:.1f} (bottom {bottom})")

    yy0, xx0 = np.mgrid[0:H, 0:W]
    rr0 = np.hypot(xx0 - cx, yy0 - cy) / R
    disk = rr0 < 1.10
    # Everything the bouncing orb (or the re-typeset text) can uncover must look like the sky
    # around it: low frequencies are filled harmonically, the soft mottling is borrowed from the
    # same rows 500 px to the side (outside every hole), so no smooth "patch" shows when the orb lifts.
    hole = disk | ndimage.binary_dilation(txt, iterations=8)
    res = base - bands
    low = harmonic_fill(res, hole, iters=2500)
    mottle = res - np.stack([ndimage.gaussian_filter(res[..., c], 8) for c in range(3)], -1)
    S = 500
    syn = np.zeros_like(res)
    wsum = np.zeros((H, W, 1), np.float32)
    w2sum = np.zeros((H, W, 1), np.float32)
    for sh in (S, -S):
        src = np.roll(mottle, -sh, axis=1)                 # src[y, x] = mottle[y, x + sh]
        ok = np.roll(~hole, -sh, axis=1)
        xs_ = np.arange(W) + sh
        ok &= ((xs_ >= 12) & (xs_ < W - 12))[None, :]
        w = ndimage.gaussian_filter(ok.astype(np.float32), 18)[..., None] * ok[..., None]
        syn += src * w
        wsum += w
        w2sum += w * w
    syn = syn / np.sqrt(np.maximum(w2sum, 1e-6))               # variance-preserving blend
    syn = np.where(wsum > 1e-4, syn, 0)
    fill = bands + low + syn
    soft = np.clip((rr0 - 1.04) / 0.06, 0, 1)                  # feather into the real sky outside the rim glow
    soft = soft * soft * (3 - 2 * soft)
    keep = np.where(disk, soft, 1.0)
    keep = np.minimum(keep, 1.0 - ndimage.gaussian_filter((hole & ~disk).astype(np.float32), 2.0))
    keep[~hole] = 1.0
    plate = np.clip(base * keep[..., None] + fill * (1 - keep[..., None]), 0, 1)

    # ---- orb alpha model + linear-light unmixing -------------------------------------------------
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    px = (xx - cx) / R
    py = (cy - yy) / R                       # y up
    r = np.sqrt(px * px + py * py)
    edge = 1.0 - np.clip((r - 0.955) / (1.035 - 0.955), 0, 1)
    edge = edge * edge * (3 - 2 * edge)
    top = np.clip((py - 0.05) / 0.95, 0, 1)
    alpha = edge * (1.0 - 0.42 * top * top)  # the upper cap is partly translucent
    a = alpha[..., None]
    # The crisp rim highlight belongs to the orb (it must travel with it), not to the static grain.
    # It is coherent along the circle while grain is not: average the fine layer along the arc
    # (polar resample, smooth in angle only) to pull the rim out and leave pure grain behind.
    ring = np.clip(1 - np.abs(r - 1.0) / 0.075, 0, 1)
    ring = (ring * ring * (3 - 2 * ring))[..., None]
    fine = clean - base
    r_lo, r_hi, dr = 0.88, 1.12, 0.25 / R
    nr = int(round((r_hi - r_lo) / dr)) + 1
    nt = int(round(2 * np.pi * R / 0.5))
    rs_ = r_lo + np.arange(nr) * dr
    ts_ = np.arange(nt) * (2 * np.pi / nt)
    RR, TT = np.meshgrid(rs_, ts_, indexing="ij")
    py_ = cy - RR * R * np.sin(TT)
    px_ = cx + RR * R * np.cos(TT)
    rim_polar = []
    for c in range(3):
        pol = ndimage.map_coordinates(fine[..., c], [py_, px_], order=1, mode="nearest")
        pol = ndimage.gaussian_filter1d(pol, 12.0, axis=1, mode="wrap")     # ~6 px of arc
        rim_polar.append(pol)
    th = np.mod(np.arctan2(cy - yy, xx - cx), 2 * np.pi)
    ri = (r - r_lo) / dr
    ti = th / (2 * np.pi / nt)
    inz = (ri >= 0) & (ri <= nr - 1)
    rim = np.zeros_like(fine)
    for c in range(3):
        pol = np.concatenate([rim_polar[c], rim_polar[c][:, :1]], 1)        # wrap column for interpolation
        v = ndimage.map_coordinates(pol, [np.clip(ri, 0, nr - 1), ti], order=1, mode="nearest")
        rim[..., c] = np.where(inz, v, 0)
    rim *= ring
    Ilin, Blin = to_lin(np.clip(base + rim, 0, 1)), to_lin(plate)
    F = (Ilin - (1 - a) * Blin) / np.maximum(a, 1e-3)
    F = np.where(a > 0.04, F, Ilin)
    F = np.clip(F, 0, 1)
    F = np.stack([ndimage.gaussian_filter(F[..., c], 0.6) for c in range(3)], -1)

    # resample the orb into its own square texture (orb-local coords)
    g = (np.arange(ORB_TEX) + 0.5) / ORB_TEX * 2 - 1
    gx, gy = np.meshgrid(g, -g)
    sx = cx + gx * R * ORB_MARGIN
    sy = cy - gy * R * ORB_MARGIN
    orb_rgb = np.stack([ndimage.map_coordinates(to_srgb(F)[..., c], [sy, sx], order=1, mode="nearest") for c in range(3)], -1)
    orb_a = ndimage.map_coordinates(alpha, [sy, sx], order=1, mode="constant")
    orb = np.concatenate([orb_rgb, orb_a[..., None]], -1)
    Image.fromarray((np.clip(orb, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(OUT, "welcome_orb.png"))

    # ---- static film grain ----------------------------------------------------------------------
    detail = clean - base - rim                          # the rim now lives in the orb layer
    text_zone = ndimage.binary_dilation(txt, iterations=3)
    donor = np.roll(detail, 40, axis=0)                  # grain from just below the text
    detail[text_zone] = donor[text_zone]
    Image.fromarray((np.clip(detail * DETAIL_SCALE + 0.5, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, "welcome_detail.png"))
    Image.fromarray((np.clip(plate, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, "welcome_plate.png"))

    meta = {
        "imageWidth": W, "imageHeight": H,
        "orbCenter": [cx / W, 1 - cy / H], "orbRadius": R / H, "orbMargin": ORB_MARGIN,
        "lines": measured,
        "detailScale": DETAIL_SCALE,
    }
    with io.open(os.path.join(OUT, "welcome_meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)

    if "--preview" in sys.argv:
        # Rebuild: plate, orb over it in linear light, plus grain (text omitted).
        rebuilt_lin = Blin * (1 - a) + F * a
        rebuilt = to_srgb(rebuilt_lin) + detail
        err = np.abs(rebuilt - clean)
        ex = (ring[..., 0] > 0.5)
        print(f"rim zone error mean {err[ex].mean() * 255:.2f}/255")
        print(f"rebuild error mean {err.mean() * 255:.2f}/255, 99th pct {np.percentile(err, 99) * 255:.1f}/255")
        # Same orb moved 50 px down, to sanity-check translucency over different bands.
        shift = 50
        a2 = np.roll(alpha, shift, axis=0)[..., None]
        F2 = np.roll(to_srgb(F), shift, axis=0)
        moved = to_srgb(Blin * (1 - a2) + to_lin(F2) * a2) + detail
        side = np.concatenate([clean, rebuilt, moved], 1)
        Image.fromarray((np.clip(side, 0, 1) * 255).astype(np.uint8)).resize((W * 3 // 2, H // 2)).save(os.path.join(ROOT, "Tools", "welcome_compare.png"))


if __name__ == "__main__":
    main()
