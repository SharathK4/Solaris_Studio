"""Mirror of Song.Parse used to sanity-check charts: chord beats vs. lyric timeline."""
import glob, io, json, os, sys
ALLOWED = {"C", "D", "Em", "G", "Am", "Cm", "Dm", "E", "Gm", "A", "F", "Bb"}
root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "StreamingAssets", "Songs")
ok = True
for f in sorted(glob.glob(os.path.join(root, "*.json"))):
    j = json.load(io.open(f, encoding="utf-8"))
    meter = j["meter"]; beats = 0; bad = []
    for tok in j["chords"].split():
        if tok == "|": continue
        name, _, l = tok.partition(":")
        beats += float(l) if l else meter
        if name not in ALLOWED: bad.append(name)
    t = -j.get("pickup", 0)
    for line in j["lyrics"]:
        for tok in line.split():
            if tok == "|": continue
            w, _, l = tok.rpartition(":")
            t += float(l)
    good = not bad and t <= beats + 1e-6 and beats % meter == 0
    ok &= good
    print(f"{os.path.basename(f):22s} bars={beats/meter:4.0f} chordBeats={beats:5.0f} lyricsEnd={t:6.1f} "
          f"{'OK' if good else 'PROBLEM'} {bad if bad else ''} ~{(beats + j.get('pickup',0))*60/j['bpm']:.0f}s")
sys.exit(0 if ok else 1)
