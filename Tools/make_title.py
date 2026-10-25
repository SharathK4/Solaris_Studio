"""Turns the Scene 1 key art into three layers the title screen can animate without changing it:

  title_plate.png   the soft colour field with the lettering removed (this layer drifts slowly)
  title_detail.png  film grain, dust, scratches, light-leak flecks as a signed offset (stays static)
  title_logo_sdf.png the SOLARIS / STUDIO lettering traced from the art as a signed distance field
  title_motion.png  where the red glows are: B = weight (red + its orange halo), RG = direction
                    toward each glow's core, so the shader can make only the reds breathe and drift
  title_meta.json   where the lettering sits (image uv) and its colour

At rest, plate + detail + lettering reproduces the original image. Usage:

    python Tools/make_title.py [path/to/Scene1.jpeg] [--preview]
"""
import io
import json
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_SRC = os.path.join(os.path.dirname(ROOT), "Inspiration_Images", "Scene1.jpeg")
OUT = os.path.join(ROOT, "Assets", "Solaris", "Art", "Title")
DETAIL_SCALE = 1.6      # detail stored as d * scale + 0.5
UPSAMPLE = 8            # tracing resolution for the lettering
SDF_DOWN = 2            # 8x trace -> 4x texture
SPREAD = 24             # distance (8x px) mapped to the full 0..1 range


def lum(a):
    return a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114


def harmonic_fill(img, mask, iters=1500):
    """Fill masked pixels by solving Laplace's equation (smooth, seam-free inpainting)."""
    out = img.copy()
    # Warm start from a normalised wide blur of the known pixels so convergence is quick.
    known = ~mask
    for c in range(3):
        ch = img[..., c].copy()
        w = ndimage.gaussian_filter(known.astype(np.float32), 12)
        v = ndimage.gaussian_filter(np.where(known, ch, 0), 12)
        ch[mask] = (v / np.maximum(w, 1e-6))[mask]
        out[..., c] = ch
    ys, xs = np.nonzero(mask)
    y0, y1, x0, x1 = ys.min() - 1, ys.max() + 2, xs.min() - 1, xs.max() + 2
    sub = out[y0:y1, x0:x1].copy()
    m = mask[y0:y1, x0:x1]
    for _ in range(iters):
        avg = 0.25 * (np.roll(sub, 1, 0) + np.roll(sub, -1, 0) + np.roll(sub, 1, 1) + np.roll(sub, -1, 1))
        sub[m] = avg[m]
    out[y0:y1, x0:x1] = sub
    return out


def find_line_split(mask, y_offset):
    """Row (source px) between the SOLARIS line and the STUDIO line: the emptiest row in the gap."""
    counts = mask.sum(1)
    rows = np.nonzero(counts)[0]
    lo, hi = rows.min() + int(len(counts) * 0.45), rows.max() - 4
    gap = lo + int(np.argmin(counts[lo:hi]))
    return gap + y_offset


