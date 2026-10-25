using NUnit.Framework;
using Solaris.Gestures;
using Solaris.UI;
using Solaris.Vision;
using UnityEngine;

namespace Solaris.Tests
{
    public class GestureTests
    {
        /// <summary>
        /// Closed loop: the 3D tutorial rig poses a hand, we convert it to MediaPipe-style landmarks
        /// (z toward camera is negative), and the tracker's analyzer must read the same finger count.
        /// </summary>
        static Vector3[] Landmarks(HandShape shape, Vector3 euler, float scale = 0.18f, bool mirror = false)
        {
            var joints = new Vector4[21];
            HandRig.Solve(shape, joints);
            var rot = Quaternion.Euler(euler);
            var pts = new Vector3[21];
            for (int i = 0; i < 21; i++)
            {
                var p = rot * (Vector3)joints[i];
                if (mirror) p.x = -p.x;
                pts[i] = new Vector3(0.9f + p.x * scale, 0.35f + p.y * scale, -p.z * scale);
            }
            return pts;
        }

        static int Count(HandShape shape, Vector3 euler, bool mirror = false)
        {
            var pose = new HandPose();
            var pts = Landmarks(shape, euler, 0.18f, mirror);
            // A few frames so hysteresis settles.
            for (int i = 0; i < 3; i++) pose.Analyze(pts);
            return pose.Count;
        }

        [Test]
        public void CountsChordGestures_Upright([Values(0, 1, 2, 3, 4, 5)] int fingers)
        {
            Assert.AreEqual(fingers, Count(HandShape.Fingers(fingers), Vector3.zero));
        }

        [Test]
        public void CountsChordGestures_Tilted([Values(1, 2, 3, 4, 5)] int fingers,
            [Values(-30f, 30f)] float roll, [Values(-25f, 25f)] float yaw)
        {
            Assert.AreEqual(fingers, Count(HandShape.Fingers(fingers), new Vector3(0, yaw, roll)));
        }

        [Test]
        public void CountsChordGestures_LeftHand([Values(1, 2, 3, 4, 5)] int fingers)
        {
            Assert.AreEqual(fingers, Count(HandShape.Fingers(fingers), Vector3.zero, true));
        }

        [Test]
        public void LooseThumbDoesNotChangeLowCounts([Values(1, 2, 3)] int fingers)
        {
            var shape = HandShape.Fingers(fingers);
            shape.ThumbFold = 0f;
            shape.Thumb = 0f;           // thumb sticking out, as people often do
            Assert.AreEqual(fingers, Count(shape, Vector3.zero));
        }

        [Test]
        public void Openness_SeparatesOpenFromCurled()
        {
            var open = new HandPose();
            open.Analyze(Landmarks(HandShape.Fingers(5), Vector3.zero));
            var curled = new HandPose();
            curled.Analyze(Landmarks(HandShape.Curled, Vector3.zero));
            Assert.Greater(open.Openness, 0.7f);
            Assert.Less(curled.Openness, 0.4f);
        }

        [Test]
        public void Roll_FollowsTilt()
        {
            var pose = new HandPose();
            pose.Analyze(Landmarks(HandShape.Fingers(5), new Vector3(0, 0, -30f)));
            Assert.Greater(pose.Roll * Mathf.Rad2Deg, 15f, "tilting clockwise leans the hand to the right");
            pose.Analyze(Landmarks(HandShape.Fingers(5), new Vector3(0, 0, 30f)));
            Assert.Less(pose.Roll * Mathf.Rad2Deg, -15f);
        }

        [Test]
        public void Pinch_DetectsThumbIndexContact()
        {
            var pts = Landmarks(HandShape.Fingers(5), Vector3.zero);
            var pose = new HandPose();
            pose.Analyze(pts);
            Assert.IsFalse(pose.Pinching);
            pts[Lm.ThumbTip] = pts[Lm.IndexTip] + new Vector3(0.004f, 0, 0);
            pose.Analyze(pts);
            Assert.IsTrue(pose.Pinching);
        }

