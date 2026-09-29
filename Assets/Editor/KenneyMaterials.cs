using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UntitledGame.EditorTools
{
    /// <summary>
    /// Swaps the materials Unity imports from Kenney FBXs for our stylised Comfy/Lit ones.
    /// Flat-coloured kits (Nature Kit) get a warmer, greener "cozy" palette; texture-atlas kits keep
    /// their colormap. One shared material per (kit, source material) keeps batching efficient.
    /// </summary>
    public static class KenneyMaterials
    {
        private const string Folder = ComfyAssets.MatFolder + "/Kenney";
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        private static readonly Dictionary<string, string> NaturePalette = new Dictionary<string, string>
        {
            { "leafsGreen", "#6DB35C" },
            { "leafsDark", "#3F8C57" },
            { "leafsFall", "#EFA04C" },
            { "grass", "#7FBA57" },
            { "dirt", "#B98759" },
            { "dirtDark", "#9A6C48" },
            { "woodBark", "#8C5F3D" },
            { "woodBarkDark", "#6F4B34" },
            { "wood", "#C68B5B" },
            { "woodDark", "#9C6843" },
            { "woodInner", "#E9CFA6" },
            { "woodBirch", "#EFE6D6" },
            { "stone", "#B8B4AA" },
            { "stoneDark", "#908C83" },
            { "colorRed", "#E0605A" },
            { "colorRedDark", "#B84A45" },
            { "colorYellow", "#F6C453" },
            { "colorPurple", "#A68CE0" },
            { "colorTan", "#E8B07A" },
            { "colorWhite", "#F7F2EA" },
            { "_defaultMat", "#F2EEE6" },
        };

        public static void ClearCache() => Cache.Clear();

        public static string KitOf(string kitPath) => kitPath.Contains("/") ? kitPath.Substring(0, kitPath.IndexOf('/')) : kitPath;

        public static void Remap(GameObject go, string kit)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    mats[i] = Get(kit, mats[i]);
                }
                r.sharedMaterials = mats;
            }
        }

        public static Material Get(string kit, Material source)
        {
            string key = $"{kit}_{source.name}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            Directory.CreateDirectory(Folder);
            string path = $"{Folder}/{key}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Comfy/Lit")) { name = key };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Comfy/Lit");
            m.enableInstancing = true;
            m.SetFloat("_VertexColorWeight", 0f);

            Texture tex = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
            if (tex == null && source.HasProperty("_MainTex")) tex = source.GetTexture("_MainTex");
            if (tex != null)
            {
                m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", Color.white);
            }
            else
            {
                m.SetTexture("_BaseMap", null);
                Color c;
                if (NaturePalette.TryGetValue(source.name, out string hex) && ColorUtility.TryParseHtmlString(hex, out c)) { }
                else
                {
                    // Unknown flat colour: undo Unity's linear->gamma brightening of the FBX value.
                    c = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor").linear : Color.white;
                }
                m.SetColor("_BaseColor", c);
            }
            EditorUtility.SetDirty(m);
            Cache[key] = m;
            return m;
        }
    }
}
