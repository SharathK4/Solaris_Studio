using System;
using Solaris.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Solaris.Screens
{
    /// <summary>
    /// Scene 1: the key art, exactly as designed. The art is rebuilt from layers
    /// (Tools/make_title.py) so only the red glows breathe and drift; the lettering, the grain
    /// and the pale areas stay perfectly still. Nothing else on screen; any key or click continues.
    /// </summary>
    public sealed class TitleScreen : AppScreen
    {
        public override ScreenId Id => ScreenId.Title;
        public override float Grain => 0.035f;   // the art carries its own grain; this only adds native-res crispness

        [Serializable]
        sealed class Meta
        {
            public int imageWidth = 1243, imageHeight = 696;
            public float[] logoMin = { 0.357f, 0.404f }, logoMax = { 0.65f, 0.596f };
            public string logoColor = "FBF4E4";
            public float detailScale = 1.6f;
        }

        Meta m_Meta;
        float m_ImageAspect;
        Material m_ArtMat, m_LogoMat;
        RawImage m_Logo;
        CanvasGroup m_LogoGroup;
        Vector2 m_LaidOutFor;
        float m_T, m_Live;

        protected override void Build()
        {
            var assets = SolarisAssets.Get;
            m_Meta = assets.titleMeta != null ? JsonUtility.FromJson<Meta>(assets.titleMeta.text) : new Meta();
            m_ImageAspect = (float)m_Meta.imageWidth / m_Meta.imageHeight;

            m_ArtMat = SolarisAssets.Mat("Solaris/BG/TitlePlate");
            m_ArtMat.SetTexture("_Plate", assets.titlePlate);
            m_ArtMat.SetTexture("_Detail", assets.titleDetail);
            m_ArtMat.SetTexture("_Motion", assets.titleMotion);
            m_ArtMat.SetFloat("_ImageAspect", m_ImageAspect);
            m_ArtMat.SetFloat("_DetailScale", m_Meta.detailScale);
            m_ArtMat.SetFloat("_Live", 0f);
            var art = UIKit.Raw(Root, Texture2D.whiteTexture, m_ArtMat, "KeyArt");
            art.rectTransform.Stretch();

            m_LogoMat = SolarisAssets.Mat("Solaris/UI/Logo");
            m_LogoMat.SetFloat("_ShadowAlpha", 0f);
            m_LogoMat.SetFloat("_Sweep", -2f);
            m_Logo = UIKit.Raw(Root, assets.titleLogoSdf, m_LogoMat, "SOLARIS STUDIO");
            m_Logo.color = Theme.Hex(m_Meta.logoColor);
            var rt = m_Logo.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            m_LogoGroup = m_Logo.Group();
            Layout();
        }

        /// <summary>
        /// Fit the full image width (or height) to the screen; the other axis extends with mirrored
        /// colour field, so the composition (and the edge light leaks) is identical on 16:9 and 16:10.
        /// </summary>
        void Layout()
        {
            var size = App.CanvasSize;
            if (size.x <= 1 || size.y <= 1) return;
            m_LaidOutFor = size;
            float screenAspect = size.x / size.y;
            var fit = new Vector2(Mathf.Max(1f, screenAspect / m_ImageAspect), Mathf.Max(1f, m_ImageAspect / screenAspect));
            m_ArtMat.SetVector("_Fit", new Vector4(fit.x, fit.y, 0, 0));

            Vector2 ToCanvas(float[] uv) => new Vector2((uv[0] - 0.5f) / fit.x * size.x, (uv[1] - 0.5f) / fit.y * size.y);
            var min = ToCanvas(m_Meta.logoMin);
            var max = ToCanvas(m_Meta.logoMax);
            m_Logo.rectTransform.anchoredPosition = (min + max) * 0.5f;
            m_Logo.rectTransform.sizeDelta = max - min;
        }

        public override void Enter()
        {
            // Opening: the still image arrives, the lettering resolves, then the reds slowly come alive.
            m_LogoGroup.alpha = 0f;
            Anim.Run(2.6f, k =>
            {
                m_LogoGroup.alpha = k;
                m_LogoMat.SetFloat("_Soft", Mathf.Lerp(0.06f, 0f, k));
            }, Ease.InOutSine, 0.6f, null, m_Logo);
            Anim.Run(5f, k => m_Live = k, Ease.InOutSine, 1.2f, null, this);
        }

        protected override void Tick(float dt)
        {
            if (App.CanvasSize != m_LaidOutFor) Layout();
            m_T += dt;
            m_ArtMat.SetFloat("_T", m_T);
            m_ArtMat.SetFloat("_Live", m_Live);

            if (Leaving || Age < 1.0f) return;
            if (AnyKeyToContinue() || Clicked())
            {
                Anim.Run(1.1f, k => { if (m_Logo) m_Logo.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.03f, k); },
                    Ease.OutCubic, 0f, null, m_Logo);
                Go(ScreenId.Welcome);
            }
        }

        static bool AnyKeyToContinue()
        {
            var kb = Keyboard.current;
            if (AppArgs.Automation || kb == null || !kb.anyKey.wasPressedThisFrame) return false;
            // Window/diagnostic shortcuts shouldn't skip the opening.
            if (kb.altKey.isPressed || kb.f1Key.wasPressedThisFrame || kb.f11Key.wasPressedThisFrame) return false;
            return true;
        }

        void OnDestroy()
        {
            if (m_ArtMat != null) Destroy(m_ArtMat);
            if (m_LogoMat != null) Destroy(m_LogoMat);
        }
    }
}
