using System;
using System.Collections;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;

namespace Solaris.Vision
{
    /// <summary>
    /// Real-time vision on the GPU (Sentis): two-hand tracking, face centre, person mask.
    ///
    /// Per new camera frame:
    ///  1. Each tracked hand runs the landmark model on a rotated ROI predicted from its previous
    ///     landmarks (MediaPipe "tracking" mode, so the palm detector is skipped while hands are held).
    ///  2. While fewer than two hands are tracked, the palm detector runs on the letterboxed frame;
    ///     TopK + sigmoid happen on the GPU, anchor decode + NMS on the CPU for a handful of candidates.
    ///  3. Face detection (every other frame) and person segmentation (straight into a RenderTexture)
    ///     run only when the current screen asks for them.
    /// All readbacks are async, so the render loop never stalls on the GPU.
    /// </summary>
    public sealed class VisionService : MonoBehaviour
    {
        const int PalmSize = 192, LandmarkSize = 224, FaceSize = 128;
        const int PalmAnchors = 2016, FaceAnchors = 896, TopK = 6;
        const float PalmThreshold = 0.55f, PresenceThreshold = 0.5f, NmsIoU = 0.3f;

        public bool HandsEnabled, FaceEnabled, SegmentationEnabled;
        public bool IsReady { get; private set; }
        public bool IsLoading { get; private set; }
        public string LoadError { get; private set; }

        public readonly HandTrack[] Hands = { new HandTrack(0), new HandTrack(1) };
        public FaceInfo Face;
        public RenderTexture PersonMask { get; private set; }

        public float IterationMs { get; private set; }
        public float Rate { get; private set; }
        public int Iterations { get; private set; }
        public int SegmenterWidth { get; private set; } = 256;
        public int SegmenterHeight { get; private set; } = 144;

        /// <summary>Raised on the main thread after each processed frame.</summary>
        public event Action Updated;

        CameraFeed m_Feed;
        Worker m_Palm, m_Face, m_Seg;
        readonly Worker[] m_Landmark = new Worker[2];
        Tensor<float> m_PalmIn, m_FaceIn, m_SegIn;
        readonly Tensor<float>[] m_LandmarkIn = new Tensor<float>[2];
        float[,] m_PalmAnchors, m_FaceAnchors;
        string m_LmName, m_PresenceName, m_HandednessName;
        bool m_SegNhwc = true;
        int m_LastFrame = -1, m_NextId = 1;
        float m_RateClock;
        int m_RateCount;
        float m_LastIterationTime;

        struct Candidate
        {
            public float Score;
            public Rect Box;       // tensor space, for NMS
            public Roi Roi;        // texture space
        }

        readonly List<Candidate> m_Candidates = new List<Candidate>(TopK);

        public void Bind(CameraFeed feed) => m_Feed = feed;

        public int VisibleHands
        {
            get
            {
                int n = 0;
                foreach (var h in Hands) if (h.Visible) n++;
                return n;
            }
        }

        /// <summary>Loads and compiles the models, spread over a few frames. Safe to call repeatedly.</summary>
        public void Warmup()
        {
            if (IsReady || IsLoading) return;
            StartCoroutine(Load());
        }

