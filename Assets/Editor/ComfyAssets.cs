using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UntitledGame.Core;

namespace UntitledGame.EditorTools
{
    /// <summary>Generates the procedural textures, sprites and materials the game uses.</summary>
    public static class ComfyAssets
    {
        public const string Folder = "Assets/Generated";
        public const string TexFolder = Folder + "/Textures";
        public const string MatFolder = Folder + "/Materials";
        public const string MeshFolder = Folder + "/Meshes";

        public static Material TerrainMat, WaterMat, SkyMat, WoodMat, StoneMat, PlainLitMat;

        public static void EnsureAll()
        {
            Directory.CreateDirectory(TexFolder);
            Directory.CreateDirectory(MatFolder);
            Directory.CreateDirectory(MeshFolder);
            Directory.CreateDirectory("Assets/Resources");

            var softCircle = Tex("soft_circle", 64, (u, v) =>
            {
                float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
                float a = Mathf.Clamp01(1f - d);
                return new Color(1, 1, 1, a * a * (3 - 2 * a));
            });
            var glow = Tex("glow", 64, (u, v) =>
            {
                float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
                float a = Mathf.Exp(-d * d * 5f) * Mathf.Clamp01(1f - d);
                return new Color(1, 1, 1, a);
            });
            var ringTex = Tex("ring", 128, (u, v) =>
            {
                float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
                float a = Mathf.Exp(-Mathf.Pow((d - 0.8f) * 14f, 2f));
                return new Color(1, 1, 1, a);
            });
            var streak = Tex("rain_streak", 32, (u, v) =>
            {
                float a = Mathf.Exp(-Mathf.Pow((u - 0.5f) * 8f, 2f)) * Mathf.Sin(v * Mathf.PI);
                return new Color(1, 1, 1, a);
            });
            var sparkle = Tex("sparkle", 64, (u, v) =>
            {
                Vector2 p = new Vector2(u - 0.5f, v - 0.5f) * 2f;
                float cross = Mathf.Exp(-Mathf.Abs(p.x) * 18f) * Mathf.Exp(-Mathf.Abs(p.y) * 2.5f)
                              + Mathf.Exp(-Mathf.Abs(p.y) * 18f) * Mathf.Exp(-Mathf.Abs(p.x) * 2.5f);
                float core = Mathf.Exp(-p.sqrMagnitude * 20f);
                return new Color(1, 1, 1, Mathf.Clamp01(cross + core));
            });
            var normal = WaterNormalMap();

            // UI sprites.
            var rounded = SpriteTex("ui_rounded", 128, 44, 0f, 44);
            var roundedSmall = SpriteTex("ui_rounded_small", 64, 16, 0f, 18);
            var shadow = SpriteTex("ui_soft_shadow", 128, 40, 18f, 58);
            var circle = SpriteTex("ui_circle", 128, 64, 0f, 0);
            var ringSprite = SpriteRing("ui_ring", 128);
            var fishIcon = FishIconSprite("ui_fish", 128);

            // Materials.
            TerrainMat = Mat("Terrain", "Comfy/Lit", m => { m.SetFloat("_VertexColorWeight", 1f); });
            WoodMat = Mat("DockWood", "Comfy/Lit", m => { m.SetFloat("_VertexColorWeight", 1f); });
            StoneMat = Mat("Stone", "Comfy/Lit", m => { m.SetColor("_BaseColor", new Color(0.62f, 0.6f, 0.58f)); m.SetFloat("_VertexColorWeight", 0f); });
            PlainLitMat = Mat("PlainLit", "Comfy/Lit", m => { m.SetFloat("_VertexColorWeight", 1f); });
            WaterMat = Mat("Water", "Comfy/Water", m => m.SetTexture("_NormalMap", normal));
            SkyMat = Mat("Sky", "Comfy/Sky", null);

            var bobberRed = Mat("BobberRed", "Comfy/Lit", m => { m.SetColor("_BaseColor", new Color(0.92f, 0.2f, 0.18f)); m.SetFloat("_VertexColorWeight", 0f); });
            var bobberWhite = Mat("BobberWhite", "Comfy/Lit", m => { m.SetColor("_BaseColor", new Color(0.97f, 0.95f, 0.9f)); m.SetFloat("_VertexColorWeight", 0f); });
            var rod = Mat("Rod", "Comfy/Lit", m => { m.SetColor("_BaseColor", new Color(0.55f, 0.36f, 0.2f)); m.SetFloat("_VertexColorWeight", 0f); });
            var line = Mat("FishingLine", "Comfy/Particle", m => { m.SetColor("_BaseColor", new Color(0.95f, 0.95f, 0.92f, 0.75f)); Alpha(m); });

            var splash = Mat("FX_Splash", "Comfy/Particle", m => { m.SetTexture("_BaseMap", softCircle); m.SetColor("_BaseColor", new Color(0.9f, 0.97f, 1f, 0.9f)); Alpha(m); });
            var ripple = Mat("FX_Ripple", "Comfy/Particle", m => { m.SetTexture("_BaseMap", ringTex); m.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.6f)); Alpha(m); });
            var firefly = Mat("FX_Firefly", "Comfy/Particle", m => { m.SetTexture("_BaseMap", glow); m.SetColor("_BaseColor", new Color(2.2f, 2.4f, 0.8f, 1f)); Additive(m); m.SetFloat("_FogWeight", 0.3f); });
            var fire = Mat("FX_Fire", "Comfy/Particle", m => { m.SetTexture("_BaseMap", glow); m.SetColor("_BaseColor", new Color(2.4f, 1.1f, 0.35f, 1f)); Additive(m); });
            var smoke = Mat("FX_Smoke", "Comfy/Particle", m => { m.SetTexture("_BaseMap", softCircle); m.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.58f, 0.35f)); Alpha(m); });
            var glowMat = Mat("FX_Glow", "Comfy/Particle", m => { m.SetTexture("_BaseMap", glow); m.SetColor("_BaseColor", new Color(2.0f, 1.3f, 0.6f, 1f)); Additive(m); m.SetFloat("_FogWeight", 0.4f); });
            var rain = Mat("FX_Rain", "Comfy/Particle", m => { m.SetTexture("_BaseMap", streak); m.SetColor("_BaseColor", new Color(0.8f, 0.85f, 0.95f, 0.45f)); Alpha(m); });
            var sparkleMat = Mat("FX_Sparkle", "Comfy/Particle", m => { m.SetTexture("_BaseMap", sparkle); m.SetColor("_BaseColor", new Color(2.5f, 2.2f, 1.4f, 1f)); Additive(m); });

            var fishMat = Mat("Fish", "Comfy/Fish", m =>
            {
                var palette = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ThirdParty/Kenney/SurvivalKit/Textures/colormap.png");
                m.SetTexture("_BaseMap", palette);
            });

            // Runtime asset registry.
            var ga = AssetDatabase.LoadAssetAtPath<GameAssets>("Assets/Resources/GameAssets.asset");
            if (ga == null)
            {
                ga = ScriptableObject.CreateInstance<GameAssets>();
                AssetDatabase.CreateAsset(ga, "Assets/Resources/GameAssets.asset");
            }
            ga.fishSmallModel = RemappedPrefab("SurvivalKit/fish");
            ga.fishLargeModel = RemappedPrefab("SurvivalKit/fish-large");
            ga.bottleModel = RemappedPrefab("PirateKit/bottle");
            ga.teacupModel = RemappedPrefab("FoodKit/cup-tea");
            ga.driftwoodModel = RemappedPrefab("NatureKit/log");
            ga.fishMaterial = fishMat;
            ga.bobberRed = bobberRed;
            ga.bobberWhite = bobberWhite;
            ga.rodMaterial = rod;
            ga.lineMaterial = line;
            ga.splashMaterial = splash;
            ga.rippleMaterial = ripple;
            ga.fireflyMaterial = firefly;
            ga.fireMaterial = fire;
            ga.smokeMaterial = smoke;
            ga.glowMaterial = glowMat;
            ga.rainMaterial = rain;
            ga.sparkleMaterial = sparkleMat;
            ga.roundedRect = rounded;
            ga.roundedRectSmall = roundedSmall;
            ga.softShadow = shadow;
            ga.circle = circle;
            ga.ring = ringSprite;
            ga.fishIcon = fishIcon;
            EditorUtility.SetDirty(ga);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Saves a prefab of a Kenney model with our remapped materials (for runtime spawning).</summary>
        public static GameObject RemappedPrefab(string kitPath)
        {
            var src = Model(kitPath);
            if (src == null) return null;
            Directory.CreateDirectory(Folder + "/Prefabs");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            KenneyMaterials.Remap(inst, KenneyMaterials.KitOf(kitPath));
            string path = $"{Folder}/Prefabs/{kitPath.Replace('/', '_')}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(inst, path);
            UnityEngine.Object.DestroyImmediate(inst);
            return prefab;
        }

        public static GameObject Model(string kitPath) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ThirdParty/Kenney/{kitPath}.fbx");

        private static void Alpha(Material m)
        {
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        private static void Additive(Material m)
        {
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
        }

        private static Material Mat(string name, string shaderName, Action<Material> setup)
        {
            string path = $"{MatFolder}/{name}.mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new Exception($"Shader not found: {shaderName}");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.enableInstancing = true;
            setup?.Invoke(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Texture2D Tex(string name, int size, Func<float, float, Color> f, bool linear = false)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, linear);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                t.SetPixel(x, y, f((x + 0.5f) / size, (y + 0.5f) / size));
            t.Apply();
            return SavePng(t, $"{TexFolder}/{name}.png", ti =>
            {
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = true;
            });
        }

        private static Texture2D SavePng(Texture2D t, string path, Action<TextureImporter> configure)
        {
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            configure?.Invoke(ti);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D WaterNormalMap()
        {
            const int size = 256;
            var rng = new System.Random(7);
            var waves = new (int kx, int ky, float phase, float amp)[28];
            for (int i = 0; i < waves.Length; i++)
            {
                int kx = rng.Next(-7, 8), ky = rng.Next(-7, 8);
                if (kx == 0 && ky == 0) kx = 1;
                float freq = Mathf.Sqrt(kx * kx + ky * ky);
                waves[i] = (kx, ky, (float)rng.NextDouble() * Mathf.PI * 2f, 1f / (freq * 0.8f + 0.5f));
            }
            float[,] h = new float[size, size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size * Mathf.PI * 2f, v = (float)y / size * Mathf.PI * 2f;
                float s = 0;
                foreach (var w in waves) s += Mathf.Sin(w.kx * u + w.ky * v + w.phase) * w.amp;
                h[x, y] = s;
            }
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = h[(x + 1) % size, y] - h[(x - 1 + size) % size, y];
                float dy = h[x, (y + 1) % size] - h[x, (y - 1 + size) % size];
                Vector3 n = new Vector3(-dx * 1.2f, -dy * 1.2f, 1f).normalized;
                float foamNoise = Mathf.InverseLerp(-2.5f, 2.5f, h[x, y]);
                t.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, foamNoise));
            }
            t.Apply();
            // Stored as a plain linear texture: R/G hold the normal xy, alpha holds a noise value the
            // shader reuses for foam. UnpackNormal on a Default texture uses the RGB path on desktop.
            return SavePng(t, $"{TexFolder}/water_normal.png", ti =>
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.mipmapEnabled = true;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
            });
        }

        private static float RoundedRectSdf(Vector2 p, Vector2 half, float r)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + new Vector2(r, r);
            return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - r;
        }

        private static Sprite SpriteTex(string name, int size, float radius, float blur, int border)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;
            float inset = blur > 0 ? blur : 0f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                float d = RoundedRectSdf(p, new Vector2(half - inset, half - inset), Mathf.Min(radius, half - inset));
                float a = blur > 0 ? Mathf.Clamp01(1f - (d + blur) / (blur * 2f)) : Mathf.Clamp01(0.5f - d);
                if (blur > 0) a = a * a * (3 - 2 * a) * 0.55f;
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            t.Apply();
            string path = $"{TexFolder}/{name}.png";
            SavePng(t, path, ti =>
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spriteBorder = new Vector4(border, border, border, border);
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
            });
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Sprite SpriteRing(string name, int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude;
                float a = Mathf.Clamp01(0.5f - Mathf.Abs(d - (half - 8f)) + 5f);
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            t.Apply();
            string path = $"{TexFolder}/{name}.png";
            SavePng(t, path, ti =>
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
            });
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Sprite FishIconSprite(string name, int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                // Body ellipse.
                Vector2 b = new Vector2((u - 0.45f) / 0.33f, (v - 0.5f) / 0.2f);
                float body = 1f - b.magnitude;
                // Tail triangle.
                float tx = (u - 0.72f) / 0.2f;
                float tail = (tx > 0 && tx < 1) ? (tx * 0.22f - Mathf.Abs(v - 0.5f)) * 20f : -1f;
                // Eye hole.
                float eye = Vector2.Distance(new Vector2(u, v), new Vector2(0.28f, 0.54f)) - 0.035f;
                float a = Mathf.Clamp01(Mathf.Max(body * 25f, tail));
                if (eye < 0) a *= 0.15f;
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            t.Apply();
            string path = $"{TexFolder}/{name}.png";
            SavePng(t, path, ti =>
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
            });
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