        static float Circle(bool clockwise, int loops = 2, float radius = 0.12f, float noise = 0f)
        {
            var c = new CircleGesture();
            var rng = new System.Random(3);
            const float dt = 1f / 30f;
            int steps = 36 * loops;
            for (int i = 0; i <= steps; i++)
            {
                float a = i * Mathf.PI * 2f / 36f * (clockwise ? -1f : 1f);
                var p = new Vector2(0.8f + Mathf.Cos(a) * radius, 0.5f + Mathf.Sin(a) * radius);
                p += new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * noise;
                c.Update(p, dt);
            }
            return c.Total;
        }

        [Test]
        public void Circle_ClockwiseIsPositive() => Assert.That(Circle(true), Is.EqualTo(4 * Mathf.PI).Within(1.2f));

        [Test]
        public void Circle_CounterClockwiseIsNegative() => Assert.That(Circle(false), Is.EqualTo(-4 * Mathf.PI).Within(1.2f));

        [Test]
        public void Circle_StraightLineDoesNotTurn()
        {
            var c = new CircleGesture();
            for (int i = 0; i < 60; i++) c.Update(new Vector2(0.2f + i * 0.01f, 0.5f), 1f / 30f);
            Assert.That(c.Total, Is.EqualTo(0).Within(0.05f));
        }

        [Test]
        public void Circle_JitterAtRestIsIgnored()
        {
            var c = new CircleGesture();
            var rng = new System.Random(9);
            for (int i = 0; i < 120; i++)
                c.Update(new Vector2(0.5f, 0.5f) + new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()) * 0.002f, 1f / 30f);
            Assert.That(Mathf.Abs(c.Total), Is.LessThan(0.3f));
        }
    }

    public class VisionMathTests
    {
        [Test]
        public void RotatedCrop_MapsCentreAndUp()
        {
            var up = new Vector2(1, 1).normalized;
            var m = Affine.RotatedCrop(new Vector2(400, 300), 224f, up, 224);
            Assert.That(Vector2.Distance(m.Apply(new Vector2(112, 112)), new Vector2(400, 300)), Is.LessThan(1e-3f));
            // Tensor "up" (decreasing row) must map to the requested up direction.
            var top = m.Apply(new Vector2(112, 0)) - new Vector2(400, 300);
            Assert.That(Vector2.Dot(top.normalized, up), Is.GreaterThan(0.999f));
            Assert.That(m.Scale, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Letterbox_CentresTheFrame()
        {
            var m = Affine.Letterbox(1920, 1080, 192);
            Assert.That(Vector2.Distance(m.Apply(new Vector2(96, 96)), new Vector2(960, 540)), Is.LessThan(1e-2f));
            // Tensor top-left is above the frame (letterbox band), y-up texture space.
            Assert.That(m.Apply(Vector2.zero).y, Is.EqualTo(540 + 960).Within(1e-2f));
        }

        [Test]
        public void OneEuro_SmoothsJitterButFollowsMotion()
        {
            var f = new OneEuro();
            var rng = new System.Random(1);
            float maxDev = 0;
            for (int i = 0; i < 90; i++)
            {
                float v = f.Filter(0.5f + ((float)rng.NextDouble() - 0.5f) * 0.01f, 1f / 30f, 1.4f, 30f);
                if (i > 10) maxDev = Mathf.Max(maxDev, Mathf.Abs(v - 0.5f));
            }
            Assert.Less(maxDev, 0.004f, "jitter should be damped");
            float last = 0;
            for (int i = 0; i < 30; i++) last = f.Filter(0.5f + i * 0.02f, 1f / 30f, 1.4f, 30f);
            Assert.That(last, Is.EqualTo(0.5f + 29 * 0.02f).Within(0.05f), "fast motion should not lag much");
        }
    }
}