        IEnumerator Load()
        {
            IsLoading = true;
            var a = SolarisAssets.Get;
            var backend = SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU;
            Model palm = null, landmarks = null, face = null, seg = null;
            try
            {
                m_PalmAnchors = VisionMath.LoadAnchors(a.handAnchors.text, PalmAnchors);
                m_FaceAnchors = VisionMath.LoadAnchors(a.faceAnchors.text, FaceAnchors);

                // Palm detector + GPU post-process: sigmoid(TopK(scores)) and the matching boxes.
                var raw = ModelLoader.Load(a.handDetector);
                var g = new FunctionalGraph();
                var input = g.AddInput(raw, 0);
                var outs = Functional.Forward(raw, input);
                var scores = outs[1].Reshape(new[] { PalmAnchors });
                var top = Functional.TopK(scores, TopK, 0, true, true);
                var boxes = Functional.IndexSelect(outs[0], 1, top[1]);
                palm = g.Compile(Functional.Sigmoid(top[0]), top[1], boxes);
            }
            catch (Exception e)
            {
                Fail("palm detector", e);
                yield break;
            }
            yield return null;

            try
            {
                landmarks = ModelLoader.Load(a.handLandmarker);
                m_LmName = landmarks.outputs[0].name;
                m_PresenceName = landmarks.outputs.Count > 1 ? landmarks.outputs[1].name : null;
                m_HandednessName = landmarks.outputs.Count > 2 ? landmarks.outputs[2].name : null;
                foreach (var o in landmarks.outputs)
                {
                    if (o.name == "Identity") m_LmName = o.name;
                    if (o.name == "Identity_1") m_PresenceName = o.name;
                    if (o.name == "Identity_2") m_HandednessName = o.name;
                }
            }
            catch (Exception e)
            {
                Fail("hand landmarker", e);
                yield break;
            }
            yield return null;

            try
            {
                var rawFace = ModelLoader.Load(a.faceDetector);
                var g = new FunctionalGraph();
                var input = g.AddInput(rawFace, 0);
                var outs = Functional.Forward(rawFace, 2 * input - 1);
                var scores = outs[1].Reshape(new[] { FaceAnchors });
                var top = Functional.TopK(scores, 1, 0, true, true);
                var boxes = Functional.IndexSelect(outs[0], 1, top[1]);
                face = g.Compile(Functional.Sigmoid(top[0]), top[1], boxes);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Solaris] Face detector unavailable: {e.Message}");
            }
            yield return null;

            try
            {
                if (a.personSegmenter != null)
                {
                    seg = ModelLoader.Load(a.personSegmenter);
                    var shape = seg.inputs[0].shape;
                    // Accept NHWC (1,H,W,3, TFLite style) or NCHW (1,3,H,W, ONNX style).
                    if (shape.rank == 4 && shape.Get(3) == 3)
                    {
                        m_SegNhwc = true;
                        SegmenterHeight = shape.Get(1);
                        SegmenterWidth = shape.Get(2);
                    }
                    else if (shape.rank == 4 && shape.Get(1) == 3)
                    {
                        m_SegNhwc = false;
                        SegmenterHeight = shape.Get(2);
                        SegmenterWidth = shape.Get(3);
                    }
                    Debug.Log($"[Solaris] Segmenter input {shape} -> {SegmenterWidth}x{SegmenterHeight} ({(m_SegNhwc ? "NHWC" : "NCHW")})");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Solaris] Person segmenter unavailable: {e.Message}");
                seg = null;
            }
            yield return null;

            try
            {
                m_Palm = new Worker(palm, backend);
                for (int i = 0; i < 2; i++)
                {
                    m_Landmark[i] = new Worker(landmarks, backend);
                    m_LandmarkIn[i] = new Tensor<float>(new TensorShape(1, LandmarkSize, LandmarkSize, 3));
                }
                m_PalmIn = new Tensor<float>(new TensorShape(1, PalmSize, PalmSize, 3));
                if (face != null)
                {
                    m_Face = new Worker(face, backend);
                    m_FaceIn = new Tensor<float>(new TensorShape(1, FaceSize, FaceSize, 3));
                }
                if (seg != null)
                {
                    m_Seg = new Worker(seg, backend);
                    m_SegIn = new Tensor<float>(m_SegNhwc ? new TensorShape(1, SegmenterHeight, SegmenterWidth, 3) : new TensorShape(1, 3, SegmenterHeight, SegmenterWidth));
                    PersonMask = new RenderTexture(SegmenterWidth, SegmenterHeight, 0, RenderTextureFormat.RHalf)
                    {
                        name = "PersonMask",
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                    PersonMask.Create();
                }
            }
            catch (Exception e)
            {
                Fail("workers", e);
                yield break;
            }

            IsLoading = false;
            IsReady = true;
            Debug.Log($"[Solaris] Vision ready on {backend} ({SystemInfo.graphicsDeviceName}); landmarks '{m_LmName}', presence '{m_PresenceName}', handedness '{m_HandednessName}'");
            RunLoop();
        }

