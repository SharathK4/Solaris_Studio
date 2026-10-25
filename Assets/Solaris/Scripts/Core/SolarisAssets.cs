using System;
using System.Collections.Generic;
using TMPro;
using Unity.InferenceEngine;
using UnityEngine;

namespace Solaris
{
    /// <summary>
    /// Single registry of every asset the code-built UI needs. Living in Resources guarantees each
    /// referenced shader, font and model ships in the player. Populated by the editor setup step.
    /// </summary>
    public sealed class SolarisAssets : ScriptableObject
    {
        [Header("Type")]
        public TMP_FontAsset serif;
        public TMP_FontAsset serifItalic;
        public TMP_FontAsset sansLight;
        public TMP_FontAsset sans;
        public TMP_FontAsset sansMedium;
        public TMP_FontAsset garamond;

        [Header("Art")]
        public Texture2D wordmarkSdf;

        [Header("Title key art (Tools/make_title.py)")]
        public Texture2D titlePlate;
        public Texture2D titleDetail;
        public Texture2D titleMotion;
        public Texture2D titleLogoSdf;
        public TextAsset titleMeta;

        [Header("Welcome key art (Tools/make_welcome.py)")]
        public Texture2D welcomePlate;
        public Texture2D welcomeDetail;
        public Texture2D welcomeOrb;
        public TextAsset welcomeMeta;

        [Header("Tutorial key art (Tools/make_tutorials.py)")]
        public Texture2D[] tutorialArt = Array.Empty<Texture2D>();
        public TextAsset[] tutorialMeta = Array.Empty<TextAsset>();

        public Texture2D TutorialTexture(string name)
        {
            foreach (var t in tutorialArt)
                if (t != null && t.name == name) return t;
            return null;
        }

        public TextAsset TutorialMeta(string name)
        {
            foreach (var t in tutorialMeta)
                if (t != null && t.name == name) return t;
            return null;
        }
        public Texture2D[] instruments = Array.Empty<Texture2D>();

        [Header("Shaders")]
        public Shader[] shaders = Array.Empty<Shader>();
        public ComputeShader imageSampler;

        [Header("Vision models")]
        public ModelAsset handDetector;
        public ModelAsset handLandmarker;
        public ModelAsset faceDetector;
        public ModelAsset personSegmenter;
        public TextAsset handAnchors;
        public TextAsset faceAnchors;

        static SolarisAssets s_Instance;
        Dictionary<string, Shader> m_ShaderMap;

        public static SolarisAssets Get
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = Resources.Load<SolarisAssets>("SolarisAssets");
                    if (s_Instance == null)
                        throw new InvalidOperationException("Resources/SolarisAssets.asset missing - run Solaris > Setup Project.");
                }
                return s_Instance;
            }
        }

        public Shader Shader(string name)
        {
            if (m_ShaderMap == null)
            {
                m_ShaderMap = new Dictionary<string, Shader>();
                foreach (var s in shaders)
                    if (s != null) m_ShaderMap[s.name] = s;
            }

            if (m_ShaderMap.TryGetValue(name, out var shader)) return shader;
            shader = UnityEngine.Shader.Find(name);
            if (shader == null) Debug.LogError($"[Solaris] Shader '{name}' is not registered.");
            return shader;
        }

        public static Material Mat(string shaderName) => new Material(Get.Shader(shaderName)) { hideFlags = HideFlags.DontSave };
    }
}
