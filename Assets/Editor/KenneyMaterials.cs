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
    /// A style swaps in a regional palette and a recoloured copy of a kit's colormap, so the same models dress each
    /// region on the bus route: "desert" (sun-baked; canopies saffron and indigo), "snow" (frosted; canopies white
    /// and red) and "mars" (rust-red rock, purple alien plants; canopies white and orange like a space station).
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

        /// <summary>The desert's flat colours: sandstone rocks, dry grass, dusty leaves.</summary>
        private static readonly Dictionary<string, string> DesertPalette = new Dictionary<string, string>
        {
            { "stone", "#D8A574" },
            { "stoneDark", "#B98157" },
            { "grass", "#BDB46A" },
            { "leafsGreen", "#7FA34E" },
            { "leafsDark", "#5C8A45" },
            { "dirt", "#CC9C64" },
            { "dirtDark", "#A97A4C" },
        };

        /// <summary>The snow's flat colours: frosted grass, cold grey stone, dark winter leaves.</summary>
        private static readonly Dictionary<string, string> SnowPalette = new Dictionary<string, string>
        {
            { "stone", "#AEB8C2" },
            { "stoneDark", "#8A95A1" },
            { "grass", "#D3DEE4" },
            { "leafsGreen", "#4D7D5E" },
            { "leafsDark", "#3A6650" },
            { "leafsFall", "#9A6E4C" },
            { "dirt", "#BCC3C8" },
            { "dirtDark", "#969EA5" },
        };

        /// <summary>Mars: rust-red rock and dust, purple alien leaves, glowing cyan and violet mushroom caps.</summary>
        private static readonly Dictionary<string, string> MarsPalette = new Dictionary<string, string>
        {
            { "stone", "#B5583A" },
            { "stoneDark", "#7A3326" },
            { "grass", "#9C5AB4" },
            { "leafsGreen", "#8A52C8" },
            { "leafsDark", "#5E3596" },
            { "leafsFall", "#D86AB0" },
            { "dirt", "#B9623A" },
            { "dirtDark", "#84402A" },
            { "colorRed", "#4FE0D2" },
            { "colorRedDark", "#2FB4AA" },
            { "colorTan", "#B48CFF" },
        };

        private static Dictionary<string, string> PaletteFor(string style) =>
            style == "desert" ? DesertPalette : style == "snow" ? SnowPalette : style == "mars" ? MarsPalette : null;

        public static void ClearCache() => Cache.Clear();

        public static string KitOf(string kitPath) => kitPath.Contains("/") ? kitPath.Substring(0, kitPath.IndexOf('/')) : kitPath;

        public static void Remap(GameObject go, string kit, string style = null)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    mats[i] = Get(kit, mats[i], style);
                }
                r.sharedMaterials = mats;
            }
        }

        public static Material Get(string kit, Material source, string style = null)
        {
            string key = string.IsNullOrEmpty(style) ? $"{kit}_{source.name}" : $"{kit}_{style}_{source.name}";
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
                if (style == "desert" || style == "snow" || style == "mars") tex = RegionAtlas(kit, tex, style);
                m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", Color.white);
            }
            else
            {
                m.SetTexture("_BaseMap", null);
                Color c;
                if (PaletteFor(style) != null && PaletteFor(style).TryGetValue(source.name, out string dhex) && ColorUtility.TryParseHtmlString(dhex, out c)) { }
                else if (NaturePalette.TryGetValue(source.name, out string hex) && ColorUtility.TryParseHtmlString(hex, out c)) { }
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

        /// <summary>
        /// A regional copy of a kit's colormap. Desert: teal and green swatches become saffron and terracotta, reds
        /// become indigo. Snow: teal and green become snowy white (reds stay red). Wood, skin, greys and purples stay as
        /// they are. Saved next to the other generated textures (made once).
        /// </summary>
        private static Texture RegionAtlas(string kit, Texture source, string style)
        {
            string srcPath = AssetDatabase.GetAssetPath(source);
            string path = $"{ComfyAssets.TexFolder}/{kit}_colormap_{style}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath)) return source;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(srcPath));
            var px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                Color.RGBToHSV(px[i], out float h, out float sat, out float v);
                if (sat < 0.25f) continue;
                if (style == "mars")
                {
                    if (h > 0.22f && h < 0.56f)
                    {
                        sat *= 0.08f;            // greens and teals: space-station white
                        h = 0.6f;
                        v = Mathf.Clamp01(0.8f + v * 0.18f);
                    }
                    else if ((h > 0.94f || h < 0.025f) && sat > 0.45f)
                    {
                        h = 0.065f;              // reds: safety orange
                        sat = Mathf.Clamp01(sat * 1.05f);
                    }
                    else continue;
                }
                else if (style == "snow")
                {
                    if (!(h > 0.22f && h < 0.56f)) continue;
                    sat *= 0.1f;                 // greens and teals: snowy white with a hint of blue
                    h = 0.58f;
                    v = Mathf.Clamp01(0.82f + v * 0.16f);
                }
                else if (h > 0.22f && h < 0.56f)
                {
                    // Greens and teals: saffron (teal) to terracotta (green).
                    float k = Mathf.InverseLerp(0.22f, 0.56f, h);
                    h = Mathf.Lerp(0.035f, 0.11f, k);
                    sat = Mathf.Clamp01(sat * 1.05f + 0.1f);
                    v = Mathf.Clamp01(v * 1.05f);
                }
                else if ((h > 0.94f || h < 0.025f) && sat > 0.45f)
                {
                    h = 0.63f;              // reds: deep indigo
                    sat *= 0.85f;
                    v *= 0.8f;
                }
                else continue;
                var c = Color.HSVToRGB(h, sat, v);
                c.a = px[i].a;
                px[i] = c;
            }
            tex.SetPixels(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter dst && AssetImporter.GetAtPath(srcPath) is TextureImporter src)
            {
                dst.filterMode = src.filterMode;
                dst.mipmapEnabled = src.mipmapEnabled;
                dst.sRGBTexture = src.sRGBTexture;
                dst.textureCompression = TextureImporterCompression.Uncompressed;
                dst.wrapMode = src.wrapMode;
                dst.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path) ?? source;
        }
    }
}
