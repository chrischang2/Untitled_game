using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UntitledGame.CameraControl;
using UntitledGame.Companion;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Environment;
using UntitledGame.Fishing;
using UntitledGame.GenAI;
using UntitledGame.Home;
using UntitledGame.Player;
using UntitledGame.Progression;
using UntitledGame.UI;

namespace UntitledGame.EditorTools
{
    /// <summary>
    /// Generates the Willow Lake scene from scratch: terrain, lake, dock, camp, forest, characters,
    /// lighting, post-processing and all runtime systems. Deterministic (fixed seed) and re-runnable.
    ///   Menu: Untitled Game/Build Willow Lake Scene
    ///   Batch: -executeMethod UntitledGame.EditorTools.SceneBuilder.Build
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/WillowLake.unity";
        private const float CharacterScale = 1.4f;

        private static System.Random _rng;
        private static Transform _env;

        private static float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        private static T Pick<T>(IList<T> list) => list[_rng.Next(list.Count)];

        [MenuItem("Untitled Game/Build Willow Lake Scene")]
        public static void Build()
        {
            KenneyMaterials.ClearCache();
            ComfyAssets.EnsureAll();
            _rng = new System.Random(20260929);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _env = new GameObject("Environment").transform;

            var sun = CreateSun();
            var dayNight = new GameObject("DayNight").AddComponent<DayNightCycle>();
            var dso = new SerializedObject(dayNight);
            dso.FindProperty("sun").objectReferenceValue = sun;
            dso.FindProperty("skyMaterial").objectReferenceValue = ComfyAssets.SkyMat;
            dso.ApplyModifiedPropertiesWithoutUndo();
            dayNight.ResetPalette();
            RenderSettings.skybox = ComfyAssets.SkyMat;
            RenderSettings.sun = sun;

            BuildTerrain();
            BuildWater();
            BuildDock(out Vector3 dockEnd, out Quaternion dockRot);
            BuildCamp(out Vector3 fireSpot);
            BuildIsland();
            ScatterNature(fireSpot);

            var systems = new GameObject("Systems").transform;
            var cam = BuildCamera();
            var player = BuildPlayer(cam);
            var mei = BuildMei(player, out var brain, out var voice);
            BuildMarket(player);
            BuildCritters(dockEnd, dockRot, player);
            BuildPostProcessing();
            BuildSystems(systems, dayNight, player, mei, brain, voice, cam);

            dayNight.TimeOfDay = 7.5f;
            dayNight.Apply();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"[SceneBuilder] Built {ScenePath}");
        }

        // ------------------------------------------------------------------ lighting

        private static Light CreateSun()
        {
            var go = new GameObject("Sun");
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.85f;
            l.intensity = 1.2f;
            l.color = new Color(1f, 0.95f, 0.88f);
            go.transform.rotation = Quaternion.Euler(40f, -60f, 0f);
            var data = go.AddComponent<UniversalAdditionalLightData>();
            data.usePipelineSettings = true;
            return l;
        }

        // ------------------------------------------------------------------ terrain

        private static Color C(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        private static readonly Color GrassA = C("#86BA5C"), GrassB = C("#72AB50"), GrassC = C("#A3C765"), Forest = C("#5F9A48");
        private static readonly Color Sand = C("#DDC48E"), WetSand = C("#BFA676"), Dirt = C("#BE9464"), Rock = C("#9A9181");
        private static readonly Color Plaza = C("#CDB894");
        private static readonly Color BedShallow = C("#BFAE7C"), BedDeep = C("#56694C"), FarHill = C("#5B8C5A");

        private static Color TerrainColor(Vector3 p, Vector3 n)
        {
            float d = WorldShape.ShoreDistance(p.x, p.z);
            float r = new Vector2(p.x, p.z).magnitude;
            float h = p.y;
            float noise = Mathf.PerlinNoise(p.x * 0.06f + 3.1f, p.z * 0.06f + 7.7f);
            float fine = Mathf.PerlinNoise(p.x * 0.35f, p.z * 0.35f);
            Color c;
            if (h < WorldShape.WaterLevel - 0.15f)
            {
                c = Color.Lerp(BedShallow, BedDeep, Mathf.InverseLerp(0.2f, 3.5f, -h));
            }
            else if (WorldShape.OnIsland(p.x, p.z, 8f))
            {
                // The island: sandy beaches round a grassy hump.
                c = h < 0.9f + noise * 0.3f ? Color.Lerp(WetSand, Sand, Mathf.InverseLerp(0f, 0.5f, h)) : Color.Lerp(GrassB, GrassA, noise);
            }
            else if (d < 7f + noise * 2.5f && h < 0.9f)
            {
                c = Color.Lerp(WetSand, Sand, Mathf.InverseLerp(0f, 1.5f, d));
            }
            else
            {
                c = Color.Lerp(GrassB, GrassA, noise);
                c = Color.Lerp(c, GrassC, Mathf.Clamp01((fine - 0.6f) * 2.5f));
                c = Color.Lerp(c, Forest, Mathf.InverseLerp(20f, 45f, d) * 0.7f);
                c = Color.Lerp(c, FarHill, Mathf.InverseLerp(60f, 110f, r));
                if (n.y < 0.82f) c = Color.Lerp(c, Rock, Mathf.InverseLerp(0.82f, 0.65f, n.y));
            }

            float path = WorldShape.DistanceToPath(p.x, p.z);
            if (d > 0.4f && path < 1.35f + fine * 0.4f) c = Color.Lerp(Dirt, c, Mathf.InverseLerp(0.7f, 1.6f, path) * 0.5f);
            float camp = Vector2.Distance(new Vector2(p.x, p.z), WorldShape.CampCenter);
            if (camp < 4.5f) c = Color.Lerp(c, Dirt, Mathf.InverseLerp(4.5f, 2f, camp) * 0.55f);
            float plaza = Vector2.Distance(new Vector2(p.x, p.z), WorldShape.MarketCenter);
            if (plaza < WorldShape.MarketRadius) c = Color.Lerp(c, Plaza, Mathf.InverseLerp(WorldShape.MarketRadius, WorldShape.MarketRadius * 0.7f, plaza) * (0.75f + fine * 0.2f));

            float jitter = 1f + ((float)_rng.NextDouble() - 0.5f) * 0.06f;
            return c * jitter;
        }

        private static void BuildTerrain()
        {
            float size = WorldShape.TerrainSize;
            float cell = 1.6f;
            int n = Mathf.RoundToInt(size / cell);
            float half = size * 0.5f;
            var heights = new float[n + 1, n + 1];
            for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
                heights[x, z] = WorldShape.TerrainHeight(-half + x * cell, -half + z * cell);

            var mb = new MeshBuilder();
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                Vector3 p00 = new Vector3(-half + x * cell, heights[x, z], -half + z * cell);
                Vector3 p10 = new Vector3(-half + (x + 1) * cell, heights[x + 1, z], -half + z * cell);
                Vector3 p01 = new Vector3(-half + x * cell, heights[x, z + 1], -half + (z + 1) * cell);
                Vector3 p11 = new Vector3(-half + (x + 1) * cell, heights[x + 1, z + 1], -half + (z + 1) * cell);
                if (((x + z) & 1) == 0)
                {
                    TriUp(mb, p00, p01, p11);
                    TriUp(mb, p00, p11, p10);
                }
                else
                {
                    TriUp(mb, p00, p01, p10);
                    TriUp(mb, p10, p01, p11);
                }
            }
            var mesh = mb.ToMesh("WillowLakeTerrain");
            SaveMesh(mesh, "Terrain");

            var go = new GameObject("Terrain");
            go.transform.SetParent(_env, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ComfyAssets.TerrainMat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);

            // Smooth, coarser collider mesh.
            var col = new Mesh { name = "TerrainCollider", indexFormat = IndexFormat.UInt32 };
            int cn = Mathf.RoundToInt(size / 2f);
            float cc = size / cn;
            var verts = new Vector3[(cn + 1) * (cn + 1)];
            for (int z = 0; z <= cn; z++)
            for (int x = 0; x <= cn; x++)
            {
                float wx = -half + x * cc, wz = -half + z * cc;
                verts[z * (cn + 1) + x] = new Vector3(wx, WorldShape.TerrainHeight(wx, wz), wz);
            }
            var tris = new List<int>();
            for (int z = 0; z < cn; z++)
            for (int x = 0; x < cn; x++)
            {
                int i = z * (cn + 1) + x;
                tris.AddRange(new[] { i, i + cn + 1, i + cn + 2, i, i + cn + 2, i + 1 });
            }
            col.vertices = verts;
            col.triangles = tris.ToArray();
            col.RecalculateNormals();
            col.RecalculateBounds();
            SaveMesh(col, "TerrainCollider");
            go.AddComponent<MeshCollider>().sharedMesh = col;
        }

        private static void TriUp(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.y < 0) (b, c) = (c, b);
            n = Vector3.Cross(b - a, c - a).normalized;
            mb.Triangle(a, b, c, TerrainColor((a + b + c) / 3f, n));
        }

        private static void SaveMesh(Mesh mesh, string name)
        {
            string path = $"{ComfyAssets.MeshFolder}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
        }

        // ------------------------------------------------------------------ water

        private static void BuildWater()
        {
            // The sea: a big plane from behind the beach out to the horizon (the land hides the part under it).
            float size = 300f;
            int n = 200;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
            {
                verts.Add(new Vector3(-size / 2 + x * size / n, 0f, -size / 2 + z * size / n));
                uvs.Add(new Vector2((float)x / n, (float)z / n));
            }
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * (n + 1) + x;
                tris.AddRange(new[] { i, i + n + 1, i + n + 2, i, i + n + 2, i + 1 });
            }
            var mesh = new Mesh { name = "Sea", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 2f, size));
            SaveMesh(mesh, "Sea");

            var go = new GameObject("Sea");
            go.transform.SetParent(_env, false);
            go.transform.position = new Vector3(0f, WorldShape.WaterLevel, WorldShape.CoastZ + size * 0.5f - 30f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ComfyAssets.WaterMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }

        // ------------------------------------------------------------------ props

        private static GameObject Place(string kitPath, Transform parent, Vector3 pos, float yaw, float scale, bool isStatic = true, bool shadows = true)
        {
            // The furniture kit is authored with corner pivots; use the re-pivoted prefab.
            var prefab = kitPath.StartsWith("FurnitureKit/") ? ComfyAssets.RemappedPrefab(kitPath) : ComfyAssets.Model(kitPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[SceneBuilder] Missing model {kitPath}");
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            KenneyMaterials.Remap(go, KenneyMaterials.KitOf(kitPath));
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            if (isStatic)
            {
                foreach (var t in go.GetComponentsInChildren<Transform>())
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            }
            return go;
        }

        private static Vector3 Ground(float x, float z, float sink = 0.04f) =>
            new Vector3(x, WorldShape.TerrainHeight(x, z) - sink, z);

        private static void AddLight(Transform parent, Vector3 localPos, Color color, float range, float night, float day, bool flicker, float glowSize)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = night;
            l.shadows = LightShadows.None;

            Renderer glow = null;
            if (glowSize > 0f)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.name = "Glow";
                q.transform.SetParent(go.transform, false);
                q.transform.localScale = Vector3.one * glowSize / Mathf.Max(0.01f, parent.lossyScale.x);
                glow = q.GetComponent<MeshRenderer>();
                glow.sharedMaterial = GameAssets.Instance.glowMaterial;
                glow.shadowCastingMode = ShadowCastingMode.Off;
                glow.receiveShadows = false;
                q.AddComponent<Billboard>();
            }
            go.AddComponent<NightLight>().Configure(l, glow, night, day, flicker);
        }

        // ------------------------------------------------------------------ dock

        private static void BuildDock(out Vector3 dockEnd, out Quaternion rot)
        {
            Vector2 d2 = -WorldShape.DockDirection;
            Vector2 start2 = WorldShape.DockShorePoint + WorldShape.DockDirection * 1.5f;
            float length = WorldShape.DockLength + 1.5f;
            float width = WorldShape.DockWidth;
            float deck = WorldShape.DockDeckHeight;

            var root = new GameObject("Dock").transform;
            root.SetParent(_env, false);
            root.position = new Vector3(start2.x, 0f, start2.y);
            rot = Quaternion.LookRotation(new Vector3(d2.x, 0f, d2.y));
            root.rotation = rot;

            var mb = new MeshBuilder();
            Color[] wood = { C("#A8784C"), C("#966941"), C("#B58659"), C("#9E6F46") };
            float plank = 0.3f, gap = 0.035f;
            int i = 0;
            for (float z = 0.15f; z < length; z += plank + gap, i++)
            {
                float w = width + ((i % 3) - 1) * 0.06f;
                float yJitter = (float)_rng.NextDouble() * 0.015f;
                mb.Box(new Vector3(((i % 2) - 0.5f) * 0.05f, deck - 0.04f + yJitter, z), new Vector3(w, 0.08f, plank), Quaternion.Euler(0, R(-1.2f, 1.2f), 0), Pick(wood));
            }
            // Stringers.
            foreach (float x in new[] { -width * 0.5f + 0.25f, width * 0.5f - 0.25f })
                mb.Box(new Vector3(x, deck - 0.17f, length * 0.5f), new Vector3(0.14f, 0.18f, length), Quaternion.identity, C("#7A5534"));
            // Posts (sticking up a little at the edges).
            for (float z = 0.4f; z <= length; z += 2.4f)
            {
                foreach (float x in new[] { -width * 0.5f - 0.05f, width * 0.5f + 0.05f })
                {
                    float bottom = Mathf.Min(-0.3f, WorldShape.TerrainHeight(root.position.x + (rot * new Vector3(x, 0, z)).x, root.position.z + (rot * new Vector3(x, 0, z)).z) - 0.3f);
                    float top = deck + 0.22f;
                    mb.Box(new Vector3(x, (top + bottom) * 0.5f, z), new Vector3(0.2f, top - bottom, 0.2f), Quaternion.Euler(0, R(-4, 4), 0), C("#7F5A3A"));
                }
            }
            var mesh = mb.ToMesh("Dock");
            SaveMesh(mesh, "Dock");
            var dockGo = new GameObject("DockMesh");
            dockGo.transform.SetParent(root, false);
            dockGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            dockGo.AddComponent<MeshRenderer>().sharedMaterial = ComfyAssets.WoodMat;
            GameObjectUtility.SetStaticEditorFlags(dockGo, StaticEditorFlags.BatchingStatic);
            var box = root.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, deck - 0.1f, length * 0.5f);
            box.size = new Vector3(width + 0.2f, 0.2f, length + 0.1f);

            // Props on the dock.
            Vector3 L(float x, float y, float z) => root.TransformPoint(new Vector3(x, y, z));
            float yaw = rot.eulerAngles.y;
            foreach (float side in new[] { -1f, 1f })
            {
                var lamp = Place("FantasyTown/lantern", root, L(side * (width * 0.5f - 0.05f), deck, length - 0.35f), yaw, 1.25f);
                if (lamp != null) AddLight(lamp.transform, new Vector3(0, 1.38f, 0), C("#FFC477"), 8f, 2.2f, 0f, false, 0.9f);
            }
            var lamp2 = Place("FantasyTown/lantern", root, L(-width * 0.5f + 0.05f, deck, 0.4f), yaw, 1.25f);
            if (lamp2 != null) AddLight(lamp2.transform, new Vector3(0, 1.38f, 0), C("#FFC477"), 8f, 2.2f, 0f, false, 0.9f);

            Place("SurvivalKit/bucket", root, L(width * 0.5f - 0.45f, deck, length - 2.2f), yaw + 20f, 2.6f);
            Place("SurvivalKit/barrel", root, L(width * 0.5f - 0.45f, deck, 1.6f), yaw, 2.6f);
            Place("PirateKit/crate", root, L(-width * 0.5f + 0.55f, deck, 2.4f), yaw + 8f, 0.55f);
            Place("SurvivalKit/fish", root, L(width * 0.5f - 0.35f, deck + 0.52f, length - 2.2f), yaw + 70f, 2.2f);

            // Old Wang's rowboat, tied up beside the end of the dock (rideable once rented: Rowboat).
            var boatRoot = new GameObject("Rowboat").transform;
            boatRoot.SetParent(_env, false);
            boatRoot.SetPositionAndRotation(L(width * 0.5f + 1.5f, -0.1f, length - 1.2f), Quaternion.Euler(0f, yaw + 4f, 0f));
            var boat = Place("PirateKit/boat-row-small", boatRoot, boatRoot.position, yaw + 4f, 0.95f, isStatic: false);
            if (boat != null)
            {
                boat.AddComponent<Floater>().Configure(0.035f, 1.8f, 0.8f);
                boatRoot.gameObject.AddComponent<Rowboat>().Configure(boat.transform);
            }

            dockEnd = L(0f, deck, length - 1.2f);
        }

        // ------------------------------------------------------------------ camp

        /// <summary>The island far out to sea: a grassy hump with a few trees and rocks, and a camp spot (IslandCamp).</summary>
        private static void BuildIsland()
        {
            var island = new GameObject("Island").transform;
            island.SetParent(_env, false);
            Vector2 c = WorldShape.IslandCenter;
            Vector3 centre = Ground(c.x, c.y);
            var rngTrees = new[] { (a: 20f, r: 6.5f), (a: 110f, r: 7.5f), (a: 200f, r: 5.5f), (a: 290f, r: 8f), (a: 250f, r: 3.5f) };
            foreach (var t in rngTrees)
            {
                float a = t.a * Mathf.Deg2Rad;
                var tree = Place("NatureKit/" + Pick(Broadleaf), island, Ground(c.x + Mathf.Cos(a) * t.r, c.y + Mathf.Sin(a) * t.r, 0.05f), R(0, 360), R(3.4f, 4.4f));
                if (tree != null)
                {
                    var cap = tree.AddComponent<CapsuleCollider>();
                    cap.radius = 0.06f;
                    cap.height = 0.5f;
                    cap.center = new Vector3(0, 0.25f, 0);
                }
            }
            for (int k = 0; k < 14; k++)
            {
                float a = R(0, Mathf.PI * 2f), r = R(10f, 13.5f);
                Place("NatureKit/" + Pick(BigRocks), island, Ground(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r, 0.1f), R(0, 360), R(1.4f, 2.6f));
            }
            for (int k = 0; k < 30; k++)
            {
                float a = R(0, Mathf.PI * 2f), r = R(0f, 9f);
                Place("NatureKit/" + Pick(Grasses), island, Ground(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r, 0.02f), R(0, 360), R(2.4f, 3.4f), shadows: false);
            }

            // The camp spot: tent + campfire appear once the player sets up camp.
            var campGo = new GameObject("IslandCamp");
            campGo.transform.SetParent(island, false);
            campGo.transform.SetPositionAndRotation(centre + new Vector3(0f, 0f, -2f), Quaternion.Euler(0f, 180f, 0f));
            var visuals = new GameObject("CampVisuals").transform;
            visuals.SetParent(campGo.transform, false);
            Place("NatureKit/tent_detailedOpen", visuals, Ground(c.x - 1.6f, c.y - 1.2f), 200f, 3.4f);
            Place("NatureKit/campfire_stones", visuals, Ground(c.x + 1.2f, c.y - 3.4f), 0f, 2.4f);
            Place("NatureKit/campfire_logs", visuals, Ground(c.x + 1.2f, c.y - 3.4f, 0.02f), 30f, 2.2f);
            Place("SurvivalKit/bedroll", visuals, Ground(c.x - 1.4f, c.y + 0.6f), 160f, 2.6f);
            campGo.AddComponent<IslandCamp>().Configure(visuals.gameObject);
            // A marker you can see from a distance: a flag-like lantern post.
            var lamp = Place("FantasyTown/lantern", island, Ground(c.x + 2.5f, c.y - 1.5f), 0f, 1.25f);
            if (lamp != null) AddLight(lamp.transform, new Vector3(0, 1.38f, 0), C("#FFC477"), 8f, 2.2f, 0f, false, 0.9f);
        }

        private static void BuildCamp(out Vector3 fireSpot)
        {
            Vector2 c2 = WorldShape.CampCenter;
            Vector2 f2 = -WorldShape.DockDirection;
            Vector3 f = new Vector3(f2.x, 0, f2.y);
            Vector3 r = Vector3.Cross(Vector3.up, f);
            Vector3 center = new Vector3(c2.x, WorldShape.CampHeight, c2.y);
            float yaw = Quaternion.LookRotation(f).eulerAngles.y;

            var camp = new GameObject("Camp").transform;
            camp.SetParent(_env, false);

            BuildCabin(camp, new Vector3(WorldShape.CabinCenter.x, WorldShape.CampHeight, WorldShape.CabinCenter.y), yaw);

            // Campfire with log seats.
            fireSpot = center + r * 3.6f + f * 1.2f;
            fireSpot.y = WorldShape.TerrainHeight(fireSpot.x, fireSpot.z);
            Place("NatureKit/campfire_stones", camp, fireSpot, 0f, 2.6f);
            Place("NatureKit/campfire_logs", camp, fireSpot + Vector3.up * 0.02f, 30f, 2.4f);
            Place("SurvivalKit/campfire-stand", camp, fireSpot, yaw + 90f, 3.2f);
            BuildFireFx(camp, fireSpot);
            for (int k = 0; k < 3; k++)
            {
                float a = (k * 120f + 30f) * Mathf.Deg2Rad;
                Vector3 p = fireSpot + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 2.1f;
                var log = Place("NatureKit/log_large", camp, Ground(p.x, p.z, 0.02f), -a * Mathf.Rad2Deg + 90f, 1.9f);
                if (log != null) AddBoxCollider(log, 0.8f);
            }

            // Tent on the other side, and cosy clutter.
            var tent = Place("NatureKit/tent_detailedOpen", camp, Ground(center.x - r.x * 4.8f + f.x * 0.5f, center.z - r.z * 4.8f + f.z * 0.5f), yaw + 25f, 3.6f);
            if (tent != null) AddBoxCollider(tent, 0.9f);
            var stack = Place("NatureKit/log_stackLarge", camp, Ground(center.x + r.x * 3.2f - f.x * 4.2f, center.z + r.z * 3.2f - f.z * 4.2f), yaw, 2.4f);
            if (stack != null) AddBoxCollider(stack, 0.9f);
            Place("PirateKit/barrel", camp, Ground(center.x - r.x * 1.8f - f.x * 2.4f, center.z - r.z * 1.8f - f.z * 2.4f), 10f, 0.55f);
            Place("PirateKit/crate", camp, Ground(center.x - r.x * 2.9f - f.x * 2.2f, center.z - r.z * 2.9f - f.z * 2.2f), yaw + 15f, 0.55f);
            Place("NatureKit/pot_large", camp, Ground(center.x + r.x * 1.2f - f.x * 1.9f, center.z + r.z * 1.2f - f.z * 1.9f), 0f, 2.4f);
            Place("SurvivalKit/bedroll", camp, Ground(center.x - r.x * 4.4f + f.x * 2.6f, center.z - r.z * 4.4f + f.z * 2.6f), yaw + 40f, 2.8f);

            // Sign by the dock + lanterns along the path.
            Vector2 s2 = WorldShape.DockShorePoint + WorldShape.DockDirection * 2.6f;
            Place("NatureKit/sign", camp, Ground(s2.x + r.x * 2f, s2.y + r.z * 2f), yaw + 180f + 15f, 2.8f);
            for (int k = 0; k < 2; k++)
            {
                Vector2 p2 = Vector2.Lerp(WorldShape.DockShorePoint + WorldShape.DockDirection * 2f, c2 - f2 * 1.5f, 0.35f + k * 0.4f);
                Vector3 side = r * (k % 2 == 0 ? 1.7f : -1.7f);
                var lamp = Place("FantasyTown/lantern", camp, Ground(p2.x + side.x, p2.y + side.z), 0f, 1.25f);
                if (lamp != null) AddLight(lamp.transform, new Vector3(0, 1.38f, 0), C("#FFC477"), 8f, 2.0f, 0f, false, 0.9f);
            }

            // Canoe pulled up on the beach.
            Vector2 cn = WorldShape.DockShorePoint + new Vector2(r.x, r.z) * 6.5f + WorldShape.DockDirection * 1.4f;
            Place("NatureKit/canoe", camp, Ground(cn.x, cn.y, 0.02f), yaw + 70f, 3.1f);
            Place("NatureKit/canoe_paddle", camp, Ground(cn.x + 1.2f, cn.y + 0.8f, 0.0f), yaw + 20f, 3.1f);
        }

        private static void AddBoxCollider(GameObject go, float shrink)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(b.center);
            Vector3 s = go.transform.lossyScale;
            box.size = new Vector3(b.size.x / s.x, b.size.y / s.y, b.size.z / s.z) * shrink;
        }

        private static void BuildCabin(Transform parent, Vector3 pos, float yaw)
        {
            const float scale = WorldShape.CabinScale;
            var root = new GameObject("Cabin").transform;
            root.SetParent(parent, false);
            pos.y = WorldShape.CampHeight - 0.02f;
            root.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            root.localScale = Vector3.one * scale;
            _cabinRoot = root;

            // Pieces are grouped by wall so the runtime Cabin can hide the walls between the camera and the player.
            Transform Group(string name)
            {
                var g = new GameObject(name).transform;
                g.SetParent(root, false);
                return g;
            }
            Transform floorGroup = Group("Floor"), front = Group("Front"), back = Group("Back"), left = Group("Left"), right = Group("Right"), corners = Group("Corners");

            GameObject Piece(Transform group, string name, float x, float z, float rotY, bool isStatic = true)
            {
                var prefab = ComfyAssets.Model("HolidayKit/" + name);
                if (prefab == null) return null;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                KenneyMaterials.Remap(go, "HolidayKit");
                go.transform.localPosition = new Vector3(x, 0f, z - 0.5f);
                go.transform.localRotation = Quaternion.Euler(0, rotY, 0);
                go.transform.localScale = Vector3.one;
                if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                return go;
            }

            // Floor.
            for (int x = -1; x <= 1; x++)
            for (int z = 0; z <= 1; z++)
                Piece(floorGroup, "floor-wood", x, z, 0);
            // Front (+z, facing the lake). The door's leaf (child "door", pivot on its hinge) swings open at runtime.
            Piece(front, "cabin-window-a", -1, 1, 0);
            Piece(front, "cabin-doorway", 0, 1, 0);
            Piece(front, "cabin-door-rotate", 0, 1, 0, isStatic: false);
            Piece(front, "cabin-window-a", 1, 1, 0);
            // Back.
            Piece(back, "cabin-wall", -1, 0, 180);
            Piece(back, "cabin-window-b", 0, 0, 180);
            Piece(back, "cabin-wall", 1, 0, 180);
            // Sides.
            Piece(right, "cabin-wall", 1, 0, 90);
            Piece(right, "cabin-window-c", 1, 1, 90);
            Piece(left, "cabin-wall", -1, 0, 270);
            Piece(left, "cabin-window-c", -1, 1, 270);
            // Corners.
            Piece(corners, "cabin-corner-logs", 1, 1, 0);
            Piece(corners, "cabin-corner-logs", 1, 0, 90);
            Piece(corners, "cabin-corner-logs", -1, 0, 180);
            Piece(corners, "cabin-corner-logs", -1, 1, 270);

            // Procedural gable roof (in cabin-local units).
            var mb = new MeshBuilder();
            Color roof = C("#4F9E74"), roofDark = C("#3E8561"), gable = C("#B8743F");
            float eaveY = 0.97f, ridgeY = 1.8f, over = 0.28f;
            float x0 = -1.5f - over, x1 = 1.5f + over;
            float zBack = -0.5f - 0.5f - over, zFront = 1.5f - 0.5f + over, zRidge = 0.0f;
            float th = 0.1f;
            // Slopes as thin boxes.
            void Slope(float zEave)
            {
                Vector3 a = new Vector3(0, eaveY, zEave);
                Vector3 b = new Vector3(0, ridgeY, zRidge);
                Vector3 mid = (a + b) * 0.5f + Vector3.up * th * 0.5f;
                float len = Vector3.Distance(a, b) + 0.06f;
                Quaternion q = Quaternion.LookRotation(b - a, Vector3.up);
                mb.Box(mid, new Vector3(x1 - x0, th, len), q, roof);
            }
            Slope(zFront);
            Slope(zBack);
            mb.Box(new Vector3(0, ridgeY + 0.07f, zRidge), new Vector3(x1 - x0 + 0.04f, 0.1f, 0.18f), Quaternion.identity, roofDark);
            // Gable triangles.
            foreach (float gx in new[] { -1.52f, 1.52f })
            {
                Vector3 g0 = new Vector3(gx, eaveY, -1.0f), g1 = new Vector3(gx, eaveY, 1.0f), g2 = new Vector3(gx, ridgeY - 0.05f, zRidge);
                mb.Triangle(g0, g1, g2, gable);
                mb.Triangle(g0, g2, g1, gable);
            }
            // Chimney.
            mb.Box(new Vector3(1.0f, 1.75f, -0.45f), new Vector3(0.3f, 0.7f, 0.3f), Quaternion.identity, C("#9C8F82"));
            var mesh = mb.ToMesh("CabinRoof");
            SaveMesh(mesh, "CabinRoof");
            var roofGo = new GameObject("Roof");
            roofGo.transform.SetParent(root, false);
            roofGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            roofGo.AddComponent<MeshRenderer>().sharedMaterial = ComfyAssets.PlainLitMat;
            GameObjectUtility.SetStaticEditorFlags(roofGo, StaticEditorFlags.BatchingStatic);

            // Walls you can't walk through, with a gap for the doorway (measured from the Kenney pieces with
            // DebugTools.DumpCabinPieces: walls are 0.3 thick on the tile edge, the doorway is 0.45 wide and 0.78 high).
            void Wall(Vector3 center, Vector3 size)
            {
                var c = root.gameObject.AddComponent<BoxCollider>();
                c.center = center;
                c.size = size;
            }
            const float halfW = 1.5f, halfD = 1.0f, t = 0.3f, h = 1.0f, door = 0.225f;
            Wall(new Vector3(0, h / 2, -halfD), new Vector3(halfW * 2 + t, h, t));                        // back
            Wall(new Vector3(-halfW, h / 2, 0), new Vector3(t, h, halfD * 2 + t));                        // left
            Wall(new Vector3(halfW, h / 2, 0), new Vector3(t, h, halfD * 2 + t));                         // right
            float sideLen = halfW + t / 2 - door;
            Wall(new Vector3(-(door + sideLen / 2), h / 2, halfD), new Vector3(sideLen, h, t));           // front, left of the door
            Wall(new Vector3(door + sideLen / 2, h / 2, halfD), new Vector3(sideLen, h, t));              // front, right of the door
            Wall(new Vector3(0, (0.78f + h) / 2, halfD), new Vector3(door * 2, h - 0.78f, t));            // above the door
            Wall(new Vector3(0, 0.075f / 2, 0), new Vector3(halfW * 2, 0.075f, halfD * 2));              // floor (furniture sits on it)

            var interior = new GameObject("InteriorLight").transform;
            interior.SetParent(root, false);
            interior.localPosition = new Vector3(0, 0.85f, -0.1f);
            var il = interior.gameObject.AddComponent<Light>(); // switched on by Cabin while the player is inside
            il.type = LightType.Point;
            il.color = C("#FFD9A0");
            il.range = 7f;
            il.intensity = 1.6f;
            il.shadows = LightShadows.None;
            il.enabled = false;

            root.gameObject.AddComponent<Cabin>();

            // The bed (the house comes with a sleeping mat; BedInteractable swaps in better beds as you buy them).
            var bedGo = new GameObject("Bed");
            bedGo.transform.SetParent(root, false);
            bedGo.transform.localPosition = new Vector3(-0.95f, Cabin.FloorTop, -0.5f);
            bedGo.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            bedGo.transform.localScale = Vector3.one / scale; // real-world size inside the scaled cabin
            bedGo.AddComponent<BedInteractable>();

            // Porch lantern + a warm glow in the window at night.
            var lantern = Place("HolidayKit/lantern-hanging", root, root.TransformPoint(new Vector3(0.62f, 0.98f, 1.12f)), yaw, 1f);
            if (lantern != null) AddLight(lantern.transform, new Vector3(0, -0.3f, 0.2f), C("#FFB866"), 7f, 2.2f, 0f, true, 0.7f);
            var inside = new GameObject("WindowGlow").transform;
            inside.SetParent(root, false);
            inside.localPosition = new Vector3(0, 0.5f, 0.2f);
            AddLight(inside, Vector3.zero, C("#FFB060"), 6f, 1.6f, 0f, true, 0f);
        }

        private static void BuildFireFx(Transform parent, Vector3 spot)
        {
            var ga = GameAssets.Instance;
            var fx = new GameObject("CampfireFX").transform;
            fx.SetParent(parent, false);
            fx.position = spot + Vector3.up * 0.15f;

            var fire = fx.gameObject.AddComponent<ParticleSystem>();
            var main = fire.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            var em = fire.emission;
            em.rateOverTime = 38f;
            var shape = fire.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.28f;
            shape.rotation = new Vector3(-90, 0, 0);
            var sol = fire.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.1f));
            var col = fire.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0), new GradientColorKey(new Color(1f, 0.45f, 0.15f), 0.5f), new GradientColorKey(new Color(0.8f, 0.2f, 0.1f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var fr = fx.GetComponent<ParticleSystemRenderer>();
            fr.sharedMaterial = ga.fireMaterial;
            fr.shadowCastingMode = ShadowCastingMode.Off;

            var smokeGo = new GameObject("Smoke");
            smokeGo.transform.SetParent(fx, false);
            smokeGo.transform.localPosition = Vector3.up * 0.6f;
            var smoke = smokeGo.AddComponent<ParticleSystem>();
            var sm = smoke.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4.5f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            sm.maxParticles = 60;
            var sem = smoke.emission;
            sem.rateOverTime = 5f;
            var ssh = smoke.shape;
            ssh.shapeType = ParticleSystemShapeType.Circle;
            ssh.radius = 0.15f;
            ssh.rotation = new Vector3(-90, 0, 0);
            var ssol = smoke.sizeOverLifetime;
            ssol.enabled = true;
            ssol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 2.6f));
            var scol = smoke.colorOverLifetime;
            scol.enabled = true;
            var sg = new Gradient();
            sg.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.7f, 0.2f), new GradientAlphaKey(0, 1) });
            scol.color = sg;
            var vel = smoke.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            var sr = smokeGo.GetComponent<ParticleSystemRenderer>();
            sr.sharedMaterial = ga.smokeMaterial;
            sr.shadowCastingMode = ShadowCastingMode.Off;

            AddLight(fx, new Vector3(0, 0.6f, 0), C("#FF9A4A"), 10f, 3.2f, 0.9f, true, 1.6f);
        }


        // ------------------------------------------------------------------ market

        private static readonly string[] StallModels = { "FantasyTown/stall-green", "FantasyTown/stall-red", "FantasyTown/stall-green", "FantasyTown/stall-red", "FantasyTown/stall-green", "FantasyTown/stall-red", "FantasyTown/stall-green", "FantasyTown/stall-red", "FantasyTown/stall-green", "FantasyTown/stall-red" };
        private static readonly string[] KeeperModels =
        {
            "MiniCharacters/character-male-d", "MiniCharacters/character-female-a", "MiniCharacters/character-male-e", "MiniCharacters/character-female-c",
            "MiniCharacters/character-female-d", "MiniCharacters/character-male-c", "MiniCharacters/character-male-b", "MiniCharacters/character-female-e",
            "MiniCharacters/character-female-b", "MiniCharacters/character-male-f",
        };

        private static Bounds ModelBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private static void BuildMarket(GameObject player)
        {
            var market = new GameObject("Market").transform;
            market.SetParent(_env, false);
            Vector2 c2 = WorldShape.MarketCenter;
            Vector3 center = new Vector3(c2.x, WorldShape.MarketHeight, c2.y);

            for (int i = 0; i < WorldShape.StallCount && i < Catalog.Shops.Count; i++)
            {
                var shop = Catalog.Shops[i];
                Vector2 s2 = WorldShape.StallPosition(i);
                Vector3 pos = Ground(s2.x, s2.y, 0.02f);
                Vector2 f2 = WorldShape.StallFront(i);
                Vector3 front = new Vector3(f2.x, 0f, f2.y);
                float yaw = Quaternion.LookRotation(front).eulerAngles.y;

                var stallRoot = new GameObject("Stall_" + shop.id).transform;
                stallRoot.SetParent(market, false);
                stallRoot.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

                // Stall: scale to ~3.4 m wide, open side towards the plaza.
                const float scale = StallScale;
                // Measure the footprint unrotated (the model's long side runs along its local Z), then turn it.
                var stall = Place(StallModels[i], stallRoot, pos, 0f, scale);
                Bounds local = ModelBounds(stall);
                float depth = StallYawOffset % 180f == 0f ? local.size.z : local.size.x;
                var col = stall.AddComponent<BoxCollider>();
                col.center = stall.transform.InverseTransformPoint(local.center);
                col.size = local.size / scale * 0.85f;
                stall.transform.rotation = Quaternion.Euler(0f, yaw + StallYawOffset, 0f);
                Bounds b = ModelBounds(stall);
                float counterY = b.min.y + b.size.y * CounterHeightFraction;

                // Shopkeeper behind the counter.
                var keeperGo = new GameObject(shop.keeperEnglish);
                keeperGo.transform.SetParent(stallRoot, false);
                keeperGo.transform.position = pos - front * (depth * 0.5f + KeeperBehind);
                keeperGo.transform.rotation = Quaternion.LookRotation(front);
                var anim = AttachModel(keeperGo, KeeperModels[i], CharacterScale);
                keeperGo.AddComponent<AudioSource>();
                var voice = keeperGo.AddComponent<CharacterVoice>();
                voice.Configure(shop.voice, "", shop.pitch);
                var brain = keeperGo.AddComponent<ShopkeeperBrain>();
                brain.Configure(shop.id, voice, player.transform);
                keeperGo.AddComponent<TalkingHead>().Configure(anim, voice);

                // Goods on the counter, with price tags.
                Vector3 right = Vector3.Cross(Vector3.up, front);
                Vector3 counterFront = pos + front * CounterForward;
                BuildDisplay(shop, stallRoot, counterFront, right, counterY, yaw);

                // A lantern beside each stall.
                var lamp = Place("FantasyTown/lantern", stallRoot, Ground(pos.x + right.x * 2.3f + front.x * 0.8f, pos.z + right.z * 2.3f + front.z * 0.8f), 0f, 1.25f);
                if (lamp != null) AddLight(lamp.transform, new Vector3(0, 1.38f, 0), C("#FFC477"), 8f, 2.0f, 0f, false, 0.9f);
            }

            // A bit of market clutter and a sign at the entrance.
            Vector2 entrance = c2 - new Vector2(WorldShape.MarketRadius * 0.75f, 0f);
            Place("NatureKit/sign", market, Ground(entrance.x, entrance.y + 2f), 90f + 180f, 2.8f);
            // (Kept clear of the bookshop and trainer stalls north and south of the entrance.)
            Place("FantasyTown/cart", market, Ground(c2.x - 11.5f, c2.y - 7.5f), 30f, 1.4f);
            Place("PirateKit/barrel", market, Ground(c2.x - 10.6f, c2.y + 8.2f), 0f, 0.55f);
            Place("PirateKit/crate", market, Ground(c2.x - 11.4f, c2.y + 7.4f), 20f, 0.55f);
            Place("FantasyTown/stall-bench", market, Ground(c2.x + 1.5f, c2.y - 0.5f), 0f, 1.6f);

            BuildCrabPots(market);
            BuildGameStalls(market);
        }

        /// <summary>A plain coloured material saved as an asset (primitives in the scene need real materials).</summary>
        private static Material SolidMat(string name, string hex)
        {
            string path = $"{ComfyAssets.MatFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var m = new Material(ComfyAssets.PlainLitMat) { name = name };
            m.SetColor("_BaseColor", C(hex));
            m.SetFloat("_VertexColorWeight", 0f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, Material m, string name = null)
        {
            var p = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(p.GetComponent<Collider>());
            if (name != null) p.name = name;
            p.transform.SetParent(parent, false);
            p.transform.position = pos;
            p.transform.localScale = scale;
            p.transform.rotation = rot;
            p.GetComponent<Renderer>().sharedMaterial = m;
            return p;
        }

        /// <summary>海叔's pots out at sea: a line of orange buoys off his stretch of beach, and his little boat pulled up.</summary>
        private static void BuildCrabPots(Transform market)
        {
            var root = new GameObject("CrabPots").transform;
            root.SetParent(market, false);
            Vector2 c = WorldShape.CrabberPosition;
            var orange = SolidMat("Buoy_Orange", "#F08A3C");
            var white = SolidMat("Buoy_White", "#F2EFE8");
            for (int k = 0; k < 6; k++)
            {
                float x = c.x - 7f + k * 2.8f + (k % 2) * 0.6f;
                float z = WorldShape.ShoreZ(x) + 5f + (k % 3) * 2.2f;
                var buoy = new GameObject("Buoy");
                buoy.transform.SetParent(root, false);
                buoy.transform.position = new Vector3(x, WorldShape.WaterLevel, z);
                Prim(PrimitiveType.Sphere, buoy.transform, buoy.transform.position + Vector3.up * 0.05f, new Vector3(0.42f, 0.36f, 0.42f), Quaternion.identity, orange);
                Prim(PrimitiveType.Cylinder, buoy.transform, buoy.transform.position + Vector3.up * 0.35f, new Vector3(0.05f, 0.22f, 0.05f), Quaternion.identity, white);
            }
            Place("PirateKit/boat-row-small", root, Ground(c.x + 4.2f, c.y + 1.2f), 70f, 0.9f);
            // Spare pots stacked by the stall.
            Place("PirateKit/crate", root, Ground(c.x - 2.7f, c.y - 0.6f), 10f, 0.55f);
            Place("PirateKit/crate", root, Ground(c.x - 2.9f, c.y + 0.4f), -15f, 0.55f);
            Place("PirateKit/barrel", root, Ground(c.x + 2.6f, c.y - 0.9f), 0f, 0.5f);
        }

        /// <summary>
        /// The three games stalls on the seafront. Each has its finished stall ("Built") and a fenced building site
        /// ("Construction"); MinigameStall shows one or the other by the player's HSK level.
        /// </summary>
        private static void BuildGameStalls(Transform market)
        {
            (string id, string hanzi, string english, int hsk, string blurb, string model)[] games =
            {
                ("pitchpot", "投壶", "Pitch-pot", 1, "throw arrows into a tall pot, an ancient party game.", "FantasyTown/stall-green"),
                ("jianzi", "毽子", "Jianzi", 2, "keep the feathered shuttlecock in the air with your feet.", "FantasyTown/stall-red"),
                ("mahjong", "麻将", "Mahjong", 3, "four players, 144 tiles, and a lot of talking.", "FantasyTown/stall-green"),
            };
            var red = SolidMat("Site_Red", "#D9534A");
            var cream = SolidMat("Mahjong_Tile", "#F4EEDC");
            var green = SolidMat("Mahjong_Back", "#3F8F5A");
            var bronze = SolidMat("Pot_Bronze", "#9A6B3A");
            var feather = SolidMat("Jianzi_Feather", "#E05A8C");
            for (int k = 0; k < games.Length; k++)
            {
                var g = games[k];
                Vector2 p2 = WorldShape.GameStallPosition(k);
                Vector2 f2 = WorldShape.GameStallFront;
                Vector3 front = new Vector3(f2.x, 0f, f2.y), right = Vector3.Cross(Vector3.up, front);
                Vector3 pos = Ground(p2.x, p2.y, 0.02f);
                float yaw = Quaternion.LookRotation(front).eulerAngles.y;

                var root = new GameObject("Game_" + g.id);
                root.transform.SetParent(market, false);
                root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                var stall = root.AddComponent<MinigameStall>();
                stall.gameId = g.id; stall.hanzi = g.hanzi; stall.english = g.english; stall.hsk = g.hsk; stall.blurb = g.blurb;

                // Finished: a stall with the game on its counter.
                var built = new GameObject("Built").transform;
                built.SetParent(root.transform, false);
                var model = Place(g.model, built, pos, yaw + StallYawOffset, StallScale);
                float counterY = model != null ? ModelBounds(model).min.y + ModelBounds(model).size.y * CounterHeightFraction : pos.y + 0.9f;
                Vector3 counter = new Vector3(pos.x, counterY, pos.z) + front * CounterForward;
                switch (g.id)
                {
                    case "mahjong":
                        for (int t = 0; t < 7; t++)
                        {
                            Vector3 tp = counter + right * (-0.55f + t * 0.17f) + Vector3.up * 0.06f;
                            Prim(PrimitiveType.Cube, built, tp, new Vector3(0.12f, 0.15f, 0.09f), Quaternion.Euler(0f, yaw, 0f), cream);
                            Prim(PrimitiveType.Cube, built, tp - front * 0.05f, new Vector3(0.12f, 0.15f, 0.02f), Quaternion.Euler(0f, yaw, 0f), green);
                        }
                        break;
                    case "jianzi":
                        for (int t = 0; t < 2; t++)
                        {
                            Vector3 jp = counter + right * (-0.3f + t * 0.6f);
                            Prim(PrimitiveType.Cylinder, built, jp + Vector3.up * 0.03f, new Vector3(0.12f, 0.02f, 0.12f), Quaternion.identity, bronze);
                            Prim(PrimitiveType.Sphere, built, jp + Vector3.up * 0.16f, new Vector3(0.14f, 0.22f, 0.14f), Quaternion.identity, feather);
                        }
                        break;
                    case "pitchpot":
                    {
                        // The pot stands in front of the stall; you throw from 3.4 m further out (PitchPotGame).
                        // Body 0.4 wide and 0.6 tall, neck 0.26 wide up to 0.86 m: the mouth is 13 cm across the radius.
                        Vector3 pp = Ground(pos.x + front.x * 2.4f, pos.z + front.z * 2.4f, 0f);
                        var pot = new GameObject("Pot").transform;
                        pot.SetParent(built, false);
                        pot.position = pp;
                        Prim(PrimitiveType.Cylinder, pot, pp + Vector3.up * 0.3f, new Vector3(0.4f, 0.3f, 0.4f), Quaternion.identity, bronze);
                        Prim(PrimitiveType.Cylinder, pot, pp + Vector3.up * 0.72f, new Vector3(0.26f, 0.14f, 0.26f), Quaternion.identity, bronze);
                        Prim(PrimitiveType.Cylinder, pot, pp + Vector3.up * 0.861f, new Vector3(0.2f, 0.002f, 0.2f), Quaternion.identity, SolidMat("Pot_Inside", "#2A1E14"));
                        // A quiver of arrows on the counter.
                        for (int t = 0; t < 3; t++)
                            Prim(PrimitiveType.Cylinder, built, counter + right * (0.4f + t * 0.06f) + Vector3.up * 0.2f, new Vector3(0.025f, 0.22f, 0.025f), Quaternion.Euler(8f * (t - 1), 0f, 6f), cream);
                        // The throwing line.
                        Vector3 line = Ground(pos.x + front.x * 5.8f, pos.z + front.z * 5.8f, -0.02f);
                        Prim(PrimitiveType.Cube, built, line, new Vector3(1.4f, 0.02f, 0.06f), Quaternion.Euler(0f, yaw, 0f), cream);
                        break;
                    }
                }

                // Not yet: a fenced building site with timber and a red 施工中 sign.
                var site = new GameObject("Construction").transform;
                site.SetParent(root.transform, false);
                Place("SurvivalKit/structure", site, pos, yaw, 2.4f);
                Place("SurvivalKit/resource-wood", site, Ground(pos.x + right.x * 1.2f + front.x * 0.6f, pos.z + right.z * 1.2f + front.z * 0.6f), yaw + 20f, 0.8f);
                Place("PirateKit/barrel", site, Ground(pos.x - right.x * 1.5f + front.x * 0.4f, pos.z - right.z * 1.5f + front.z * 0.4f), 0f, 0.5f);
                for (int s = -1; s <= 1; s++)
                    Place("FantasyTown/fence", site, Ground(pos.x + front.x * 2.1f + right.x * s * 1.25f, pos.z + front.z * 2.1f + right.z * s * 1.25f), yaw, 1.3f);
                site.gameObject.SetActive(true);
                built.gameObject.SetActive(false);
            }
        }

        // Tuned from preview captures of Kenney's stall models.
        private const float StallScale = 1.6f;
        private const float StallYawOffset = 90f;
        private const float CounterHeightFraction = 0.345f;
        private const float CounterForward = 0.15f;
        private const float KeeperBehind = 0.3f;

        /// <summary>Spawns a catalog item's (re-pivoted, remapped) prefab for a display.</summary>
        private static GameObject PlaceItem(string itemId, Transform parent, Vector3 pos, float yaw, float scaleMul = 1f)
        {
            var def = Catalog.Get(itemId);
            var prefab = def != null ? GameAssets.Instance.PrefabFor(itemId) : null;
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * def.modelScale * scaleMul;
            Tag(go, itemId);
            return go;
        }

        private static void Tag(GameObject go, string itemId)
        {
            if (go == null || string.IsNullOrEmpty(itemId)) return;
            go.AddComponent<PriceTag>().itemId = itemId;
        }

        private static void BuildDisplay(ShopDef shop, Transform parent, Vector3 counter, Vector3 right, float y, float yaw)
        {
            Vector3 front = Vector3.Cross(right, Vector3.up);
            Vector3 At(float side) => new Vector3(counter.x + right.x * side, y, counter.z + right.z * side);
            Vector3 g(float side, float fwd) => Ground(counter.x + right.x * side + front.x * fwd, counter.z + right.z * side + front.z * fwd);
            switch (shop.id)
            {
                case "tackle":
                {
                    // Rods leaning on the counter.
                    var rods = new GameObject("Rods");
                    rods.transform.SetParent(parent, false);
                    rods.transform.position = g(-0.9f, 1.25f);
                    Color[] colors = { C("#DDBD6B"), C("#2E3440"), C("#FFC640") };
                    string[] ids = { "rod_bamboo", "rod_carbon", "rod_gold" };
                    for (int k = 0; k < 3; k++)
                    {
                        var rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        Object.DestroyImmediate(rod.GetComponent<Collider>());
                        rod.name = ids[k];
                        rod.transform.SetParent(rods.transform, false);
                        rod.transform.localPosition = new Vector3(k * 0.28f, 0.75f, 0f);
                        rod.transform.localScale = new Vector3(0.035f, 0.8f, 0.035f);
                        rod.transform.localRotation = Quaternion.Euler(0f, yaw, 12f);
                        var mat = new Material(ComfyAssets.PlainLitMat) { name = "DisplayRod_" + ids[k] };
                        mat.SetColor("_BaseColor", colors[k]);
                        mat.SetFloat("_VertexColorWeight", 0f);
                        string matPath = $"{ComfyAssets.MatFolder}/DisplayRod_{ids[k]}.mat";
                        AssetDatabase.DeleteAsset(matPath);
                        AssetDatabase.CreateAsset(mat, matPath);
                        rod.GetComponent<Renderer>().sharedMaterial = mat;
                        Tag(rod, ids[k]);
                    }
                    Tag(Place("SurvivalKit/box-open", parent, At(-0.4f), yaw + 10f, 0.75f), "bait_worm");
                    Tag(Place("SurvivalKit/box", parent, At(0.05f), yaw - 5f, 0.75f), "bait_shrimp");
                    Tag(Place("SurvivalKit/bucket", parent, At(0.55f), yaw, 1.4f), "bucket_big");
                    break;
                }
                case "fish":
                {
                    // 陈阿姨's sushi bar: a fish waiting on the board, a plate of sushi, and her scale.
                    Place("FoodKit/fish", parent, At(-0.62f) + front * 0.12f, yaw + 90f, 0.55f);
                    Material Mat(string name, string hex)
                    {
                        var m = new Material(ComfyAssets.PlainLitMat) { name = name };
                        m.SetColor("_BaseColor", C(hex));
                        m.SetFloat("_VertexColorWeight", 0f);
                        string path = $"{ComfyAssets.MatFolder}/{name}.mat";
                        AssetDatabase.DeleteAsset(path);
                        AssetDatabase.CreateAsset(m, path);
                        return m;
                    }
                    var rice = Mat("SushiRice", "#F4F1E8");
                    var plate = Mat("SushiPlate", "#3B4A5C");
                    var metal = Mat("ScaleMetal", "#B9C3C9");
                    Material[] tops = { Mat("SushiSalmon", "#F08A5D"), Mat("SushiTuna", "#C8424A"), Mat("SushiEgg", "#F2C94C") };
                    void Prim(PrimitiveType type, Vector3 pos, Vector3 scale, Material m, float turn = 0f)
                    {
                        var p = GameObject.CreatePrimitive(type);
                        Object.DestroyImmediate(p.GetComponent<Collider>());
                        p.transform.SetParent(parent, true);
                        p.transform.position = pos;
                        p.transform.rotation = Quaternion.Euler(0f, yaw + turn, 0f);
                        p.transform.localScale = scale;
                        p.GetComponent<Renderer>().sharedMaterial = m;
                    }
                    Vector3 platePos = At(-0.05f) + Vector3.up * 0.01f;
                    Prim(PrimitiveType.Cylinder, platePos, new Vector3(0.42f, 0.012f, 0.42f), plate);
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 piece = platePos + right * (-0.12f + k * 0.12f) + Vector3.up * 0.035f;
                        Prim(PrimitiveType.Cube, piece, new Vector3(0.07f, 0.045f, 0.11f), rice, 90f);
                        Prim(PrimitiveType.Cube, piece + Vector3.up * 0.03f, new Vector3(0.08f, 0.015f, 0.13f), tops[k], 90f);
                    }
                    Vector3 scalePos = At(0.6f);
                    Prim(PrimitiveType.Cube, scalePos + Vector3.up * 0.04f, new Vector3(0.22f, 0.08f, 0.18f), metal);
                    Prim(PrimitiveType.Cylinder, scalePos + Vector3.up * 0.1f, new Vector3(0.3f, 0.01f, 0.3f), metal);
                    break;
                }
                case "furniture":
                {
                    PlaceItem("chair", parent, g(-1.7f, 1.9f), yaw + 200f);
                    PlaceItem("plant", parent, g(1.8f, 1.7f), yaw);
                    PlaceItem("floor_lamp", parent, g(-1.6f, 0.2f), yaw);
                    PlaceItem("radio", parent, At(-0.35f), yaw);
                    PlaceItem("teddy", parent, At(0.45f), yaw + 20f);
                    break;
                }
                case "books":
                    PlaceItem("book_basics", parent, At(-0.55f), yaw + 8f);
                    PlaceItem("book_bigfish", parent, At(-0.05f), yaw - 6f);
                    PlaceItem("book_deep", parent, At(0.45f), yaw + 14f);
                    Place("PirateKit/crate", parent, g(-1.75f, 1.6f), yaw + 12f, 0.55f);
                    break;
                case "gym":
                {
                    // Dumbbells on the counter, built from primitives (no free dumbbell model).
                    for (int k = 0; k < 2; k++)
                    {
                        var bell = new GameObject("Dumbbell");
                        bell.transform.SetParent(parent, false);
                        bell.transform.position = At(-0.45f + k * 0.7f) + Vector3.up * 0.09f;
                        bell.transform.rotation = Quaternion.Euler(0f, yaw + 90f + k * 15f, 0f);
                        var mat = ComfyAssets.PlainLitMat;
                        void Part(PrimitiveType type, Vector3 pos, Vector3 scale, Quaternion rot)
                        {
                            var p = GameObject.CreatePrimitive(type);
                            Object.DestroyImmediate(p.GetComponent<Collider>());
                            p.transform.SetParent(bell.transform, false);
                            p.transform.localPosition = pos;
                            p.transform.localScale = scale;
                            p.transform.localRotation = rot;
                            p.GetComponent<Renderer>().sharedMaterial = mat;
                        }
                        Quaternion side = Quaternion.Euler(0f, 0f, 90f);
                        Part(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.05f, 0.22f, 0.05f), side);
                        Part(PrimitiveType.Cylinder, new Vector3(-0.2f, 0f, 0f), new Vector3(0.17f, 0.05f, 0.17f), side);
                        Part(PrimitiveType.Cylinder, new Vector3(0.2f, 0f, 0f), new Vector3(0.17f, 0.05f, 0.17f), side);
                        if (k == 0) Tag(bell, "training");
                    }
                    Place("PirateKit/barrel", parent, g(1.8f, 1.4f), yaw, 0.55f);
                    break;
                }
                case "crabber":
                    // 海叔's catch on the counter, and a pot waiting to be mended.
                    Place("CubePets/animal-crab", parent, At(-0.45f) + Vector3.up * 0.02f, yaw + 160f, 0.17f);
                    Place("CubePets/animal-crab", parent, At(0.5f) + Vector3.up * 0.02f, yaw - 140f, 0.14f);
                    Place("SurvivalKit/box-open", parent, g(1.8f, 1.2f), yaw + 15f, 0.8f);
                    break;
                case "school":
                {
                    // A blackboard on two legs beside the counter, and a pile of books on it.
                    var board = new GameObject("Blackboard");
                    board.transform.SetParent(parent, false);
                    board.transform.position = g(1.75f, 1.1f);
                    board.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    var dark = new Material(ComfyAssets.PlainLitMat) { name = "Blackboard" };
                    dark.SetColor("_BaseColor", C("#2F4A3A"));
                    dark.SetFloat("_VertexColorWeight", 0f);
                    string darkPath = $"{ComfyAssets.MatFolder}/Blackboard.mat";
                    AssetDatabase.DeleteAsset(darkPath);
                    AssetDatabase.CreateAsset(dark, darkPath);
                    void Piece(Vector3 pos, Vector3 scale, Material m)
                    {
                        var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Object.DestroyImmediate(p.GetComponent<Collider>());
                        p.transform.SetParent(board.transform, false);
                        p.transform.localPosition = pos;
                        p.transform.localScale = scale;
                        p.GetComponent<Renderer>().sharedMaterial = m;
                    }
                    Piece(new Vector3(0f, 1.15f, 0f), new Vector3(1.2f, 0.8f, 0.05f), dark);
                    Piece(new Vector3(-0.55f, 0.6f, 0.03f), new Vector3(0.06f, 1.2f, 0.06f), ComfyAssets.PlainLitMat);
                    Piece(new Vector3(0.55f, 0.6f, 0.03f), new Vector3(0.06f, 1.2f, 0.06f), ComfyAssets.PlainLitMat);
                    Place("FurnitureKit/books", parent, At(-0.3f), yaw + 8f, 0.2f);
                    break;
                }
                case "colours":
                {
                    // Paint pots in three colours.
                    string[] pots = { "#E0604E", "#4F8FC0", "#F2C94C" };
                    for (int k = 0; k < 3; k++)
                    {
                        var pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        Object.DestroyImmediate(pot.GetComponent<Collider>());
                        pot.name = "PaintPot";
                        pot.transform.SetParent(parent, false);
                        pot.transform.position = At(-0.5f + k * 0.5f) + Vector3.up * 0.11f;
                        pot.transform.localScale = new Vector3(0.22f, 0.11f, 0.22f);
                        var mat = new Material(ComfyAssets.PlainLitMat) { name = "PaintPot" + k };
                        mat.SetColor("_BaseColor", C(pots[k]));
                        mat.SetFloat("_VertexColorWeight", 0f);
                        string matPath = $"{ComfyAssets.MatFolder}/PaintPot{k}.mat";
                        AssetDatabase.DeleteAsset(matPath);
                        AssetDatabase.CreateAsset(mat, matPath);
                        pot.GetComponent<Renderer>().sharedMaterial = mat;
                    }
                    break;
                }
                case "gifts":
                    Tag(Place("FoodKit/cup-tea", parent, At(-0.6f), yaw + 10f, 1.2f), "gift_tea");
                    Tag(Place("FoodKit/mug", parent, At(-0.2f), yaw - 20f, 1.2f), "gift_coffee");
                    Tag(Place("FoodKit/loaf", parent, At(0.25f), yaw + 30f, 1.2f), "gift_bread");
                    Place("FoodKit/apple", parent, At(0.62f), yaw, 1.2f);
                    break;
                case "pet":
                    Tag(Place("FurnitureKit/cardboardBoxClosed", parent, At(-0.5f), yaw + 5f, 0.1f), "cat_food");
                    Tag(Place("FoodKit/fish-bones", parent, At(0.1f), yaw + 40f, 1.1f), "cat_treat");
                    PlaceItem("cat_box", parent, g(1.7f, 1.6f), yaw - 8f);
                    break;
            }
        }

        // ------------------------------------------------------------------ nature scatter

        private static readonly string[] Broadleaf =
        {
            "tree_default", "tree_oak", "tree_detailed", "tree_fat", "tree_tall", "tree_simple", "tree_small", "tree_thin", "tree_plateau", "tree_blocks",
        };
        private static readonly string[] Pines =
        {
            "tree_pineRoundA", "tree_pineRoundB", "tree_pineRoundC", "tree_pineRoundD", "tree_pineRoundE", "tree_pineRoundF",
            "tree_pineTallA", "tree_pineTallB", "tree_pineTallC", "tree_pineTallD", "tree_pineDefaultA", "tree_pineDefaultB",
        };
        private static readonly string[] Grasses = { "grass", "grass_large", "grass_leafs", "grass_leafsLarge" };
        private static readonly string[] Flowers =
        {
            "flower_purpleA", "flower_purpleB", "flower_purpleC", "flower_redA", "flower_redB", "flower_redC", "flower_yellowA", "flower_yellowB", "flower_yellowC",
        };
        private static readonly string[] Bushes = { "plant_bush", "plant_bushDetailed", "plant_bushLarge", "plant_bushSmall", "plant_bushTriangle", "plant_bushLargeTriangle" };
        private static readonly string[] BigRocks = { "rock_largeA", "rock_largeB", "rock_largeC", "rock_largeD", "rock_largeE", "rock_largeF", "rock_tallA", "rock_tallB", "rock_tallC" };
        private static readonly string[] SmallRocks = { "rock_smallA", "rock_smallB", "rock_smallC", "rock_smallD", "rock_smallE", "rock_smallF", "rock_smallFlatA", "rock_smallFlatB", "stone_smallA", "stone_smallB" };
        private static readonly string[] Mushrooms = { "mushroom_red", "mushroom_redGroup", "mushroom_tan", "mushroom_tanGroup", "mushroom_redTall" };
        private static readonly string[] Deadwood = { "log", "log_large", "stump_old", "stump_oldTall", "stump_round", "stump_roundDetailed" };

        private static Transform _cabinRoot;

        /// <summary>Inside the cabin's walls (plus a margin): no grass or flowers growing through the floor.</summary>
        private static bool InCabin(float x, float z, float pad = 0.5f)
        {
            if (_cabinRoot == null) return false;
            Vector3 l = _cabinRoot.InverseTransformPoint(new Vector3(x, _cabinRoot.position.y, z));
            float s = _cabinRoot.lossyScale.x;
            return Mathf.Abs(l.x) < Cabin.HalfWidth + pad / s && Mathf.Abs(l.z) < Cabin.HalfDepth + pad / s;
        }

        private static bool Blocked(float x, float z, float campPad, float pathPad, Vector3 fire)
        {
            if (InCabin(x, z)) return true;
            if (Vector2.Distance(new Vector2(x, z), WorldShape.CampCenter) < WorldShape.CampRadius + campPad) return true;
            if (WorldShape.DistanceToPath(x, z) < pathPad) return true;
            if (WorldShape.IsOnDock(x, z, 3f)) return true;
            if (WorldShape.InMarket(x, z, campPad + 1f)) return true;
            if (WorldShape.InSeafront(x, z, campPad + 1f)) return true;
            if (Vector2.Distance(new Vector2(x, z), new Vector2(fire.x, fire.z)) < 3f) return true;
            return false;
        }

        private static void ScatterNature(Vector3 fire)
        {
            var nature = new GameObject("Nature").transform;
            nature.SetParent(_env, false);
            var trees = new GameObject("Trees").transform;
            trees.SetParent(nature, false);
            var small = new GameObject("Plants").transform;
            small.SetParent(nature, false);
            var water = new GameObject("WaterPlants").transform;
            water.SetParent(nature, false);

            // Trees on a jittered grid, denser away from the shore, pines further out.
            var placed = new List<Vector2>();
            int treeCount = 0;
            for (float gx = -118f; gx <= 118f; gx += 3.4f)
            for (float gz = -118f; gz <= 118f; gz += 3.4f)
            {
                float x = gx + R(-1.4f, 1.4f), z = gz + R(-1.4f, 1.4f);
                float d = WorldShape.ShoreDistance(x, z);
                float r = new Vector2(x, z).magnitude;
                if (d < 3.5f || r > 122f) continue;
                if (Blocked(x, z, 2f, 3.2f, fire)) continue;
                float cluster = Mathf.PerlinNoise(x * 0.045f + 11f, z * 0.045f + 5f);
                float p = Mathf.SmoothStep(0f, 0.28f, Mathf.InverseLerp(3.5f, 16f, d)) + Mathf.SmoothStep(0f, 0.35f, Mathf.InverseLerp(20f, 45f, d)) + Mathf.InverseLerp(60f, 90f, r) * 0.3f;
                p *= cluster > 0.45f ? 1.6f : 0.45f;
                if (_rng.NextDouble() > p) continue;
                bool nearOthers = false;
                foreach (var q in placed) if ((q - new Vector2(x, z)).sqrMagnitude < 5.5f) { nearOthers = true; break; }
                if (nearOthers) continue;
                placed.Add(new Vector2(x, z));

                bool pine = r > 58f || Mathf.PerlinNoise(x * 0.03f + 50f, z * 0.03f) > 0.58f;
                string model = pine ? Pick(Pines) : Pick(Broadleaf);
                if (!pine && _rng.NextDouble() < 0.12) model += "_fall";
                else if (!pine && _rng.NextDouble() < 0.12) model += "_dark";
                if (model == "tree_fat_dark") model = "tree_fat_darkh";
                float scale = pine ? R(3.6f, 5.6f) : R(3.2f, 4.6f);
                var t = Place("NatureKit/" + model, trees, Ground(x, z, 0.08f), R(0, 360), scale);
                if (t == null) continue;
                treeCount++;
                if (r < WorldShape.PlayableRadius + 6f)
                {
                    var cap = t.AddComponent<CapsuleCollider>();
                    cap.radius = 0.06f;
                    cap.height = 0.5f;
                    cap.center = new Vector3(0, 0.25f, 0);
                }
            }

            // Ground cover.
            for (int k = 0; k < 1400; k++)
            {
                float x = R(-78f, 78f), z = R(-78f, 78f);
                float d = WorldShape.ShoreDistance(x, z);
                if (d < 1.2f || Blocked(x, z, -3f, 1.2f, fire)) continue;
                float meadow = Mathf.PerlinNoise(x * 0.08f + 3f, z * 0.08f + 1f);
                double roll = _rng.NextDouble();
                if (roll < 0.55)
                    Place("NatureKit/" + Pick(Grasses), small, Ground(x, z, 0.02f), R(0, 360), R(2.4f, 3.6f), shadows: false);
                else if (roll < 0.72 && meadow > 0.5f)
                    Place("NatureKit/" + Pick(Flowers), small, Ground(x, z, 0.02f), R(0, 360), R(2.4f, 3.2f), shadows: false);
                else if (roll < 0.84 && d > 5f)
                    Place("NatureKit/" + Pick(Bushes), small, Ground(x, z, 0.05f), R(0, 360), R(2.6f, 3.8f));
                else if (roll < 0.9 && d > 8f)
                    Place("NatureKit/" + Pick(Mushrooms), small, Ground(x, z, 0.02f), R(0, 360), R(2.2f, 3f), shadows: false);
                else if (roll < 0.95)
                    Place("NatureKit/" + Pick(SmallRocks), small, Ground(x, z, 0.05f), R(0, 360), R(2f, 3.2f));
                else if (d > 6f)
                {
                    var dw = Place("NatureKit/" + Pick(Deadwood), small, Ground(x, z, 0.03f), R(0, 360), R(2.2f, 3f));
                    if (dw != null) AddBoxCollider(dw, 0.8f);
                }
            }

            // Flower beds around the camp.
            for (int k = 0; k < 60; k++)
            {
                Vector2 p = WorldShape.CampCenter + Random2(R(6f, 11f));
                if (WorldShape.DistanceToPath(p.x, p.y) < 1.5f || InCabin(p.x, p.y)) continue;
                Place("NatureKit/" + Pick(Flowers), small, Ground(p.x, p.y, 0.02f), R(0, 360), R(2.4f, 3.2f), shadows: false);
            }

            // Rocks along the waterline and tufts of beach grass behind it.
            for (int k = 0; k < 200; k++)
            {
                float x = R(-WorldShape.PlayableRadius, WorldShape.PlayableRadius);
                bool rockOut = _rng.NextDouble() < 0.4;
                float z = WorldShape.ShoreZ(x) + (rockOut ? R(-1.5f, 3f) : -R(3f, 11f));
                if (Blocked(x, z, 0f, 2.5f, fire) || WorldShape.IsOnDock(x, z, 3f)) continue;
                if (rockOut)
                {
                    var rock = Place("NatureKit/" + Pick(BigRocks), small, Ground(x, z, 0.15f), R(0, 360), R(1.6f, 3.2f));
                    if (rock != null && WorldShape.TerrainHeight(x, z) > -0.3f) AddBoxCollider(rock, 0.75f);
                }
                else
                {
                    Place("NatureKit/" + Pick(new[] { "grass_leafsLarge", "grass_large", "grass" }), water, Ground(x, z, 0.02f), R(0, 360), R(2.6f, 3.6f), shadows: false);
                }
            }

            Debug.Log($"[SceneBuilder] Placed {treeCount} trees.");
        }

        private static Vector2 Random2(float radius)
        {
            float a = R(0, Mathf.PI * 2f);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        // ------------------------------------------------------------------ characters

        private static Camera BuildCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 48f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 450f;
            cam.clearFlags = CameraClearFlags.Skybox;
            go.AddComponent<AudioListener>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            go.AddComponent<CameraRig>();
            return cam;
        }

        private static CharacterAnimator AttachModel(GameObject owner, string kitPath, float scale)
        {
            var model = Place(kitPath, owner.transform, owner.transform.position, owner.transform.eulerAngles.y, scale, isStatic: false);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            var anim = model.GetComponent<Animation>();
            var ca = model.AddComponent<CharacterAnimator>();
            ca.SetAnimation(anim);
            if (anim != null)
            {
                anim.playAutomatically = true;
                anim.clip = anim.GetClip("idle");
                anim.cullingType = AnimationCullingType.AlwaysAnimate;
            }
            return ca;
        }

        private static GameObject BuildPlayer(Camera cam)
        {
            Vector2 spawn2 = WorldShape.DockShorePoint + WorldShape.DockDirection * 3.2f;
            var go = new GameObject("Player");
            go.tag = "Player";
            go.transform.position = Ground(spawn2.x, spawn2.y, -0.05f);
            go.transform.rotation = Quaternion.LookRotation(new Vector3(-WorldShape.DockDirection.x, 0, -WorldShape.DockDirection.y));

            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.25f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0, 0.65f, 0);
            cc.stepOffset = 0.35f;
            cc.slopeLimit = 50f;

            var anim = AttachModel(go, "MiniCharacters/character-male-a", CharacterScale);
            var controller = go.AddComponent<PlayerController>();
            var rig = cam.GetComponent<CameraRig>();
            controller.Configure(rig, anim);

            var rod = go.AddComponent<FishingRod>();
            var hand = anim.FindBone("arm-right");
            rod.SetHand(hand, new Vector3(0f, -0.2f, 0.06f));
            var fishing = go.AddComponent<FishingController>();
            fishing.Configure(controller, rod, rig);

            float yaw = go.transform.eulerAngles.y;
            rig.Configure(go.transform, yaw, 22f, 8.5f);
            return go;
        }

        private static GameObject BuildMei(GameObject player, out CompanionBrain brain, out CharacterVoice voice)
        {
            var go = new GameObject("Mei");
            Vector3 p = player.transform.position + player.transform.right * 1.8f - player.transform.forward * 0.6f;
            go.transform.position = Ground(p.x, p.z, -0.02f);
            go.transform.rotation = player.transform.rotation;

            var anim = AttachModel(go, "MiniCharacters/character-female-f", CharacterScale);
            go.AddComponent<AudioSource>();
            voice = go.AddComponent<CharacterVoice>();
            voice.Configure(SpeechEngine.VoiceMei, SpeechEngine.VoiceEnglish, 1f);
            var controller = go.AddComponent<CompanionController>();
            var unlocks = go.AddComponent<PhraseUnlockSystem>();
            brain = go.AddComponent<CompanionBrain>();

            var fishing = player.GetComponent<FishingController>();
            controller.Configure(player.transform, anim, voice, fishing);
            brain.Configure(voice, controller, player.transform, fishing);
            var so = new SerializedObject(brain);
            so.FindProperty("phraseUnlockSystem").objectReferenceValue = unlocks;
            so.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        private static void BuildCritters(Vector3 dockEnd, Quaternion dockRot, GameObject player)
        {
            var critters = new GameObject("Critters").transform;
            critters.SetParent(_env, false);

            // Tangyuan (汤圆) the cat, napping at the end of the dock.
            var cat = new GameObject("Tangyuan");
            cat.transform.SetParent(critters, false);
            cat.transform.position = dockEnd + dockRot * new Vector3(-0.7f, 0f, 0.3f);
            cat.transform.rotation = dockRot * Quaternion.Euler(0, -130f, 0);
            var catAnim = AttachModel(cat, "CubePets/animal-cat", 0.3f);
            cat.AddComponent<PetController>().Configure(catAnim, player.transform);

            // A couple of shy deer and bunnies around the woods.
            var spots = new[] { (a: 40f, r: 18f, m: "CubePets/animal-deer", s: 0.55f), (a: 150f, r: 22f, m: "CubePets/animal-deer", s: 0.5f), (a: 250f, r: 9f, m: "CubePets/animal-bunny", s: 0.2f), (a: 300f, r: 12f, m: "CubePets/animal-fox", s: 0.3f) };
            foreach (var s in spots)
            {
                // (a = x position along the beach, r = how far inland)
                float cx = Mathf.Lerp(-60f, 60f, s.a / 360f);
                Vector2 spot = WorldShape.ShorePoint(cx) + new Vector2(0f, -s.r - 6f);
                var go = new GameObject(Path.GetFileName(s.m));
                go.transform.SetParent(critters, false);
                go.transform.position = Ground(spot.x, spot.y, 0.02f);
                go.transform.rotation = Quaternion.Euler(0, R(0, 360), 0);
                var ca = AttachModel(go, s.m, s.s);
                go.AddComponent<AmbientCritter>().Configure(ca, "idle", "eat", "idle");
            }
        }

        // ------------------------------------------------------------------ post + systems

        private static void BuildPostProcessing()
        {
            const string path = ProjectSetup.SettingsFolder + "/ComfyVolume.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.7f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(-0.1f);
            ca.contrast.Override(8f);
            ca.saturation.Override(8f);
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(6f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.22f);
            vig.smoothness.Override(0.45f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("PostProcessing");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        private static void BuildSystems(Transform systems, DayNightCycle dayNight, GameObject player, GameObject mei,
            CompanionBrain brain, CharacterVoice voice, Camera cam)
        {
            var boot = systems.gameObject.AddComponent<GameBootstrap>();
            // Its own root object: it survives the scene reload in its save/load checks, and must not drag other systems along.
            new GameObject("SelfTest").AddComponent<SelfTest>();
            var bso = new SerializedObject(boot);
            bso.FindProperty("dayNight").objectReferenceValue = dayNight;
            bso.ApplyModifiedPropertiesWithoutUndo();

            new GameObject("Audio").AddComponent<AudioManager>().transform.SetParent(systems, false);
            new GameObject("LocalAI").AddComponent<LocalAIServices>().transform.SetParent(systems, false);

            var voiceGo = new GameObject("VoiceChat");
            voiceGo.transform.SetParent(systems, false);
            var mic = voiceGo.AddComponent<MicRecorder>();
            var vc = voiceGo.AddComponent<VoiceChatController>();
            vc.Configure(brain, mic, player.transform);

            new GameObject("HomeItems").AddComponent<HomeItems>().transform.SetParent(systems, false);
            new GameObject("Placement").AddComponent<PlacementController>().transform.SetParent(systems, false);
            player.AddComponent<ShopConversation>();
            var interaction = player.AddComponent<InteractionController>();

            // Weather + rain.
            var weatherGo = new GameObject("Weather");
            weatherGo.transform.SetParent(systems, false);
            var weather = weatherGo.AddComponent<Weather>();
            var rainGo = new GameObject("Rain");
            rainGo.transform.SetParent(weatherGo.transform, false);
            var rain = rainGo.AddComponent<ParticleSystem>();
            var rm = rain.main;
            rm.startLifetime = 1.1f;
            rm.startSpeed = 0f;
            rm.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
            rm.maxParticles = 3000;
            rm.simulationSpace = ParticleSystemSimulationSpace.World;
            var rem = rain.emission;
            rem.rateOverTime = 0f;
            var rsh = rain.shape;
            rsh.shapeType = ParticleSystemShapeType.Box;
            rsh.scale = new Vector3(40f, 1f, 40f);
            var rvel = rain.velocityOverLifetime;
            rvel.enabled = true;
            rvel.space = ParticleSystemSimulationSpace.World;
            rvel.x = new ParticleSystem.MinMaxCurve(-1f, -1f);
            rvel.y = new ParticleSystem.MinMaxCurve(-14f, -14f);
            rvel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var rr = rainGo.GetComponent<ParticleSystemRenderer>();
            rr.renderMode = ParticleSystemRenderMode.Stretch;
            rr.velocityScale = 0.05f;
            rr.lengthScale = 2f;
            rr.sharedMaterial = GameAssets.Instance.rainMaterial;
            rr.shadowCastingMode = ShadowCastingMode.Off;
            weather.SetRain(rain);

            // Fish jumping + fireflies.
            var lifeGo = new GameObject("LakeLife");
            lifeGo.transform.SetParent(systems, false);
            lifeGo.AddComponent<LakeLife>().SetFocus(player.transform);

            var ff = new GameObject("Fireflies").transform;
            ff.SetParent(systems, false);
            var spots = new List<Vector3> { new Vector3(WorldShape.CampCenter.x, 1.2f, WorldShape.CampCenter.y) };
            for (int k = 0; k < 7; k++)
            {
                Vector2 fp = WorldShape.ShorePoint(-54f + k * 18f) + new Vector2(0f, -6f);
                spots.Add(new Vector3(fp.x, WorldShape.TerrainHeight(fp.x, fp.y) + 1.2f, fp.y));
            }
            foreach (var s in spots) BuildFireflies(ff, s);

            // UI.
            var uiGo = new GameObject("UI");
            uiGo.transform.SetParent(systems, false);
            var sleepGo = new GameObject("Sleep");
            sleepGo.transform.SetParent(systems, false);
            sleepGo.AddComponent<SleepSystem>();
            sleepGo.AddComponent<Cosmetics>();

            var ui = uiGo.AddComponent<GameUI>();
            ui.Configure(brain, player.transform, player.GetComponent<FishingController>(), vc, interaction);
        }

        private static void BuildFireflies(Transform parent, Vector3 pos)
        {
            var go = new GameObject("FireflySwarm");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(16f, 2.2f, 16f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.4f;
            noise.scrollSpeed = 0.2f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(0.3f, 0.5f), new GradientAlphaKey(1, 0.75f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = GameAssets.Instance.fireflyMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<Fireflies>().SetSystem(ps, 10f);
        }
    }
}
