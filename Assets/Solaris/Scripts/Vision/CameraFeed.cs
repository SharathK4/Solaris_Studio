using System.Collections;
using System.IO;
using UnityEngine;

namespace Solaris.Vision
{
    /// <summary>
    /// Webcam (or still-image test source) normalised into one upright RenderTexture per new frame.
    /// The raw frame is NOT mirrored; mirroring happens at display/geometry time.
    /// </summary>
    public sealed class CameraFeed : MonoBehaviour
    {
        public RenderTexture Frame { get; private set; }
        public int FrameIndex { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsStarting { get; private set; }
        public string DeviceName { get; private set; } = "";
        public string Error { get; private set; }
        public bool Mirror = true;
        public float MeasuredFps { get; private set; }

        public int Width => Frame != null ? Frame.width : 1920;
        public int Height => Frame != null ? Frame.height : 1080;
        public float Aspect => (float)Width / Height;

        WebCamTexture m_Cam;
        Texture2D m_Still;
        float m_StillClock, m_FpsClock;
        int m_FpsFrames, m_DeviceIndex = -1;

        public void StartCamera()
        {
            if (IsRunning || IsStarting) return;
            Error = null;
            if (!string.IsNullOrEmpty(AppArgs.CameraImage)) StartStill(AppArgs.CameraImage);
            else if (AppArgs.Automation) Error = "Webcam disabled during automated runs";   // never record the user unattended
            else StartCoroutine(OpenWebcam(PickDevice()));
        }

        public void StopCamera()
        {
            if (m_Cam != null)
            {
                m_Cam.Stop();
                Destroy(m_Cam);
                m_Cam = null;
            }
            IsRunning = false;
        }

        /// <summary>Switch to the next physical camera (C key in the debug HUD).</summary>
        public void CycleDevice()
        {
            var devices = WebCamTexture.devices;
            if (devices.Length < 2 || m_Still != null) return;
            StopCamera();
            m_DeviceIndex = (m_DeviceIndex + 1) % devices.Length;
            StartCoroutine(OpenWebcam(m_DeviceIndex));
        }

        int PickDevice()
        {
            var devices = WebCamTexture.devices;
            if (devices.Length == 0) return -1;
            string wanted = AppArgs.CameraName;
            int best = 0, bestScore = int.MinValue;
            for (int i = 0; i < devices.Length; i++)
            {
                string n = devices[i].name;
                string l = n.ToLowerInvariant();
                int score = 0;
                if (!string.IsNullOrEmpty(wanted) && l.Contains(wanted.ToLowerInvariant())) score += 1000;
                // Laptops expose the infrared (Windows Hello) sensor as a second webcam - never use it.
                if (l.Contains(" ir ") || l.StartsWith("ir ") || l.Contains("infrared") || l.Contains("ir uvc")) score -= 500;
                if (l.Contains("fhd") || l.Contains("hd")) score += 20;
                if (devices[i].isFrontFacing) score += 5;
                if (l.Contains("virtual") || l.Contains("obs")) score -= 50;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            m_DeviceIndex = best;
            return best;
        }

        IEnumerator OpenWebcam(int index)
        {
            IsStarting = true;
            var devices = WebCamTexture.devices;
            if (index < 0 || index >= devices.Length)
            {
                Error = "No camera found";
                IsStarting = false;
                yield break;
            }

            DeviceName = devices[index].name;
            m_Cam = new WebCamTexture(DeviceName, 1920, 1080, 30);
            m_Cam.Play();

            float timeout = Time.realtimeSinceStartup + 8f;
            while (m_Cam.width <= 16 && Time.realtimeSinceStartup < timeout)
                yield return null;

            if (!m_Cam.isPlaying || m_Cam.width <= 16)
            {
                Error = "Camera unavailable (check Windows privacy settings)";
                StopCamera();
                IsStarting = false;
                yield break;
            }

            EnsureFrame(m_Cam.width, m_Cam.height);
            IsRunning = true;
            IsStarting = false;
            Debug.Log($"[Solaris] Camera '{DeviceName}' {m_Cam.width}x{m_Cam.height} (requested 1920x1080@30)");
        }

        /// <summary>Switch the feed to a still image (tests / self-test).</summary>
        public void UseStill(string path)
        {
            StopCamera();
            m_Still = null;
            Error = null;
            StartStill(path);
        }

        void StartStill(string path)
        {
            if (!File.Exists(path))
            {
                Error = "Test image not found: " + path;
                return;
            }
            m_Still = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            m_Still.LoadImage(File.ReadAllBytes(path));
            DeviceName = "Still: " + Path.GetFileName(path);
            EnsureFrame(1920, 1080);
            BlitStill();
            IsRunning = true;
        }

        void EnsureFrame(int w, int h)
        {
            if (Frame != null && Frame.width == w && Frame.height == h) return;
            if (Frame != null) Frame.Release();
            // sRGB storage keeps 8-bit precision where the eye needs it; ML sampling converts back.
            Frame = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "CameraFrame",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
            };
            Frame.Create();
        }

        void BlitStill()
        {
            // "Cover" fit the still into the 16:9 frame, as a webcam would deliver it.
            float srcAspect = (float)m_Still.width / m_Still.height;
            float dstAspect = (float)Frame.width / Frame.height;
            Vector2 scale = Vector2.one, offset = Vector2.zero;
            if (srcAspect > dstAspect)
            {
                scale.x = dstAspect / srcAspect;
                offset.x = (1f - scale.x) * 0.5f;
            }
            else
            {
                scale.y = srcAspect / dstAspect;
                offset.y = (1f - scale.y) * 0.5f;
            }
            Graphics.Blit(m_Still, Frame, scale, offset);
        }

        void Update()
        {
            bool fresh = false;
            if (m_Cam != null && IsRunning && m_Cam.didUpdateThisFrame)
            {
                if (m_Cam.width != Frame.width || m_Cam.height != Frame.height) EnsureFrame(m_Cam.width, m_Cam.height);
                if (m_Cam.videoVerticallyMirrored) Graphics.Blit(m_Cam, Frame, new Vector2(1, -1), new Vector2(0, 1));
                else Graphics.Blit(m_Cam, Frame);
                fresh = true;
            }
            else if (m_Still != null && IsRunning)
            {
                m_StillClock += Time.unscaledDeltaTime;
                if (m_StillClock >= 1f / 30f)
                {
                    m_StillClock = 0f;
                    fresh = true;   // same pixels, but exercise the pipeline at camera rate
                }
            }

            if (fresh)
            {
                FrameIndex++;
                m_FpsFrames++;
            }

            m_FpsClock += Time.unscaledDeltaTime;
            if (m_FpsClock >= 1f)
            {
                MeasuredFps = m_FpsFrames / m_FpsClock;
                m_FpsFrames = 0;
                m_FpsClock = 0f;
            }
        }

        /// <summary>
        /// UV scale/offset that "cover"-fits the (mirrored) frame onto a viewport of the given aspect.
        /// uv_frame = uv_screen * scale + offset.
        /// </summary>
        public Vector4 CoverTransform(float viewAspect)
        {
            float a = Aspect;
            if (viewAspect < a)
            {
                float s = viewAspect / a;
                return new Vector4(s, 1f, (1f - s) * 0.5f, 0f);
            }
            float t = a / viewAspect;
            return new Vector4(1f, t, 0f, (1f - t) * 0.5f);
        }

        /// <summary>Mirrored frame UV (what the user sees) -> viewport UV under cover fit.</summary>
        public Vector2 FrameToView(Vector2 uv, float viewAspect)
        {
            var c = CoverTransform(viewAspect);
            return new Vector2((uv.x - c.z) / c.x, (uv.y - c.w) / c.y);
        }

        void OnDestroy()
        {
            StopCamera();
            if (Frame != null) Frame.Release();
        }
    }
}
