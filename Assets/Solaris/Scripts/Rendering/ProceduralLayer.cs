using UnityEngine;
using Solaris.UI;
using UnityEngine.UI;

namespace Solaris.Rendering
{
    /// <summary>
    /// Full-screen animated background. The (soft, blurry-by-design) shader is evaluated into a
    /// reduced-resolution half-float RenderTexture and upscaled by the UI, which keeps expensive
    /// domain-warped noise cheap on any GPU. Scale adapts to the quality tier.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class ProceduralLayer : MonoBehaviour
    {
        public Material Material { get; private set; }
        public float ResolutionScale = 0.5f;
        public float TimeScale = 1f;
        public float LocalTime;

        RawImage m_Image;
        RenderTexture m_Target;
        static readonly int k_T = Shader.PropertyToID("_T");
        static readonly int k_Aspect = Shader.PropertyToID("_Aspect");
        static readonly int k_Resolution = Shader.PropertyToID("_Res");

        public static ProceduralLayer Create(Transform parent, string shader, float resolutionScale = 0.5f)
        {
            var img = UIKit.Raw(parent, null, null, shader.Substring(shader.LastIndexOf('/') + 1));
            img.rectTransform.Stretch();
            var layer = img.gameObject.AddComponent<ProceduralLayer>();
            layer.ResolutionScale = resolutionScale;
            layer.Material = SolarisAssets.Mat(shader);
            layer.LocalTime = Random.Range(0f, 40f);
            return layer;
        }

        void Awake() => m_Image = GetComponent<RawImage>();

        void LateUpdate()
        {
            if (Material == null) return;
            int w = Mathf.Max(64, Mathf.RoundToInt(Screen.width * ResolutionScale));
            int h = Mathf.Max(64, Mathf.RoundToInt(Screen.height * ResolutionScale));
            if (m_Target == null || m_Target.width != w || m_Target.height != h)
            {
                if (m_Target != null) m_Target.Release();
                m_Target = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf)
                {
                    name = name + "_RT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
                m_Target.Create();
                m_Image.texture = m_Target;
            }

            LocalTime += Time.unscaledDeltaTime * TimeScale;
            Material.SetFloat(k_T, LocalTime);
            Material.SetFloat(k_Aspect, (float)Screen.width / Screen.height);
            Material.SetVector(k_Resolution, new Vector4(w, h, 1f / w, 1f / h));
            Graphics.Blit(null, m_Target, Material);
        }

        void OnDestroy()
        {
            if (m_Target != null) m_Target.Release();
            if (Material != null) Destroy(Material);
        }
    }
}
