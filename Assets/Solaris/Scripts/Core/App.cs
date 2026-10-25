using System.Collections;
using System.IO;
using Solaris.Audio;
using Solaris.Diagnostics;
using Solaris.Gestures;
using Solaris.Music;
using Solaris.Rendering;
using Solaris.Vision;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Solaris
{
    /// <summary>What the player has chosen so far; survives screen changes.</summary>
    public sealed class Session
    {
        public Song Song;
        public string Instrument = "Acoustic Guitar";
        public Screens.StageResult LastResult;
    }

    /// <summary>
    /// Composition root. Builds the camera + canvases, owns the long-lived services
    /// (camera feed, vision, gestures, audio, songs) and the screen router.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class App : MonoBehaviour
    {
        public static App Instance { get; private set; }

        public Camera Camera { get; private set; }
        public Canvas Canvas { get; private set; }
        public RectTransform Stage { get; private set; }
        public RectTransform Overlay { get; private set; }
        public ScreenRouter Router { get; private set; }
        public CameraFeed Feed { get; private set; }
        public VisionService Vision { get; private set; }
        public GestureService Gestures { get; private set; }
        public AudioEngine Audio { get; private set; }
        public SongLibrary Songs { get; private set; }
        public GrainOverlay Grain { get; private set; }
        public Session Session { get; } = new Session();

        /// <summary>Canvas-space size of the stage (reference-scaled pixels).</summary>
        public Vector2 CanvasSize => Stage.rect.size;

        void Awake()
        {
            Instance = this;
            ConfigurePlayer();
            BuildRenderingAndUI();

            Songs = new SongLibrary();
            Songs.Load();

            var services = new GameObject("Services");
            services.transform.SetParent(transform, false);
            Feed = services.AddComponent<CameraFeed>();
            Vision = services.AddComponent<VisionService>();
            Vision.Bind(Feed);
            Gestures = services.AddComponent<GestureService>();
            Gestures.Bind(Vision, Feed);
            Audio = services.AddComponent<AudioEngine>();

            var hud = new GameObject("DebugHud", typeof(RectTransform)).AddComponent<DebugHud>();
            hud.Init(this);

            Router = new ScreenRouter(this, Stage);
        }

        void Start()
        {
            if (!string.IsNullOrEmpty(AppArgs.VisionSelfTest))
            {
                gameObject.AddComponent<VisionSelfTest>();
                return;
            }

            if (!string.IsNullOrEmpty(AppArgs.Song))
                Session.Song = Songs.Find(AppArgs.Song);

            Router.Go(ScreenRouter.TryParse(AppArgs.StartScreen, out var id) ? id : ScreenId.Title, 1.4f);

            if (!string.IsNullOrEmpty(AppArgs.CapturePath))
                StartCoroutine(CaptureAndQuit(AppArgs.CapturePath, AppArgs.CaptureDelay));
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || AppArgs.Automation) return;
            if (kb.f11Key.wasPressedThisFrame || (kb.altKey.isPressed && kb.enterKey.wasPressedThisFrame))
                ToggleFullscreen();
        }

        static void ToggleFullscreen()
        {
            if (Screen.fullScreenMode == FullScreenMode.Windowed)
            {
                var r = Screen.currentResolution;
                Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
            }
            else
            {
                Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            }
        }

        /// <summary>Screens declare what they need; everything else is switched off to save GPU.</summary>
        public void ApplyScreenNeeds(AppScreen screen)
        {
            var needs = screen.Vision;
            if ((needs & VisionNeeds.Camera) != 0 && !Feed.IsRunning) Feed.StartCamera();
            if ((needs & VisionNeeds.Hands) == VisionNeeds.Hands) Vision.Warmup();
            Vision.HandsEnabled = (needs & VisionNeeds.Hands) == VisionNeeds.Hands;
            Vision.FaceEnabled = (needs & VisionNeeds.Face) == VisionNeeds.Face;
            Vision.SegmentationEnabled = (needs & VisionNeeds.Segmentation) == VisionNeeds.Segmentation;
            Grain.FadeTo(screen.Grain, screen.GrainLift, 1.2f);
        }

        void ConfigurePlayer()
        {
            Application.runInBackground = true;
            // 240 Hz panels: render every other refresh. 60-144 Hz panels: every refresh.
            double hz = Screen.currentResolution.refreshRateRatio.value;
            QualitySettings.vSyncCount = hz > 160 ? 2 : 1;
            Application.targetFrameRate = -1;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            var cfg = AudioSettings.GetConfiguration();
            if (cfg.dspBufferSize != 256 || cfg.sampleRate != 48000)
            {
                cfg.dspBufferSize = 256;
                cfg.sampleRate = 48000;
                AudioSettings.Reset(cfg);
            }
        }

        void BuildRenderingAndUI()
        {
            var camGo = new GameObject("Camera", typeof(Camera), typeof(AudioListener));
            camGo.transform.SetParent(transform, false);
            camGo.transform.position = new Vector3(0, 0, -10);
            Camera = camGo.GetComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Color.black;
            Camera.fieldOfView = 30f;
            Camera.nearClipPlane = 0.1f;
            Camera.farClipPlane = 100f;
            Camera.allowHDR = false;
            Camera.allowMSAA = false;

            Canvas = CreateCanvas("Canvas", RenderMode.ScreenSpaceCamera, 0);
            Canvas.worldCamera = Camera;
            Canvas.planeDistance = 10f;
            Stage = (RectTransform)Canvas.transform;
            Canvas.gameObject.AddComponent<GraphicRaycaster>();

            var overlayCanvas = CreateCanvas("Overlay", RenderMode.ScreenSpaceOverlay, 100);
            Overlay = (RectTransform)overlayCanvas.transform;
            Grain = new GameObject("Grain", typeof(RectTransform)).AddComponent<GrainOverlay>();
            Grain.transform.SetParent(Overlay, false);

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }
        }

        Canvas CreateCanvas(string name, RenderMode mode, int order)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = mode;
            canvas.sortingOrder = order;
            canvas.pixelPerfect = false;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 |
                                              AdditionalCanvasShaderChannels.TexCoord2 |
                                              AdditionalCanvasShaderChannels.TexCoord3 |
                                              AdditionalCanvasShaderChannels.Normal |
                                              AdditionalCanvasShaderChannels.Tangent;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        IEnumerator CaptureAndQuit(string path, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            yield return new WaitForEndOfFrame();
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Frame sequences (for motion previews): -solaris-capture-frames N -solaris-capture-interval s
            int frames = Mathf.Max(1, (int)AppArgs.GetFloat("-solaris-capture-frames", 1));
            if (frames > 1)
            {
                float interval = AppArgs.GetFloat("-solaris-capture-interval", 0.1f);
                string stem = Path.Combine(dir ?? ".", Path.GetFileNameWithoutExtension(path));
                float t0 = Time.realtimeSinceStartup;
                for (int f = 0; f < frames; f++)
                {
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes($"{stem}_{f:000}.jpg", tex.EncodeToJPG(92));
                    Destroy(tex);
                    yield return new WaitForSecondsRealtime(interval);
                }
                float avg = (Time.realtimeSinceStartup - t0) / frames;
                Debug.Log($"[Solaris] Captured {frames} frames to {stem}_###.jpg, {avg:0.000}s apart ({1f / avg:0.0} fps)");
                Application.Quit();
                yield break;
            }

            ScreenCapture.CaptureScreenshot(Path.GetFullPath(path));
            Debug.Log($"[Solaris] Captured {path} at {Screen.width}x{Screen.height}, {DebugHud.Fps:0} fps");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }
    }
}
