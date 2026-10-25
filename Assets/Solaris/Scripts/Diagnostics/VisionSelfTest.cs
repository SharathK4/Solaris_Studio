using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Solaris.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Solaris.Diagnostics
{
    /// <summary>
    /// <c>-solaris-selftest-vision &lt;dir&gt; [-solaris-capture &lt;outdir&gt;]</c>: runs the full tracking
    /// pipeline on every image in a folder (as if it were the webcam), logs hands / finger counts /
    /// face, saves an annotated screenshot per image, writes a summary and quits.
    /// </summary>
    public sealed class VisionSelfTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var app = App.Instance;
            string dir = AppArgs.VisionSelfTest;
            string outDir = AppArgs.CapturePath ?? Path.Combine(dir, "out");
            Directory.CreateDirectory(outDir);
            var files = Directory.GetFiles(dir).Where(f => f.EndsWith(".jpg") || f.EndsWith(".png") || f.EndsWith(".jpeg")).OrderBy(f => f).ToArray();
            var report = new StringBuilder();
            report.AppendLine($"Solaris vision self-test — {SystemInfo.graphicsDeviceName}");

            var view = UIKit.Raw(app.Stage, null, SolarisAssets.Mat("Solaris/CameraView"), "SelfTestView");
            view.rectTransform.Stretch();
            DebugHud.Visible = true;

            app.Vision.Warmup();
            while (!app.Vision.IsReady && app.Vision.LoadError == null) yield return null;
            if (app.Vision.LoadError != null)
            {
                Debug.LogError("[SelfTest] " + app.Vision.LoadError);
                Application.Quit(2);
                yield break;
            }

            app.Vision.HandsEnabled = true;
            app.Vision.FaceEnabled = true;
            app.Vision.SegmentationEnabled = true;

            foreach (var file in files)
            {
                app.Feed.UseStill(file);
                view.texture = app.Feed.Frame;
                var viewMat = view.material;
                foreach (var h in app.Vision.Hands) h.Lose();

                // Let tracking converge (detector -> landmarks -> tracking ROI refinement).
                float until = Time.realtimeSinceStartup + 2.0f;
                while (Time.realtimeSinceStartup < until)
                {
                    viewMat.SetVector("_Cover", app.Feed.CoverTransform((float)Screen.width / Screen.height));
                    if (app.Vision.PersonMask != null) viewMat.SetTexture("_Mask", app.Vision.PersonMask);
                    yield return null;
                }

                var g = app.Gestures;
                var line = new StringBuilder();
                line.Append($"{Path.GetFileName(file)}: hands={app.Vision.VisibleHands} vision={app.Vision.IterationMs:0.0}ms ");
                foreach (var h in g.Hands)
                {
                    if (!h.Visible) continue;
                    var p = h.Pose;
                    string role = h == g.ChordHand ? "chord" : h == g.ExpressionHand ? "expr" : "?";
                    line.Append($"[#{h.Slot} {role} presence={h.Track.Presence:0.00} right={h.Track.RightHanded:0.00} count={p.Count} " +
                                $"ext=T{p.Extension[0]:0.00},I{p.Extension[1]:0.00},M{p.Extension[2]:0.00},R{p.Extension[3]:0.00},P{p.Extension[4]:0.00} " +
                                $"open={p.Openness:0.00} roll={p.Roll * Mathf.Rad2Deg:0}] ");
                }
                line.Append($"fingers={g.Fingers} face={(app.Vision.Face.Visible ? $"({app.Vision.Face.Center.x:0.00},{app.Vision.Face.Center.y:0.00})" : "none")}");
                report.AppendLine(line.ToString());
                Debug.Log("[SelfTest] " + line);

                yield return new WaitForEndOfFrame();
                string shot = Path.GetFullPath(Path.Combine(outDir, Path.GetFileNameWithoutExtension(file) + "_vision.png"));
                ScreenCapture.CaptureScreenshot(shot);
                yield return null;
                yield return null;
            }

            File.WriteAllText(Path.Combine(outDir, "vision_report.txt"), report.ToString());
            Debug.Log("[SelfTest] done\n" + report);
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }
    }
}
