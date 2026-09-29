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
using UntitledGame.Environment;
using UntitledGame.Fishing;
using UntitledGame.GenAI;
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
            ScatterNature(fireSpot);

            var systems = new GameObject("Systems").transform;
            var cam = BuildCamera();
            var player = BuildPlayer(cam);
            var mei = BuildMei(player, out var brain, out var voice);
            BuildCritters(dockEnd, dockRot);
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
            else if (d < 2.6f + noise * 1.2f && h < 0.55f)
            {
                c = Color.Lerp(WetSand, Sand, Mathf.InverseLerp(0f, 1.2f, d));
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
            float size = 96f;
            int n = 96;
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
            var mesh = new Mesh { name = "Lake", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 2f, size));
            SaveMesh(mesh, "Lake");

            var go = new GameObject("Lake");
            go.transform.SetParent(_env, false);
            go.transform.position = new Vector3(WorldShape.LakeCenter.x, WorldShape.WaterLevel, WorldShape.LakeCenter.y);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ComfyAssets.WaterMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }

        // ------------------------------------------------------------------ props

        private static GameObject Place(string kitPath, Transform parent, Vector3 pos, float yaw, float scale, bool isStatic = true, bool shadows = true)
        {
            var prefab = ComfyAssets.Model(kitPath);
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

            // Rowboat tied up alongside.
            var boat = Place("PirateKit/boat-row-small", _env, L(width * 0.5f + 1.6f, -0.1f, length - 5f), yaw + 4f, 0.95f, isStatic: false);
            if (boat != null) boat.AddComponent<Floater>().Configure(0.035f, 1.8f, 0.8f);

            dockEnd = L(0f, deck, length - 1.2f);
        }

        // ------------------------------------------------------------------ camp

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

            BuildCabin(camp, center - f * 5.2f - r * 1.0f, yaw);

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
            const float scale = 2.4f;
            var root = new GameObject("Cabin").transform;
            root.SetParent(parent, false);
            pos.y = WorldShape.CampHeight - 0.02f;
            root.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            root.localScale = Vector3.one * scale;

            void Piece(string name, float x, float z, float rotY)
            {
                var prefab = ComfyAssets.Model("HolidayKit/" + name);
                if (prefab == null) return;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                KenneyMaterials.Remap(go, "HolidayKit");
                go.transform.localPosition = new Vector3(x, 0f, z - 0.5f);
                go.transform.localRotation = Quaternion.Euler(0, rotY, 0);
                go.transform.localScale = Vector3.one;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }

            // Floor.
            for (int x = -1; x <= 1; x++)
            for (int z = 0; z <= 1; z++)
                Piece("floor-wood", x, z, 0);
            // Front (+z, facing the lake).
            Piece("cabin-window-a", -1, 1, 0);
            Piece("cabin-doorway", 0, 1, 0);
            Piece("cabin-door-rotate", 0, 1, 0);
            Piece("cabin-window-a", 1, 1, 0);
            // Back.
            Piece("cabin-wall", -1, 0, 180);
            Piece("cabin-window-b", 0, 0, 180);
            Piece("cabin-wall", 1, 0, 180);
            // Sides.
            Piece("cabin-wall", 1, 0, 90);
            Piece("cabin-window-c", 1, 1, 90);
            Piece("cabin-wall", -1, 0, 270);
            Piece("cabin-window-c", -1, 1, 270);
            // Corners.
            Piece("cabin-corner-logs", 1, 1, 0);
            Piece("cabin-corner-logs", 1, 0, 90);
            Piece("cabin-corner-logs", -1, 0, 180);
            Piece("cabin-corner-logs", -1, 1, 270);

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

            var col = root.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.8f, 0f);
            col.size = new Vector3(3.2f, 1.6f, 2.3f);

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

        private static bool Blocked(float x, float z, float campPad, float pathPad, Vector3 fire)
        {
            if (Vector2.Distance(new Vector2(x, z), WorldShape.CampCenter) < WorldShape.CampRadius + campPad) return true;
            if (WorldShape.DistanceToPath(x, z) < pathPad) return true;
            if (WorldShape.IsOnDock(x, z, 3f)) return true;
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
                if (WorldShape.DistanceToPath(p.x, p.y) < 1.5f) continue;
                Place("NatureKit/" + Pick(Flowers), small, Ground(p.x, p.y, 0.02f), R(0, 360), R(2.4f, 3.2f), shadows: false);
            }

            // Shoreline rocks and reeds.
            for (int k = 0; k < 160; k++)
            {
                float a = R(0, Mathf.PI * 2f);
                float rr = WorldShape.LakeRadiusAt(a) + R(-1.6f, 2.2f);
                float x = Mathf.Cos(a) * rr, z = Mathf.Sin(a) * rr;
                if (Blocked(x, z, 0f, 2.5f, fire)) continue;
                if (_rng.NextDouble() < 0.35)
                {
                    var rock = Place("NatureKit/" + Pick(BigRocks), small, Ground(x, z, 0.15f), R(0, 360), R(1.6f, 2.8f));
                    if (rock != null && WorldShape.TerrainHeight(x, z) > -0.3f) AddBoxCollider(rock, 0.75f);
                }
                else
                {
                    Place("NatureKit/" + Pick(new[] { "grass_leafsLarge", "grass_large", "plant_flatTall" }), water, Ground(x, z, 0.02f), R(0, 360), R(3f, 4.2f), shadows: false);
                }
            }

            // Lily pad clusters.
            for (int k = 0; k < 16; k++)
            {
                float a = R(0, Mathf.PI * 2f);
                float rr = WorldShape.LakeRadiusAt(a) - R(2.5f, 8f);
                Vector3 c = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                if (WorldShape.IsOnDock(c.x, c.z, 4f)) continue;
                var cluster = new GameObject("Lilies").transform;
                cluster.SetParent(water, false);
                cluster.position = c;
                cluster.gameObject.layer = 4;
                var trig = cluster.gameObject.AddComponent<SphereCollider>();
                trig.isTrigger = true;
                trig.radius = 2.5f;
                int count = _rng.Next(4, 9);
                for (int j = 0; j < count; j++)
                {
                    Vector2 o = Random2(R(0.2f, 2.2f));
                    var lily = Place("NatureKit/" + (_rng.NextDouble() < 0.6 ? "lily_large" : "lily_small"), cluster, new Vector3(c.x + o.x, 0.02f, c.z + o.y), R(0, 360), R(2.6f, 3.6f), isStatic: false, shadows: false);
                    if (lily != null) lily.AddComponent<Floater>().Configure(0.012f, 1.2f, 0.7f);
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

        private static GameObject BuildMei(GameObject player, out CompanionBrain brain, out CompanionVoice voice)
        {
            var go = new GameObject("Mei");
            Vector3 p = player.transform.position + player.transform.right * 1.8f - player.transform.forward * 0.6f;
            go.transform.position = Ground(p.x, p.z, -0.02f);
            go.transform.rotation = player.transform.rotation;

            var anim = AttachModel(go, "MiniCharacters/character-female-f", CharacterScale);
            go.AddComponent<AudioSource>();
            voice = go.AddComponent<CompanionVoice>();
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

        private static void BuildCritters(Vector3 dockEnd, Quaternion dockRot)
        {
            var critters = new GameObject("Critters").transform;
            critters.SetParent(_env, false);

            // Mochi the cat, napping at the end of the dock.
            var cat = new GameObject("Mochi");
            cat.transform.SetParent(critters, false);
            cat.transform.position = dockEnd + dockRot * new Vector3(-0.7f, 0f, 0.3f);
            cat.transform.rotation = dockRot * Quaternion.Euler(0, -130f, 0);
            var catAnim = AttachModel(cat, "CubePets/animal-cat", 0.3f);
            cat.AddComponent<AmbientCritter>().Configure(catAnim, "idle", "eat", "gesture-positive");

            // A couple of shy deer and bunnies around the woods.
            var spots = new[] { (a: 40f, r: 18f, m: "CubePets/animal-deer", s: 0.55f), (a: 150f, r: 22f, m: "CubePets/animal-deer", s: 0.5f), (a: 250f, r: 9f, m: "CubePets/animal-bunny", s: 0.2f), (a: 300f, r: 12f, m: "CubePets/animal-fox", s: 0.3f) };
            foreach (var s in spots)
            {
                float a = s.a * Mathf.Deg2Rad;
                float rr = WorldShape.LakeRadiusAt(a) + s.r;
                var go = new GameObject(Path.GetFileName(s.m));
                go.transform.SetParent(critters, false);
                go.transform.position = Ground(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr, 0.02f);
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
            CompanionBrain brain, CompanionVoice voice, Camera cam)
        {
            var boot = systems.gameObject.AddComponent<GameBootstrap>();
            systems.gameObject.AddComponent<SelfTest>();
            var bso = new SerializedObject(boot);
            bso.FindProperty("dayNight").objectReferenceValue = dayNight;
            bso.ApplyModifiedPropertiesWithoutUndo();

            new GameObject("Audio").AddComponent<AudioManager>().transform.SetParent(systems, false);
            new GameObject("LocalAI").AddComponent<LocalAIServices>().transform.SetParent(systems, false);

            var voiceGo = new GameObject("VoiceChat");
            voiceGo.transform.SetParent(systems, false);
            var mic = voiceGo.AddComponent<MicRecorder>();
            var vc = voiceGo.AddComponent<VoiceChatController>();
            vc.Configure(brain, mic);

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
                float a = k / 7f * Mathf.PI * 2f + 0.3f;
                float rr2 = WorldShape.LakeRadiusAt(a) + 3f;
                spots.Add(new Vector3(Mathf.Cos(a) * rr2, WorldShape.TerrainHeight(Mathf.Cos(a) * rr2, Mathf.Sin(a) * rr2) + 1.2f, Mathf.Sin(a) * rr2));
            }
            foreach (var s in spots) BuildFireflies(ff, s);

            // UI.
            var uiGo = new GameObject("UI");
            uiGo.transform.SetParent(systems, false);
            var ui = uiGo.AddComponent<GameUI>();
            ui.Configure(brain, voice, mei.transform, player.transform, player.GetComponent<FishingController>(), vc);
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
