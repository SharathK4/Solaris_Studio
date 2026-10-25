# Solaris Studio — Architecture & Plan

Webcam-AR gesture instrument for Windows (Unity 6000.0.39f1, URP 17.0.3, Mono, x64).
You hold up fingers to pick guitar chords, your other hand shapes the sound, and the song
(lyrics, chord chart, backing band) plays karaoke-style around a live, segmented camera image of you.

## 1. Version decisions (researched 2026-09-30)

| Area | Choice | Why |
|---|---|---|
| Engine | Unity **6000.0.39f1** (already installed) | Supported by every package below. Note: CVE-2025-59489 is patched from 6000.0.58f2; upgrade the editor before a public release. |
| Rendering | **URP 17.0.3**, Linear, HDR off, Forward | Future-proof; every visual is a custom shader, so no post stack is needed |
| ML runtime | **Sentis 2.6.1** (`com.unity.ai.inference`, namespace `Unity.InferenceEngine`) | Needs only Unity 6000.0. Runs on GPU compute (RTX 4060). 2.4+ imports `.tflite` directly |
| Hand models | MediaPipe **BlazePalm detector (192²) + hand landmarker (224², 21 pts, presence, handedness)**, ONNX from `huggingface.co/unity/inference-engine-blaze-hand` | Unity's official conversion of Google's current hand_landmarker |
| Face | MediaPipe **BlazeFace short-range (128²)**, `unity/inference-engine-blaze-face` | Centres the sound waves and drives head-coupled parallax |
| Person mask | MediaPipe **selfie_segmenter_landscape (144×256) .tflite**, Google's model bucket | Puts the waves *behind* you (real AR depth layering). Stays on the GPU, with no readback |
| Input | Input System 1.13 (keyboard/mouse fallbacks everywhere) | |
| UI | uGUI 2.0 + TextMeshPro, built in code, custom UI shaders | Needs richer shader effects than UI Toolkit allows |
| Audio | Procedural DSP in `OnAudioFilterRead`: Karplus-Strong guitar, synth backing band, Freeverb-style reverb | Zero samples to license, sample-accurate, about 5 ms buffer |

## 2. How hand tracking works in real time

```
WebCamTexture (FHD cam, 1280x720@30, the IR cam is skipped)
  -> CameraFeed: orientation-normalised RenderTexture, once per new camera frame
  -> VisionService async loop (Awaitable, never blocks the main thread)
       1. Palm detector  (only while fewer than 2 hands are tracked)
            GPU: sigmoid -> TopK(6) -> gather boxes  -> readback 6 candidates
            CPU: anchor decode -> weighted NMS -> new hand tracks (rotated ROI, x2.6)
       2. Landmarker per track (one worker per slot, so both run in parallel)
            ROI from *previous frame's landmarks* (MediaPipe tracking mode: rotation
            wrist->middle-MCP, rotated bbox x2.0, shifted toward the fingers)
            -> 21 landmarks + presence + handedness, async readback
            presence < 0.5 drops the track, and the palm detector re-acquires it
       3. Face detector (every 3rd iteration) and person segmenter (Stage screen only,
          output rendered straight to a RenderTexture)
  -> One-Euro filter per landmark (jitter-free at rest, low lag in motion)
  -> GestureService
       HandPose: per-finger extension (joint angles + distance ratios, hysteresis),
                 finger count, openness, roll tilt, pitch, height, pinch
       Temporal voting (about 100 ms) -> stable ChordGesture (1..5 fingers = C, D, Em, G, Am)
       Roles: left hand on screen = CHORD hand, right = EXPRESSION hand (swap with H)
       CircleGesture: signed turning angle of the fingertip velocity (centre-free, so
                      it works anywhere in frame) -> dial rotation
```

Budget on an RTX 4060: palm about 1 ms, 2 x landmarks about 2 ms, face about 0.3 ms, mask about 1 ms,
so there is plenty of headroom at a 30 fps camera. Readback is async, which keeps the render
loop at the display rate (capped at 120 fps).

## 3. How the AR layer is plugged in

A laptop webcam has no depth sensor or SLAM, so AR Foundation doesn't apply. The AR layer is built from:

* **Anchoring**: every overlay is placed in mirrored camera space, using hand landmarks
  (fingertip glows, gesture cursor) and the face centre (sound waves, parallax).
* **Occlusion/depth**: the person mask composites `background -> waves/particles -> you -> HUD`,
  so waves pass behind you.
