using System.IO;
using System.Linq;
using TMPro;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;

namespace Solaris.EditorTools
{
    /// <summary>
    /// Idempotent project bootstrap (run from the menu or batch mode):
    /// <c>Unity -batchmode -projectPath . -executeMethod Solaris.EditorTools.ProjectSetup.Run -quit</c>
    /// </summary>
    public static class ProjectSetup
    {
        const string Root = "Assets/Solaris";
        const string Settings = Root + "/Settings";
        const string FontDir = Root + "/Art/Fonts";
        const string ScenePath = Root + "/Scenes/Main.unity";
        const string RegistryPath = Root + "/Resources/SolarisAssets.asset";

        [MenuItem("Solaris/Setup Project")]
        public static void Run()
        {
            EnsureFolder(Settings);
            ImportTmpEssentials();
            ConfigureRenderPipeline();
            ConfigurePlayer();
            ConfigureLogoTexture();
            BuildRegistry();
            BuildScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Solaris] Project setup complete.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void ImportTmpEssentials()
        {
            if (File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset")) return;
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui");
            var pkg = Path.Combine(info.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
            AssetDatabase.ImportPackage(pkg, false);
            AssetDatabase.Refresh();
            Debug.Log("[Solaris] Imported TMP Essential Resources");
        }

        static void ConfigureRenderPipeline()
        {
            string rendererPath = Settings + "/SolarisRenderer.asset";
            string pipelinePath = Settings + "/SolarisURP.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }

            pipeline.supportsHDR = false;
            pipeline.msaaSampleCount = 1;
            pipeline.renderScale = 1f;
            pipeline.supportsCameraDepthTexture = false;
            pipeline.supportsCameraOpaqueTexture = false;
            var so = new SerializedObject(pipeline);
            SetBool(so, "m_MainLightShadowsSupported", false);
            SetBool(so, "m_AdditionalLightShadowsSupported", false);
            SetInt(so, "m_AdditionalLightsRenderingMode", 0);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
                QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;
                QualitySettings.antiAliasing = 0;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        static void SetBool(SerializedObject so, string name, bool v)
        {
            var p = so.FindProperty(name);
            if (p != null) p.boolValue = v;
        }

        static void SetInt(SerializedObject so, string name, int v)
        {
            var p = so.FindProperty(name);
            if (p != null) p.intValue = v;
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Solaris Studio";
            PlayerSettings.productName = "Solaris Studio";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(UnityEditor.Build.NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);

            // Both input backends: new Input System for code, legacy kept for third-party safety.
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (ps != null)
            {
                var so = new SerializedObject(ps);
                var p = so.FindProperty("activeInputHandler");
                if (p != null) p.intValue = 2;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Logo/solaris_icon.png");
            if (icon != null) PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        }

        static void ConfigureLogoTexture()
        {
            ConfigureSdf(Root + "/Art/Logo/solaris_wordmark_sdf.png");
            ConfigureSdf(Root + "/Art/Title/title_logo_sdf.png");
            foreach (var name in new[] { "title_plate", "title_detail", "title_motion" })
                ConfigureData(Root + "/Art/Title/" + name + ".png");
            foreach (var name in new[] { "welcome_plate", "welcome_detail", "welcome_orb" })
                ConfigureData(Root + "/Art/Welcome/" + name + ".png");
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Art/Tutorials" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("_plate")) { ConfigureData(path); continue; }
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.maxTextureSize = 2048;
                ti.SaveAndReimport();
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Art/Instruments" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                if (ti == null) continue;
                ti.textureType = TextureImporterType.Default;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = true;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
        }

        /// <summary>Exact pixel data (no sRGB decode, no compression, no mips), mirrored at the edges.</summary>
        static void ConfigureData(string path)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) return;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;
            ti.mipmapEnabled = false;
            bool rgba = path.EndsWith("title_motion.png") || path.EndsWith("_orb.png");
            ti.alphaSource = rgba ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.wrapMode = path.EndsWith("_orb.png") ? TextureWrapMode.Clamp : TextureWrapMode.Mirror;
            ti.filterMode = FilterMode.Bilinear;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 2048;
            ti.SaveAndReimport();
        }

        static void ConfigureSdf(string path)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) return;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;
            ti.mipmapEnabled = true;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Standalone", overridden = true, format = TextureImporterFormat.R8, maxTextureSize = 4096,
            });
            ti.SaveAndReimport();
        }

        static TMP_FontAsset Font(string file)
        {
            string ttf = $"{FontDir}/{file}.ttf";
            string assetPath = $"{FontDir}/{file} SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
            if (font == null)
            {
                Debug.LogError($"[Solaris] Missing font {ttf}");
                return null;
            }
            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            fa.name = file + " SDF";
            AssetDatabase.CreateAsset(fa, assetPath);
            fa.atlasTextures[0].name = file + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = file + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);

            var so = new SerializedObject(fa);
            var clear = so.FindProperty("m_ClearDynamicDataOnBuild");
            if (clear != null) clear.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            const string chars = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~·—–’‘“”…•×°";
            fa.TryAddCharacters(chars, out _);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return fa;
        }

        static void BuildRegistry()
        {
            var reg = AssetDatabase.LoadAssetAtPath<SolarisAssets>(RegistryPath);
            if (reg == null)
            {
                EnsureFolder(Root + "/Resources");
                reg = ScriptableObject.CreateInstance<SolarisAssets>();
                AssetDatabase.CreateAsset(reg, RegistryPath);
            }

            reg.serif = Font("PlayfairDisplay-Regular");
            reg.serifItalic = Font("PlayfairDisplay-Italic");
            reg.sansLight = Font("Figtree-Light");
            reg.sans = Font("Figtree-Regular");
            reg.sansMedium = Font("Figtree-Medium");
            reg.garamond = Font("EBGaramond-Regular");

            // Fallback chain so any missing glyph still renders in a matching family.
            foreach (var f in new[] { reg.serif, reg.serifItalic, reg.sansLight, reg.sansMedium, reg.garamond })
            {
                if (f == null || reg.sans == null || f == reg.sans) continue;
                f.fallbackFontAssetTable ??= new System.Collections.Generic.List<TMP_FontAsset>();
                if (!f.fallbackFontAssetTable.Contains(reg.sans)) f.fallbackFontAssetTable.Add(reg.sans);
                EditorUtility.SetDirty(f);
            }

            reg.wordmarkSdf = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Logo/solaris_wordmark_sdf.png");
            reg.titlePlate = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Title/title_plate.png");
            reg.titleDetail = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Title/title_detail.png");
            reg.titleMotion = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Title/title_motion.png");
            reg.titleLogoSdf = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Title/title_logo_sdf.png");
            reg.titleMeta = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Art/Title/title_meta.json");
            reg.welcomePlate = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Welcome/welcome_plate.png");
            reg.welcomeDetail = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Welcome/welcome_detail.png");
            reg.welcomeOrb = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Welcome/welcome_orb.png");
            reg.welcomeMeta = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Art/Welcome/welcome_meta.json");
            reg.tutorialArt = AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Art/Tutorials" })
                .Select(g => AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            reg.tutorialMeta = AssetDatabase.FindAssets("t:TextAsset", new[] { Root + "/Art/Tutorials" })
                .Select(g => AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            reg.instruments = AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Art/Instruments" })
                .Select(g => AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g)))
                .OrderBy(t => t.name).ToArray();
            reg.shaders = AssetDatabase.FindAssets("t:Shader", new[] { Root + "/Shaders" })
                .Select(g => AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null).ToArray();
            reg.imageSampler = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Shaders/ImageSample.compute");

            reg.handDetector = AssetDatabase.LoadAssetAtPath<ModelAsset>(Root + "/Models/hand_detector.onnx");
            reg.handLandmarker = AssetDatabase.LoadAssetAtPath<ModelAsset>(Root + "/Models/hand_landmarks_detector.onnx");
            reg.faceDetector = AssetDatabase.LoadAssetAtPath<ModelAsset>(Root + "/Models/blaze_face_short_range.onnx");
            reg.personSegmenter = AssetDatabase.LoadAssetAtPath<ModelAsset>(Root + "/Models/selfie_segmenter_landscape.onnx");
            reg.handAnchors = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Models/hand_anchors.csv");
            reg.faceAnchors = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Models/face_anchors.csv");

            EditorUtility.SetDirty(reg);
            Debug.Log($"[Solaris] Registry: title art {(reg.titlePlate && reg.titleDetail && reg.titleMotion && reg.titleLogoSdf && reg.titleMeta ? "ok" : "MISSING")}, " +
                      $"{reg.shaders.Length} shaders, fonts {(reg.serif && reg.sans ? "ok" : "MISSING")}, " +
                      $"models hand {(reg.handDetector && reg.handLandmarker ? "ok" : "MISSING")} face {(reg.faceDetector ? "ok" : "MISSING")} " +
                      $"seg {(reg.personSegmenter ? "ok" : "MISSING")}, instruments {reg.instruments.Length}");
        }

        static void BuildScene()
        {
            EnsureFolder(Root + "/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Solaris");
            go.AddComponent<App>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
