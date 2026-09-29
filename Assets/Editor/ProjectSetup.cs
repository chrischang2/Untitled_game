using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UntitledGame.EditorTools
{
    /// <summary>
    /// One-shot project configuration: URP pipeline asset, TMP essentials, player settings.
    /// Safe to re-run. Invoke from the menu or batch mode:
    ///   Unity.exe -batchmode -projectPath . -executeMethod UntitledGame.EditorTools.ProjectSetup.Run -quit
    /// </summary>
    public static class ProjectSetup
    {
        public const string SettingsFolder = "Assets/Settings";
        public const string UrpAssetPath = SettingsFolder + "/ComfyURP.asset";
        public const string RendererPath = SettingsFolder + "/ComfyURP_Renderer.asset";

        [MenuItem("Untitled Game/Setup/Configure Project")]
        public static void Run()
        {
            ImportTmpEssentials();
            ConfigureUrp();
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] Done.");
        }

        private static void ImportTmpEssentials()
        {
            if (Directory.Exists("Assets/TextMesh Pro")) return;
            string[] candidates =
            {
                "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage",
                "Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage",
            };
            foreach (var c in candidates)
            {
                string full = Path.GetFullPath(c);
                if (!File.Exists(full)) continue;
                // AssetDatabase.ImportPackage is asynchronous and never finishes in batch mode,
                // so unpack the .unitypackage (a tar.gz of guid/{pathname,asset,asset.meta}) ourselves.
                int n = ExtractUnityPackage(full);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log($"[ProjectSetup] Extracted {n} TMP essential assets from {c}");
                return;
            }
            Debug.LogWarning("[ProjectSetup] TMP Essential Resources package not found.");
        }

        private static int ExtractUnityPackage(string packagePath)
        {
            var entries = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, byte[]>>();
            using (var gz = new System.IO.Compression.GZipStream(File.OpenRead(packagePath), System.IO.Compression.CompressionMode.Decompress))
            {
                var header = new byte[512];
                while (ReadFully(gz, header) && header[0] != 0)
                {
                    string name = Encoding.ASCII.GetString(header, 0, 100).TrimEnd('\0');
                    string prefix = Encoding.ASCII.GetString(header, 345, 155).TrimEnd('\0');
                    if (prefix.Length > 0) name = prefix + "/" + name;
                    long size = System.Convert.ToInt64(Encoding.ASCII.GetString(header, 124, 12).Trim('\0', ' '), 8);
                    char type = (char)header[156];
                    var data = new byte[size];
                    ReadFully(gz, data);
                    long pad = (512 - size % 512) % 512;
                    if (pad > 0) ReadFully(gz, new byte[pad]);
                    if (type != '0' && type != '\0') continue; // directories, pax headers
                    var parts = name.TrimStart('.', '/').Split('/');
                    if (parts.Length != 2) continue;
                    if (!entries.TryGetValue(parts[0], out var files)) entries[parts[0]] = files = new System.Collections.Generic.Dictionary<string, byte[]>();
                    files[parts[1]] = data;
                }
            }
            int count = 0;
            foreach (var files in entries.Values)
            {
                if (!files.TryGetValue("pathname", out var pn)) continue;
                string path = Encoding.UTF8.GetString(pn).Split('\n')[0].Trim();
                if (files.TryGetValue("asset", out var asset))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, asset);
                }
                else Directory.CreateDirectory(path);
                if (files.TryGetValue("asset.meta", out var meta)) File.WriteAllBytes(path + ".meta", meta);
                count++;
            }
            return count;
        }

        private static bool ReadFully(Stream s, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int r = s.Read(buffer, read, buffer.Length - read);
                if (r <= 0) return false;
                read += r;
            }
            return true;
        }

        private static void ConfigureUrp()
        {
            Directory.CreateDirectory(SettingsFolder);

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            var rso = new SerializedObject(renderer);
            var ppd = rso.FindProperty("postProcessData");
            if (ppd != null && ppd.objectReferenceValue == null)
            {
                ppd.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Object>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            }
            SetInt(rso, "m_RenderingMode", 0); // Forward
            SetInt(rso, "m_CopyDepthMode", 0); // AfterOpaques: water samples scene depth during the transparent pass
            SetInt(rso, "m_DepthPrimingMode", 0);
            rso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);

            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(urp, UrpAssetPath);
            }

            var so = new SerializedObject(urp);
            SetBool(so, "m_SupportsHDR", true);
            SetInt(so, "m_MSAA", 4);
            SetFloat(so, "m_RenderScale", 1f);
            SetBool(so, "m_RequireDepthTexture", true);
            SetBool(so, "m_RequireOpaqueTexture", false);
            SetBool(so, "m_MainLightShadowsSupported", true);
            SetInt(so, "m_MainLightShadowmapResolution", 2048);
            SetInt(so, "m_AdditionalLightsRenderingMode", 1); // per pixel
            SetInt(so, "m_AdditionalLightsPerObjectLimit", 8);
            SetBool(so, "m_AdditionalLightShadowsSupported", false);
            SetFloat(so, "m_ShadowDistance", 70f);
            SetInt(so, "m_ShadowCascadeCount", 2);
            SetFloat(so, "m_Cascade2Split", 0.3f);
            SetBool(so, "m_SoftShadowsSupported", true);
            SetInt(so, "m_SoftShadowQuality", 2);
            SetFloat(so, "m_ShadowDepthBias", 1.0f);
            SetFloat(so, "m_ShadowNormalBias", 1.0f);
            SetBool(so, "m_UseSRPBatcher", true);
            SetBool(so, "m_SupportsDynamicBatching", false);
            SetInt(so, "m_ColorGradingMode", 1); // HDR grading
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(current, false);
            Debug.Log($"[ProjectSetup] URP configured ({UrpAssetPath}).");
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Cozy Local Games";
            PlayerSettings.productName = "Willow Lake";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.SetApiCompatibilityLevel(UnityEditor.Build.NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Unity_4_8);
        }

        private static void SetBool(SerializedObject so, string name, bool v)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[ProjectSetup] Missing property {name}"); return; }
            p.boolValue = v;
        }

        private static void SetInt(SerializedObject so, string name, int v)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[ProjectSetup] Missing property {name}"); return; }
            p.intValue = v; // enum properties: sets the underlying enum value, not the index
        }

        private static void SetFloat(SerializedObject so, string name, float v)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[ProjectSetup] Missing property {name}"); return; }
            p.floatValue = v;
        }

        /// <summary>Writes bounds + animation clip names for every Kenney model (used to pick world scales).</summary>
        [MenuItem("Untitled Game/Setup/Dump Model Info")]
        public static void DumpModelInfo()
        {
            var sb = new StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/ThirdParty/Kenney" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var inst = Object.Instantiate(go);
                var rs = inst.GetComponentsInChildren<Renderer>();
                Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds();
                foreach (var r in rs) b.Encapsulate(r.bounds);
                var anim = inst.GetComponent<Animation>();
                string clips = anim != null ? string.Join(",", anim.Cast<AnimationState>().Select(s => s.name)) : "";
                string mats = string.Join(",", rs.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct());
                sb.AppendLine($"{path.Replace("Assets/ThirdParty/Kenney/", "")}\tsize={b.size.x:F2}x{b.size.y:F2}x{b.size.z:F2}\tcenter={b.center.x:F2},{b.center.y:F2},{b.center.z:F2}\tmats={mats}\tclips={clips}");
                Object.DestroyImmediate(inst);
            }
            Directory.CreateDirectory("Captures");
            File.WriteAllText("Captures/model_info.txt", sb.ToString());
            Debug.Log("[ProjectSetup] Wrote Captures/model_info.txt");
        }
    }
}