* **Head-coupled parallax**: HUD panels tilt and shift with the face position, so they read as
  objects floating in front of you.

## 4. Audio and gameplay

* Song = JSON chart (`StreamingAssets/Songs`): bpm, meter, chord per bar, lyric tokens with beat durations.
* Audio-thread sequencer: count-in, backing band (kick, shaker, bass, pad following the
  chart), strum pattern per meter (4/4 `D DU UDU`, 3/4 `D DU DU`).
* Each strum uses the chord you're **holding** at that sample; a fist or no hand mutes.
* Expression hand: height is volume, roll is tone (loop-filter and low-pass), openness is
  major/minor, horizontal lean is pan.
* Scoring: every strum is compared with the chart chord (±200 ms grace) to give accuracy, streak and a grade.
* Songs shipped are public-domain (Amazing Grace, Scarborough Fair, ...) because commercial lyrics
  (Bollywood etc.) are copyrighted. Drop your own licensed charts into `StreamingAssets/Songs`.

## 5. Screens (one Unity scene, a router with cross-fades)

| # | Screen | Background (animated, static grain) | Interaction |
|---|---|---|---|
| 1 | Title | The key art itself, layered; only its red glows breathe | Enter / click |
| 2 | Welcome | The mock-up's sky; its orb bounces slowly (spring floor, squash, warm halo); headline letters rise in | Any key / click (starts the camera) |
| 3 | Song dial | Dark copper glow + faint duotone camera, fingertip spotlight, light grain | Circle index finger to rotate, pinch-hold or Enter to pick |
| 4 | Chord tutorial | The mock-up's backdrop and glass cards (photo hands); text and chord diagrams re-typeset | Live: showing a gesture lifts its card and strums it |
| 5 | Controls tutorial | Same approach, Scene 5 cards | Live: the card for the control you perform lifts |
| 6 | Jamming session | Golden top glow, instrument cards (guitar active) | Arrows, click or pinch |
| 7 | Stage | Segmented camera, radial sound waves, floating HUD | Play; Esc pauses; results at the end |

Screens 1, 2, 4 and 5 are rebuilt from their mock-ups (`Tools/make_*.py`). The art is split into
layers, and all text is re-typeset in TextMeshPro at the measured reference positions (`RefSpace`
fits ink, not TMP's advance/ascender boxes). Both use the same "fit width, extend the rest" rule, so
the result matches the design on 16:9 and 16:10. The project blends in linear light, so translucent
UI uses `Theme.DesignAlpha` to read like the sRGB-blended design files.

Grain is a fixed, full-resolution film texture with no per-frame noise, so it never "TV-static"
jitters. Expensive backgrounds render at reduced resolution into RenderTextures and are upscaled,
because they are soft by design. The quality tier scales that resolution for weaker GPUs.

## 6. Code layout

```
Assets/Solaris/
  Scripts/Core        App bootstrap, router, screen base, tween/spring, theme, CLI automation
  Scripts/UI          UI builder, rounded/glass graphics, chord diagram, pill button, hand cursor
  Scripts/Vision      CameraFeed, VisionService, BlazeMath, OneEuro, HandFrame, face and segmenter
  Scripts/Gestures    HandPose analysis, chord voting, circle gesture, roles
  Scripts/Music       Chords/voicings, song model and parser, library
  Scripts/Audio       Engine (audio thread), KS guitar, band, reverb, sequencer, meters
  Scripts/Screens     Title, Welcome, SongSelect, ChordTutorial, ControlsTutorial, Instruments, Stage
  Shaders             Backgrounds, grain, glass, SDF hand, waves/composite, logo SDF, compute sampler
  Editor              Project setup (URP, player, scene), build, model inspector
  Tests               EditMode: gestures, circle, song parsing/validation, synth sanity
```

## 7. Testing per section

1. EditMode unit tests (batch mode): finger counting on synthetic skeletons, circle direction,
   song charts (beat totals, allowed chords), synth output (non-silent, no NaN, bounded).
2. Tracking self-test on real photos (`TestData/`): palm and landmarks, then finger counts in the log.
3. Player automation: `SolarisStudio.exe -solaris-screen N -solaris-capture out.png` renders each
   screen with a test image instead of the webcam, so no private camera frames are ever saved.
4. Manual live test with the webcam: F1 debug HUD shows landmarks, fps, inference ms, gesture state.