        void Fail(string what, Exception e)
        {
            LoadError = $"Hand tracking failed to start ({what})";
            IsLoading = false;
            Debug.LogError($"[Solaris] {LoadError}: {e}");
        }

        async void RunLoop()
        {
            var token = destroyCancellationToken;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    bool any = HandsEnabled || FaceEnabled || SegmentationEnabled;
                    if (!any || m_Feed == null || !m_Feed.IsRunning || m_Feed.FrameIndex == m_LastFrame)
                    {
                        if (!HandsEnabled) foreach (var h in Hands) h.Lose();
                        await Awaitable.NextFrameAsync(token);
                        continue;
                    }

                    m_LastFrame = m_Feed.FrameIndex;
                    float t0 = Time.realtimeSinceStartup;
                    await Process(m_Feed.Frame);
                    IterationMs = (Time.realtimeSinceStartup - t0) * 1000f;
                    Iterations++;
                    m_RateCount++;
                    Updated?.Invoke();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogError($"[Solaris] Vision loop crashed: {e}");
            }
        }

        async Awaitable Process(Texture frame)
        {
            int w = frame.width, h = frame.height;
            float now = Time.realtimeSinceStartup;
            float dt = m_LastIterationTime > 0 ? Mathf.Clamp(now - m_LastIterationTime, 1e-3f, 0.2f) : 1f / 30f;
            m_LastIterationTime = now;

            // ---- schedule everything first, then await readbacks together ----
            var lmAffine = new Affine[2];
            var lmPending = new Awaitable<Tensor<float>>[2];
            var presPending = new Awaitable<Tensor<float>>[2];
            var handPending = new Awaitable<Tensor<float>>[2];
            if (HandsEnabled)
            {
                for (int s = 0; s < 2; s++)
                {
                    var hand = Hands[s];
                    if (!hand.Tracking) continue;
                    lmAffine[s] = Affine.RotatedCrop(hand.Roi.Center, hand.Roi.Size, hand.Roi.Up, LandmarkSize);
                    VisionMath.Sample(frame, m_LandmarkIn[s], lmAffine[s]);
                    m_Landmark[s].Schedule(m_LandmarkIn[s]);
                    lmPending[s] = (m_Landmark[s].PeekOutput(m_LmName) as Tensor<float>).ReadbackAndCloneAsync();
                    if (m_PresenceName != null)
                        presPending[s] = (m_Landmark[s].PeekOutput(m_PresenceName) as Tensor<float>).ReadbackAndCloneAsync();
                    if (m_HandednessName != null)
                        handPending[s] = (m_Landmark[s].PeekOutput(m_HandednessName) as Tensor<float>).ReadbackAndCloneAsync();
                }
            }

            bool runPalm = HandsEnabled && (!Hands[0].Tracking || !Hands[1].Tracking);
            var palmAffine = Affine.Letterbox(w, h, PalmSize);
            Awaitable<Tensor<float>> palmScores = null, palmBoxes = null;
            Awaitable<Tensor<int>> palmIdx = null;
            if (runPalm)
            {
                VisionMath.Sample(frame, m_PalmIn, palmAffine);
                m_Palm.Schedule(m_PalmIn);
                palmScores = (m_Palm.PeekOutput(0) as Tensor<float>).ReadbackAndCloneAsync();
                palmIdx = (m_Palm.PeekOutput(1) as Tensor<int>).ReadbackAndCloneAsync();
                palmBoxes = (m_Palm.PeekOutput(2) as Tensor<float>).ReadbackAndCloneAsync();
            }

            bool runFace = FaceEnabled && m_Face != null && (Iterations % 2 == 0);
            var faceAffine = Affine.Letterbox(w, h, FaceSize);
            Awaitable<Tensor<float>> faceScore = null, faceBox = null;
            Awaitable<Tensor<int>> faceIdx = null;
            if (runFace)
            {
                VisionMath.Sample(frame, m_FaceIn, faceAffine);
                m_Face.Schedule(m_FaceIn);
                faceScore = (m_Face.PeekOutput(0) as Tensor<float>).ReadbackAndCloneAsync();
                faceIdx = (m_Face.PeekOutput(1) as Tensor<int>).ReadbackAndCloneAsync();
                faceBox = (m_Face.PeekOutput(2) as Tensor<float>).ReadbackAndCloneAsync();
            }

            if (SegmentationEnabled && m_Seg != null)
            {
                VisionMath.Sample(frame, m_SegIn, Affine.Stretch(w, h, SegmenterWidth, SegmenterHeight), 1f, 0f, !m_SegNhwc);
                m_Seg.Schedule(m_SegIn);
                var mask = m_Seg.PeekOutput(0) as Tensor<float>;
                var tt = new TextureTransform().SetCoordOrigin(CoordOrigin.TopLeft);
                if (m_SegNhwc) tt = tt.SetTensorLayout(TensorLayout.NHWC);
                TextureConverter.RenderToTexture(mask, PersonMask, tt);
            }

            // ---- collect: landmarks ----
            for (int s = 0; s < 2; s++)
            {
                if (lmPending[s] == null) continue;
                using var lm = await lmPending[s];
                float presence = 1f;
                if (presPending[s] != null)
                {
                    using var p = await presPending[s];
                    presence = ToProbability(p[0]);
                }
                float handed = 0.5f;
                if (handPending[s] != null)
                {
                    using var hd = await handPending[s];
                    handed = ToProbability(hd[0]);
                }

                var hand = Hands[s];
                if (!hand.Tracking) continue;   // lost while awaiting
                hand.Presence = presence;
                if (presence < PresenceThreshold)
                {
                    hand.Lose();
                    continue;
                }

                DecodeLandmarks(hand, lm, lmAffine[s]);
                hand.RightHanded = handed;
                hand.UpdateView(w, h, m_Feed.Mirror, dt);
                hand.Visible = true;
                hand.LastSeen = now;
                hand.Roi = hand.RoiFromLandmarks();
                // Degenerate ROI (hand leaving frame or collapsed) -> drop and let the detector re-acquire.
                if (hand.Roi.Size < 24f || hand.Roi.Size > 3f * Mathf.Max(w, h)) hand.Lose();
            }

            // Two slots locked onto the same hand? Keep the more confident one.
            if (Hands[0].Visible && Hands[1].Visible)
            {
                float d = (Hands[0].Roi.Center - Hands[1].Roi.Center).magnitude;
                if (d < 0.35f * Mathf.Min(Hands[0].Roi.Size, Hands[1].Roi.Size))
                    (Hands[0].Presence >= Hands[1].Presence ? Hands[1] : Hands[0]).Lose();
            }

            // ---- collect: palm detections -> new tracks ----
            if (runPalm)
            {
                using var sc = await palmScores;
                using var ix = await palmIdx;
                using var bx = await palmBoxes;
                DecodePalms(sc, ix, bx, palmAffine);
                foreach (var c in m_Candidates)
                {
                    if (Overlaps(c.Roi)) continue;
                    var free = !Hands[0].Tracking ? Hands[0] : !Hands[1].Tracking ? Hands[1] : null;
                    if (free == null) break;
                    free.Acquire(c.Roi, m_NextId++);
                }
            }

            // ---- collect: face ----
            if (runFace)
            {
                using var fs = await faceScore;
                using var fi = await faceIdx;
                using var fb = await faceBox;
                DecodeFace(fs, fi, fb, faceAffine, w, h, now);
            }
            else if (FaceEnabled && now - Face.LastSeen > 0.6f)
            {
                Face.Visible = false;
            }

            // If nothing was awaited (e.g. only segmentation), yield so the main thread breathes.
            if (lmPending[0] == null && lmPending[1] == null && !runPalm && !runFace)
                await Awaitable.NextFrameAsync();

            if (now - m_RateClock > 1f)
            {
                Rate = m_RateCount / (now - m_RateClock);
                m_RateClock = now;
                m_RateCount = 0;
            }
        }

