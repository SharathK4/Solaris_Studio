using NUnit.Framework;
using Solaris.UI;
using TMPro;
using UnityEngine;

namespace Solaris.Tests
{
    /// <summary>The reference-matching layout relies on measuring real glyph ink, not TMP's layout boxes.</summary>
    public class TextFitTests
    {
        GameObject m_Canvas;
        TextMeshProUGUI m_Text;

        [SetUp]
        public void SetUp()
        {
            m_Canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(m_Canvas.transform, false);
            m_Text = go.AddComponent<TextMeshProUGUI>();
            m_Text.font = SolarisAssets.Get.sans;
            m_Text.fontSize = 100;
            m_Text.textWrappingMode = TextWrappingModes.NoWrap;
            m_Text.rectTransform.sizeDelta = new Vector2(2000, 400);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(m_Canvas);

        [Test]
        public void InkOfACapitalSpansCapHeightNotAscender()
        {
            m_Text.text = "H";
            m_Text.ForceMeshUpdate();
            var ink = RefSpace.Ink(m_Text);
            float baseline = m_Text.textInfo.lineInfo[0].baseline;
            var face = m_Text.font.faceInfo;
            float cap = face.capLine * face.scale * (100f / face.pointSize);
            Assert.That(ink.yMax - baseline, Is.EqualTo(cap).Within(cap * 0.04f), "cap height");
            Assert.That(ink.yMin, Is.EqualTo(baseline).Within(1.5f), "H sits on the baseline");
            Assert.That(ink.yMax, Is.LessThan(m_Text.textBounds.max.y - 5f), "ink top is below TMP's ascender bound");
            Assert.That(ink.width, Is.LessThan(m_Text.textBounds.size.x), "ink excludes side bearings");
        }

        [Test]
        public void FitLineSpansRequestedInkAndBaseline()
        {
            m_Text.text = "A bright and happy";
            var rs = new RefSpace(1000, 500, new Vector2(1000, 500));     // 1 canvas unit per px
            rs.FitLine(m_Text, 100, 400, 250);
            m_Text.ForceMeshUpdate();
            var ink = RefSpace.Ink(m_Text);
            var pos = m_Text.rectTransform.anchoredPosition;
            Assert.That(ink.width, Is.EqualTo(300f).Within(1.5f), "ink width");
            Assert.That(pos.x + ink.xMin, Is.EqualTo(rs.Point(100, 0).x).Within(1f), "left ink edge");
            Assert.That(pos.y + m_Text.textInfo.lineInfo[0].baseline, Is.EqualTo(rs.Point(0, 250).y).Within(0.5f), "baseline");
        }

        [Test]
        public void FitHeightMatchesCapitalHeight()
        {
            m_Text.text = "C";
            var rs = new RefSpace(1000, 500, new Vector2(1000, 500));
            rs.FitHeight(m_Text, 200, 240);
            m_Text.ForceMeshUpdate();
            float h = RefSpace.Ink(m_Text).yMax - m_Text.textInfo.lineInfo[0].baseline;
            Assert.That(h, Is.EqualTo(40f).Within(1f));
        }
    }
}