def main():
    src = next((a for a in sys.argv[1:] if not a.startswith("--")), DEFAULT_SRC)
    img = np.asarray(Image.open(src).convert("RGB"), np.float32) / 255.0
    H, W, _ = img.shape
    os.makedirs(OUT, exist_ok=True)

    # ---- 1. find the lettering: bright, cream, inside the central band ----------------------
    L = lum(img)
    local = ndimage.median_filter(L, size=31)
    contrast = L - local
    band = np.zeros_like(L, bool)
    band[int(H * 0.38):int(H * 0.62), int(W * 0.33):int(W * 0.68)] = True
    seed = (contrast > 0.07) & band
    seed = ndimage.binary_opening(seed, iterations=1) | (contrast > 0.12) & band
    labels, n = ndimage.label(ndimage.binary_dilation(seed, iterations=2))
    sizes = ndimage.sum(np.ones_like(L), labels, range(1, n + 1))
    keep = np.isin(labels, [i + 1 for i, s in enumerate(sizes) if s > 12])
    text_mask = keep & band
    ys, xs = np.nonzero(text_mask)
    bx0, bx1 = xs.min() - 10, xs.max() + 11
    by0, by1 = ys.min() - 10, ys.max() + 11
    print(f"lettering bbox px x[{bx0},{bx1}) y[{by0},{by1})")

    # ---- 2. background under the lettering (harmonic inpaint of a denoised copy) ------------
    hole = ndimage.binary_dilation(text_mask, iterations=3)
    smooth_src = np.stack([ndimage.gaussian_filter(img[..., c], 1.2) for c in range(3)], -1)
    bg = harmonic_fill(smooth_src, hole)

    # ---- 3. alpha matte of the lettering: project (I - B) onto (T - B) ----------------------
    core = text_mask & (contrast > np.percentile(contrast[text_mask], 70))
    T = np.median(img[core], axis=0)
    print("lettering colour", "#%02X%02X%02X" % tuple(int(round(v * 255)) for v in T))
    I_s = np.stack([ndimage.gaussian_filter(img[..., c], 0.35) for c in range(3)], -1)
    diff = I_s - bg
    tb = T[None, None, :] - bg
    alpha = np.clip((diff * tb).sum(-1) / np.maximum((tb * tb).sum(-1), 1e-4), 0, 1)
    alpha[~ndimage.binary_dilation(text_mask, iterations=4)] = 0

    # ---- 4. trace at 8x and build the SDF ---------------------------------------------------
    crop = alpha[by0:by1, bx0:bx1]
    up = ndimage.zoom(crop, UPSAMPLE, order=3)
    up = ndimage.gaussian_filter(up, UPSAMPLE * 0.18)
    # Hairlines are ~1 source px wide, so their coverage peaks well below 0.5 (even where they
    # meet a thick stem, like the T's crossbar). A white top-hat isolates everything thinner than
    # ~2.5 source px; adding it back lifts hairlines to full strength and leaves stems untouched.
    k = int(UPSAMPLE * 2.6) | 1
    tophat = up - ndimage.grey_opening(up, size=(k, k))
    boosted = up + 1.2 * tophat
    # Round away JPEG nicks: smooth the coverage slightly before taking the 50% contour.
    boosted = ndimage.gaussian_filter(boosted, UPSAMPLE * 0.28)
    # STUDIO is a single weight of ~1 px strokes: trace it at a lower level than SOLARIS so its
    # curves stay unbroken, without thickening SOLARIS's stems.
    rows = np.arange(boosted.shape[0])[:, None] / UPSAMPLE + by0
    line_split = find_line_split(text_mask[by0:by1, bx0:bx1], by0)
    thr = np.where(rows >= line_split, 0.36, 0.5)
    shape = boosted > thr
    lab, count = ndimage.label(shape)
    areas = ndimage.sum(np.ones_like(up), lab, range(1, count + 1))
    shape = np.isin(lab, [i + 1 for i, a in enumerate(areas) if a > (UPSAMPLE * 3) ** 2])
    shape = ndimage.binary_closing(shape, iterations=2)
    inside = ndimage.distance_transform_edt(shape)
    outside = ndimage.distance_transform_edt(~shape)
    sdf = np.clip(0.5 + (inside - outside) / (2 * SPREAD), 0, 1)
    sdf_img = Image.fromarray((sdf * 255).astype(np.uint8))
    sdf_img = sdf_img.resize((sdf_img.width // SDF_DOWN, sdf_img.height // SDF_DOWN), Image.LANCZOS)
    sdf_img.save(os.path.join(OUT, "title_logo_sdf.png"))

    # ---- 5. plate (moving) + detail (static) ------------------------------------------------
    # Lettering pixels become background, then grain is borrowed from just below for continuity.
    clean = img.copy()
    feather = np.clip(ndimage.gaussian_filter(ndimage.binary_dilation(text_mask, iterations=3).astype(np.float32), 1.5) * 1.6, 0, 1)
    clean = clean * (1 - feather[..., None]) + bg * feather[..., None]
    # Flecks, dust and scratches must live entirely in the static layer, or their soft halos
    # would drift with the glows. Exclude them from the colour field with a normalised blur.
    first = np.stack([ndimage.gaussian_filter(clean[..., c], 6.0, mode="reflect") for c in range(3)], -1)
    resid = clean - first
    fleck = (np.abs(lum(resid)) > 0.055) | (np.abs(resid).max(-1) > 0.08)
    fleck = ndimage.binary_dilation(fleck, iterations=3)
    keep = (~fleck).astype(np.float32)
    wsum = ndimage.gaussian_filter(keep, 6.0, mode="reflect")
    smooth = np.stack([ndimage.gaussian_filter(clean[..., c] * keep, 6.0, mode="reflect") for c in range(3)], -1)
    smooth = smooth / np.maximum(wsum, 1e-3)[..., None]
    smooth = np.where(wsum[..., None] > 0.05, smooth, first)
    detail = clean - smooth
    shift = by1 - by0 + 6
    donor = np.roll(detail, -shift, axis=0)
    donor_orig = np.roll(img - np.stack([ndimage.gaussian_filter(img[..., c], 6.0) for c in range(3)], -1), -shift, axis=0)
    detail = detail * (1 - feather[..., None]) + donor_orig * feather[..., None]
    del donor

    Image.fromarray((np.clip(smooth, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, "title_plate.png"))
    enc = np.clip(detail * DETAIL_SCALE + 0.5, 0, 1)
    Image.fromarray((enc * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, "title_detail.png"))

    # ---- 5b. motion mask: only the red glows (and their orange halos) are allowed to move ------
    red = np.clip((smooth[..., 0] - smooth[..., 1] - 0.08) / (0.36 - 0.08), 0, 1)
    red = red * red * (3 - 2 * red)
    weight = ndimage.gaussian_filter(red, 16, mode="reflect")
    weight = np.clip(weight / max(weight.max(), 1e-6) * 1.35, 0, 1)
    core = ndimage.gaussian_filter(red, 38, mode="reflect")
    gy, gx = np.gradient(core)                      # image rows grow downward
    mag = np.hypot(gx, gy)
    strength = np.clip(mag / (np.percentile(mag[weight > 0.2], 90) + 1e-6), 0, 1)
    dx = gx / (mag + 1e-6) * strength
    dy = -gy / (mag + 1e-6) * strength              # flip to uv-up convention
    motion = np.stack([dx * 0.5 + 0.5, dy * 0.5 + 0.5, weight, np.ones_like(weight)], -1)
    mimg = Image.fromarray((np.clip(motion, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA")
    mimg.resize((W // 2, H // 2), Image.LANCZOS).save(os.path.join(OUT, "title_motion.png"))

    meta = {
        "imageWidth": W, "imageHeight": H,
        # uv with origin bottom-left (Unity convention)
        "logoMin": [bx0 / W, 1 - by1 / H], "logoMax": [bx1 / W, 1 - by0 / H],
        "logoColor": "%02X%02X%02X" % tuple(int(round(v * 255)) for v in T),
        "detailScale": DETAIL_SCALE,
    }
    with io.open(os.path.join(OUT, "title_meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)
    print(json.dumps(meta))

    # ---- 6. verification: rebuild the still and compare -------------------------------------
    if "--preview" in sys.argv:
        sd = np.asarray(Image.open(os.path.join(OUT, "title_logo_sdf.png")), np.float32) / 255
        sd = np.asarray(Image.fromarray((sd * 255).astype(np.uint8)).resize((bx1 - bx0, by1 - by0), Image.LANCZOS), np.float32) / 255
        a = np.clip((sd - 0.5) * 6 + 0.5, 0, 1)
        plate = np.asarray(Image.open(os.path.join(OUT, "title_plate.png")), np.float32) / 255
        det = (np.asarray(Image.open(os.path.join(OUT, "title_detail.png")), np.float32) / 255 - 0.5) / DETAIL_SCALE
        rebuilt = plate + det
        region = rebuilt[by0:by1, bx0:bx1]
        rebuilt[by0:by1, bx0:bx1] = region * (1 - a[..., None]) + T[None, None, :] * a[..., None]
        rebuilt = np.clip(rebuilt, 0, 1)
        err = np.abs(rebuilt - img)
        print(f"rebuild error mean {err.mean() * 255:.2f}/255, 99th pct {np.percentile(err, 99) * 255:.1f}/255")
        side = np.concatenate([img, rebuilt], axis=1)
        Image.fromarray((side * 255).astype(np.uint8)).save(os.path.join(ROOT, "Tools", "title_compare.png"))
        z = np.concatenate([img[by0 - 20:by1 + 20, bx0 - 20:bx1 + 20], rebuilt[by0 - 20:by1 + 20, bx0 - 20:bx1 + 20]], axis=0)
        Image.fromarray((z * 255).astype(np.uint8)).resize((z.shape[1] * 3, z.shape[0] * 3), Image.LANCZOS) \
            .save(os.path.join(ROOT, "Tools", "title_logo_compare.png"))


if __name__ == "__main__":
    main()
