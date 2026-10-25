"""Crop instrument renders to their alpha bounds and pad to the card's image aspect (w/h = 0.9)."""
import glob, os, sys
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]
os.makedirs(dst, exist_ok=True)
for f in sorted(glob.glob(os.path.join(src, "*.png"))):
    im = Image.open(f).convert("RGBA")
    bbox = im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    im = im.crop(bbox)
    w, h = im.size
    pad = int(max(w, h) * 0.05)
    tw, th = w + 2 * pad, h + 2 * pad
    if tw / th < 0.9: tw = int(th * 0.9)
    else: th = int(tw / 0.9)
    canvas = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
    canvas.paste(im, ((tw - w) // 2, (th - h) // 2))
    canvas.thumbnail((620, 690), Image.LANCZOS)
    out = os.path.join(dst, os.path.basename(f))
    canvas.save(out, optimize=True)
    print(os.path.basename(f), canvas.size)