        static void DecodeLandmarks(HandTrack hand, Tensor<float> lm, Affine m)
        {
            float zScale = m.Scale;
            var span = lm.AsReadOnlySpan();
            for (int i = 0; i < Lm.Count; i++)
            {
                var px = m.Apply(new Vector2(span[3 * i], span[3 * i + 1]));
                hand.Pixel[i] = new Vector3(px.x, px.y, span[3 * i + 2] * zScale);
            }
        }

        // Some exports bake the sigmoid in, others output logits.
        static float ToProbability(float v) => v < 0f || v > 1f ? VisionMath.Sigmoid(v) : v;

        bool Overlaps(Roi roi)
        {
            foreach (var h in Hands)
            {
                if (!h.Tracking) continue;
                float d = (h.Roi.Center - roi.Center).magnitude;
                if (d < 0.45f * Mathf.Max(h.Roi.Size, roi.Size)) return true;
            }
            return false;
        }

        void DecodePalms(Tensor<float> scores, Tensor<int> indices, Tensor<float> boxes, Affine m)
        {
            m_Candidates.Clear();
            int n = scores.shape.length;
            for (int k = 0; k < n; k++)
            {
                float score = scores[k];
                if (score < PalmThreshold) continue;
                int idx = indices[k];
                var anchor = new Vector2(m_PalmAnchors[idx, 0], m_PalmAnchors[idx, 1]) * PalmSize;
                var center = anchor + new Vector2(boxes[0, k, 0], boxes[0, k, 1]);
                float size = Mathf.Max(boxes[0, k, 2], boxes[0, k, 3]);
                var kp0 = anchor + new Vector2(boxes[0, k, 4], boxes[0, k, 5]);   // wrist
                var kp2 = anchor + new Vector2(boxes[0, k, 8], boxes[0, k, 9]);   // middle-finger MCP

                var cTex = m.Apply(center);
                var upTex = (m.Apply(kp2) - m.Apply(kp0)).normalized;
                float sizeTex = size * m.Scale;
                var roi = new Roi
                {
                    Center = cTex + upTex * (0.5f * sizeTex),
                    Size = sizeTex * 2.6f,
                    Up = upTex.sqrMagnitude > 0 ? upTex : Vector2.up,
                };
                var box = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);

                bool suppressed = false;
                foreach (var c in m_Candidates)
                    if (VisionMath.IoU(c.Box, box) > NmsIoU) { suppressed = true; break; }
                if (!suppressed) m_Candidates.Add(new Candidate { Score = score, Box = box, Roi = roi });
            }
        }

        void DecodeFace(Tensor<float> score, Tensor<int> index, Tensor<float> box, Affine m, int w, int h, float now)
        {
            float s = score[0];
            if (s < 0.5f)
            {
                if (now - Face.LastSeen > 0.6f) Face.Visible = false;
                return;
            }
            int idx = index[0];
            var anchor = new Vector2(m_FaceAnchors[idx, 0], m_FaceAnchors[idx, 1]) * FaceSize;
            var c = m.Apply(anchor + new Vector2(box[0, 0, 0], box[0, 0, 1]));
            var corner = m.Apply(anchor + new Vector2(box[0, 0, 0] + 0.5f * box[0, 0, 2], box[0, 0, 1] + 0.5f * box[0, 0, 3]));
            var size = 2f * new Vector2(Mathf.Abs(corner.x - c.x), Mathf.Abs(corner.y - c.y));

            var uv = new Vector2(c.x / w, c.y / h);
            if (m_Feed.Mirror) uv.x = 1f - uv.x;
            var sz = new Vector2(size.x / w, size.y / h);
            float k = Face.Visible ? 0.35f : 1f;          // light smoothing between detections
            Face.Center = Vector2.Lerp(Face.Center, uv, k);
            Face.Size = Vector2.Lerp(Face.Size, sz, k);
            Face.Score = s;
            Face.Visible = true;
            Face.LastSeen = now;
        }

        void OnDestroy()
        {
            m_Palm?.Dispose();
            m_Face?.Dispose();
            m_Seg?.Dispose();
            for (int i = 0; i < 2; i++)
            {
                m_Landmark[i]?.Dispose();
                m_LandmarkIn[i]?.Dispose();
            }
            m_PalmIn?.Dispose();
            m_FaceIn?.Dispose();
            m_SegIn?.Dispose();
            if (PersonMask != null) PersonMask.Release();
        }
    }
}
