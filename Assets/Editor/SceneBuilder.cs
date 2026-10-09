using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Generates the Willow Lake scene from scratch: terrain, sea, dock, camp, nature, market, bus stop, characters,
    /// lighting, post-processing and all runtime systems. Deterministic (fixed seed) and re-runnable.
    /// Every region on the bus route (Regions) shares the layout; region-specific looks (terrain colours, plants,
    /// stall styles, critters) are built side by side under RegionStyle objects, and only the current one is shown.
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
            ScatterDesert(fireSpot);
            ScatterSnow(fireSpot);
            ScatterMars(fireSpot);

            var systems = new GameObject("Systems").transform;
            var cam = BuildCamera();
            var player = BuildPlayer(cam);
            var mei = BuildMei(player, out var brain, out var voice);
            BuildMarket(player);
            BuildBusStop(player);
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

        // ------------------------------------------------------------------ regions

        private static readonly string[] RegionStyles = { null, "desert", "snow", "mars" };

        /// <summary>A parent for one region's look; only region 0 starts visible (Regions.Apply switches at runtime).</summary>
        private static Transform RegionRoot(Transform parent, string name, int region)
        {
            var go = new GameObject($"{name}_{Regions.All[region].id}");
            go.transform.SetParent(parent, false);
            go.AddComponent<RegionStyle>().region = region;
            go.SetActive(region == 0);
            return go.transform;
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
        private static readonly Color DuneA = C("#E6C68E"), DuneB = C("#EED4A2"), Ochre = C("#D4A465"), RedRock = C("#C27A4E"),
            DesertFar = C("#C98E5A"), DryScrub = C("#ABA562"), DesertPlaza = C("#D9BC8C"), DesertPath = C("#BE905E");

        /// <summary>Which region's colours the terrain mesh is being built with.</summary>
        private static int _terrainRegion;

        /// <summary>Distance from the dirt road the bus takes inland (it leaves the stop to the south-west).</summary>
        private static float DistanceToBusRoad(float x, float z) =>
            WorldShape.DistanceToSegment(new Vector2(x, z), WorldShape.BusPosition + new Vector2(-3f, 0f), WorldShape.BusPosition + new Vector2(-34f, -30f));

        private static Color TerrainColor(Vector3 p, Vector3 n)
        {
            if (_terrainRegion == 1) return DesertColor(p, n);
            if (_terrainRegion == 2) return SnowColor(p, n);
            if (_terrainRegion == 3) return MarsColor(p, n);
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
            c = BusStopColor(p, c, Plaza, Dirt, fine);

            float jitter = 1f + ((float)_rng.NextDouble() - 0.5f) * 0.06f;
            return c * jitter;
        }

        /// <summary>The bus stop's gravel pad and the dirt road it leaves by.</summary>
        private static Color BusStopColor(Vector3 p, Color c, Color pad, Color road, float fine)
        {
            float stop = Vector2.Distance(new Vector2(p.x, p.z), WorldShape.BusStopCenter);
            if (stop < WorldShape.BusStopRadius) c = Color.Lerp(c, pad, Mathf.InverseLerp(WorldShape.BusStopRadius, WorldShape.BusStopRadius * 0.7f, stop) * (0.7f + fine * 0.2f));
            float r = DistanceToBusRoad(p.x, p.z);
            if (r < 1.8f + fine * 0.3f) c = Color.Lerp(road, c, Mathf.InverseLerp(0.9f, 2.0f, r) * 0.6f);
            return c;
        }

        private static readonly Color SnowA = C("#EEF3F7"), SnowB = C("#DDE6ED"), IceBlue = C("#BFD3E0"), ColdRock = C("#8C96A1"),
            Shingle = C("#A7A39B"), WetShingle = C("#8A8680"), PackedSnow = C("#C9C3B8"), SnowPlaza = C("#D6DADD");

        /// <summary>The snowy coast: grey shingle at the water, deep snow inland, blue-white drifts, bare rock on steep slopes.</summary>
        private static Color SnowColor(Vector3 p, Vector3 n)
        {
            float d = WorldShape.ShoreDistance(p.x, p.z);
            float r = new Vector2(p.x, p.z).magnitude;
            float h = p.y;
            float noise = Mathf.PerlinNoise(p.x * 0.06f + 3.1f, p.z * 0.06f + 7.7f);
            float fine = Mathf.PerlinNoise(p.x * 0.35f, p.z * 0.35f);
            Color c;
            if (h < WorldShape.WaterLevel - 0.15f)
                c = Color.Lerp(C("#9AA7A6"), C("#3E5560"), Mathf.InverseLerp(0.2f, 3.5f, -h));
            else if (d < 3.5f + noise * 2f && h < 0.7f)
                c = Color.Lerp(WetShingle, Shingle, Mathf.InverseLerp(0f, 1.5f, d));
            else
            {
                c = Color.Lerp(SnowB, SnowA, Mathf.Clamp01(noise * 1.3f));
                c = Color.Lerp(c, IceBlue, Mathf.Clamp01((fine - 0.65f) * 2f) * 0.5f);
                if (d < 7f) c = Color.Lerp(Shingle, c, Mathf.InverseLerp(3.5f, 7f, d));
                if (n.y < 0.84f) c = Color.Lerp(c, ColdRock, Mathf.InverseLerp(0.84f, 0.64f, n.y));
            }

            float path = WorldShape.DistanceToPath(p.x, p.z);
            if (d > 0.4f && path < 1.35f + fine * 0.4f) c = Color.Lerp(PackedSnow, c, Mathf.InverseLerp(0.7f, 1.6f, path) * 0.5f);
            float camp = Vector2.Distance(new Vector2(p.x, p.z), WorldShape.CampCenter);
            if (camp < 4.5f) c = Color.Lerp(c, PackedSnow, Mathf.InverseLerp(4.5f, 2f, camp) * 0.5f);
            float plaza = Vector2.Distance(new Vector2(p.x, p.z), WorldShape.MarketCenter);
            if (plaza < WorldShape.MarketRadius) c = Color.Lerp(c, SnowPlaza, Mathf.InverseLerp(WorldShape.MarketRadius, WorldShape.MarketRadius * 0.7f, plaza) * (0.75f + fine * 0.2f));
            c = BusStopColor(p, c, SnowPlaza, PackedSnow, fine);

            float jitter = 1f + ((float)_rng.NextDouble() - 0.5f) * 0.04f;
            return c * jitter;
        }

        private static readonly Color MarsRust = C("#B5532F"), MarsDust = C("#CC7442"), MarsOchre = C("#D9925A"), MarsBasalt = C("#5E2A20"),
            MarsSlope = C("#8E3A26"), MarsShore = C("#7A3A3A"), MarsWetShore = C("#5A2C34"), MarsPath = C("#9E4E30"), MarsPad = C("#B9A8A2");
        private static List<(Vector2 c, float r)> _craters;

        /// <summary>Crater spots for the Mars ground (a fixed pattern on the land, clear of paths, camp and market).</summary>
        private static List<(Vector2 c, float r)> Craters()
        {
            if (_craters != null) return _craters;
            _craters = new List<(Vector2, float)>();
            var rng = new System.Random(4242);
            for (int k = 0; k < 400 && _craters.Count < 34; k++)
            {
                float x = -110f + (float)rng.NextDouble() * 220f, z = -120f + (float)rng.NextDouble() * 110f;
                float r = 2.5f + (float)rng.NextDouble() * 6f;
                if (WorldShape.ShoreDistance(x, z) < 8f + r || WorldShape.DistanceToPath(x, z) < r + 2f) continue;
                if (Vector2.Distance(new Vector2(x, z), WorldShape.CampCenter) < WorldShape.CampRadius + r + 2f) continue;
                if (WorldShape.InMarket(x, z, r + 2f) || WorldShape.InSeafront(x, z, r + 2f) || WorldShape.InBusStop(x, z, r + 2f)) continue;
                _craters.Add((new Vector2(x, z), r));
            }
            return _craters;
        }

        /// <summary>Mars: rust and ochre dust, dark basalt on the slopes, craters with pale rims, maroon sand at the purple sea.</summary>
        private static Color MarsColor(Vector3 p, Vector3 n)
        {
            float d = WorldShape.ShoreDistance(p.x, p.z);
            float h = p.y;
            float noise = Mathf.PerlinNoise(p.x * 0.06f + 3.1f, p.z * 0.06f + 7.7f);
            float fine = Mathf.PerlinNoise(p.x * 0.35f, p.z * 0.35f);
            Color c;
            if (h < WorldShape.WaterLevel - 0.15f)
                c = Color.Lerp(C("#6E4A6A"), C("#2E1838"), Mathf.InverseLerp(0.2f, 3.5f, -h));
            else if (d < 4f + noise * 2f && h < 0.8f)
                c = Color.Lerp(MarsWetShore, MarsShore, Mathf.InverseLerp(0f, 1.5f, d));
            else
            {
                c = Color.Lerp(MarsRust, MarsDust, noise);
                c = Color.Lerp(c, MarsOchre, Mathf.Clamp01((fine - 0.6f) * 2.2f));
                if (n.y < 0.85f) c = Color.Lerp(c, MarsSlope, Mathf.InverseLerp(0.85f, 0.66f, n.y));
                if (n.y < 0.7f) c = Color.Lerp(c, MarsBasalt, Mathf.InverseLerp(0.7f, 0.55f, n.y));
                foreach (var (cc, r) in Craters())
                {
                    float dist = Vector2.Distance(new Vector2(p.x, p.z), cc);
                    if (dist > r * 1.25f) continue;
                    if (dist < r * 0.85f) c = Color.Lerp(c, MarsBasalt, 0.45f * (1f - dist / (r * 0.85f)) + 0.25f);
                    else c = Color.Lerp(c, MarsOchre, 0.55f * (1f - Mathf.Abs(dist - r) / (r * 0.25f)));
                }
            }

            float path = WorldShape.DistanceToPath(p.x, p.z);
            if (d > 0.4f && path < 1.35f + fine * 0.4f) c = Color.Lerp(MarsPath, c, Mathf.InverseLerp(0.7f, 1.6f, path) * 0.5f);
            float camp = Vector2.Distance(new Vector2(p.x, p.z), WorldShape.CampCenter);
            if (camp < 4.5f) c = Color.Lerp(c, MarsPath, Mathf.InverseLerp(4.5f, 2f, camp) * 0.5f);
            float plaza = Vector2.Distance(new Vector2(p.x, p.z), WorldShape.MarketCenter);
            if (plaza < WorldShape.MarketRadius) c = Color.Lerp(c, MarsPad, Mathf.InverseLerp(WorldShape.MarketRadius, WorldShape.MarketRadius * 0.7f, plaza) * (0.7f + fine * 0.2f));
            c = BusStopColor(p, c, MarsPad, MarsPath, fine);

            float jitter = 1f + ((float)_rng.NextDouble() - 0.5f) * 0.05f;
            return c * jitter;
        }

        private static readonly string[] AlienPlants = { "plant_bushTriangle", "plant_flatTall", "plant_bushLargeTriangle", "mushroom_redTall", "mushroom_tanTall", "mushroom_redGroup" };

        /// <summary>
        /// Mars (region 3): no trees, just rust rocks and tall red spires, glowing crystal clusters, purple alien plants
        /// and cyan mushrooms, a few habitat domes with solar panels, an antenna mast and a rover, and rocks along the
        /// purple sea.
        /// </summary>
        private static void ScatterMars(Vector3 fire)
        {
            var nature = RegionRoot(_env, "Nature", 3);
            var big = new GameObject("Rocks").transform;
            big.SetParent(nature, false);
            var small = new GameObject("Plants").transform;
            small.SetParent(nature, false);
            var base_ = new GameObject("Base").transform;
            base_.SetParent(nature, false);
            const string mars = "mars";
            var rng = new System.Random(9090); // its own sequence (keeps the other regions' layouts as they were)
            float Rr(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            T PickR<T>(IList<T> list) => list[rng.Next(list.Count)];

            // Spires and boulders inland, thicker further away.
            int rocks = 0;
            for (float gx = -118f; gx <= 118f; gx += 5f)
            for (float gz = -118f; gz <= 118f; gz += 5f)
            {
                float x = gx + Rr(-2f, 2f), z = gz + Rr(-2f, 2f);
                float d = WorldShape.ShoreDistance(x, z), r = new Vector2(x, z).magnitude;
                if (d < 6f || r > 122f || Blocked(x, z, 2f, 3.2f, fire)) continue;
                float p = Mathf.InverseLerp(6f, 40f, d) * 0.25f + Mathf.InverseLerp(55f, 100f, r) * 0.35f;
                if (rng.NextDouble() > p) continue;
                bool spire = rng.NextDouble() < 0.4;
                var go = Place("NatureKit/" + (spire ? PickR(new[] { "rock_tallA", "rock_tallB", "rock_tallC", "rock_tallD", "rock_tallE", "rock_tallF", "rock_tallG" }) : PickR(BigRocks)),
                    big, Ground(x, z, 0.2f), Rr(0, 360), spire ? Rr(3f, 7f) : Rr(2f, 4.5f), style: mars);
                if (go == null) continue;
                rocks++;
                if (r < WorldShape.PlayableRadius + 6f) AddBoxCollider(go, 0.7f);
            }

            // Ground cover: small rocks, alien plants and mushrooms, crystals.
            int crystals = 0;
            for (int k = 0; k < 700; k++)
            {
                float x = Rr(-78f, 78f), z = Rr(-78f, 78f);
                float d = WorldShape.ShoreDistance(x, z);
                if (d < 1.5f || Blocked(x, z, -3f, 1.2f, fire)) continue;
                double roll = rng.NextDouble();
                if (roll < 0.4)
                    Place("NatureKit/" + PickR(SmallRocks), small, Ground(x, z, 0.05f), Rr(0, 360), Rr(2f, 3.4f), style: mars);
                else if (roll < 0.62 && d > 4f)
                    Place("NatureKit/" + PickR(AlienPlants), small, Ground(x, z, 0.03f), Rr(0, 360), Rr(2.2f, 3.4f), shadows: false, style: mars);
                else if (roll < 0.7 && d > 5f)
                {
                    Crystal(small, Ground(x, z, 0f), Rr(0.5f, 1.3f), k, light: crystals < 14 && rng.NextDouble() < 0.35);
                    crystals++;
                }
                else if (roll < 0.78)
                    Place("SurvivalKit/" + PickR(new[] { "rock-a", "rock-b", "rock-c" }), small, Ground(x, z, 0.05f), Rr(0, 360), Rr(1.2f, 2.2f), style: mars);
            }

            // The base: habitat domes, solar panels, an antenna and a rover.
            var white = SolidMat("Mars_White", "#E9ECEF");
            var metal = SolidMat("Mars_Metal", "#9AA3AD");
            var panel = SolidMat("Mars_Panel", "#2E3E6E");
            var window = SolidMat("Mars_Window", "#3E6FB0");
            Vector2 cc = WorldShape.CampCenter, mc = WorldShape.MarketCenter;
            foreach (var (dc, rad) in new[] { (cc + new Vector2(-21f, -10f), 3.6f), (cc + new Vector2(-28f, -2f), 2.6f), (mc + new Vector2(16f, -17f), 3.2f) })
            {
                if (Blocked(dc.x, dc.y, 0f, rad + 1f, fire)) continue;
                Vector3 g = Ground(dc.x, dc.y, 0.1f);
                Prim(PrimitiveType.Sphere, base_, g, new Vector3(rad * 2f, rad * 1.5f, rad * 2f), Quaternion.identity, white, "Dome");
                Prim(PrimitiveType.Cylinder, base_, g + Vector3.up * 0.15f, new Vector3(rad * 2.1f, 0.15f, rad * 2.1f), Quaternion.identity, metal);
                Prim(PrimitiveType.Cube, base_, g + new Vector3(0f, rad * 0.45f, rad * 0.96f), new Vector3(rad * 0.5f, rad * 0.35f, 0.1f), Quaternion.identity, window);
                var domeCol = new GameObject("DomeCollider");
                domeCol.transform.SetParent(base_, false);
                domeCol.transform.position = g;
                var sc = domeCol.AddComponent<SphereCollider>();
                sc.radius = rad * 0.9f;
                // Solar panels beside it.
                for (int k = 0; k < 3; k++)
                {
                    Vector3 pp = Ground(dc.x + rad + 2f, dc.y - 2f + k * 1.6f, 0f);
                    Prim(PrimitiveType.Cylinder, base_, pp + Vector3.up * 0.4f, new Vector3(0.08f, 0.4f, 0.08f), Quaternion.identity, metal);
                    Prim(PrimitiveType.Cube, base_, pp + Vector3.up * 0.85f, new Vector3(1.4f, 0.05f, 1.0f), Quaternion.Euler(25f, 90f, 0f), panel);
                }
            }
            Vector2 mast2 = cc + new Vector2(-14f, -16f);
            if (!Blocked(mast2.x, mast2.y, 0f, 2f, fire))
            {
                Vector3 mp = Ground(mast2.x, mast2.y, 0f);
                Prim(PrimitiveType.Cylinder, base_, mp + Vector3.up * 3f, new Vector3(0.12f, 3f, 0.12f), Quaternion.identity, metal);
                Prim(PrimitiveType.Sphere, base_, mp + Vector3.up * 5.6f, new Vector3(1.4f, 0.35f, 1.4f), Quaternion.Euler(30f, 40f, 0f), white);
                var tip = Prim(PrimitiveType.Sphere, base_, mp + Vector3.up * 6.2f, Vector3.one * 0.18f, Quaternion.identity, SolidMat("Mars_Red", "#FF4A3A"));
                AddLight(tip.transform, Vector3.zero, C("#FF4A3A"), 5f, 1.6f, 0.3f, true, 0.7f);
            }
            Vector2 rover2 = mc + new Vector2(12f, -12f);
            if (!Blocked(rover2.x, rover2.y, 0f, 2f, fire))
            {
                var rover = new GameObject("Rover").transform;
                rover.SetParent(base_, false);
                rover.SetPositionAndRotation(Ground(rover2.x, rover2.y, 0f), Quaternion.Euler(0f, 35f, 0f));
                Prim(PrimitiveType.Cube, rover, rover.TransformPoint(new Vector3(0f, 0.75f, 0f)), new Vector3(1.4f, 0.4f, 2.0f), rover.rotation, white);
                Prim(PrimitiveType.Cube, rover, rover.TransformPoint(new Vector3(0f, 1.05f, -0.3f)), new Vector3(1.2f, 0.06f, 1.0f), rover.rotation, panel);
                Prim(PrimitiveType.Cylinder, rover, rover.TransformPoint(new Vector3(0.4f, 1.3f, 0.7f)), new Vector3(0.06f, 0.35f, 0.06f), rover.rotation, metal);
                Prim(PrimitiveType.Cube, rover, rover.TransformPoint(new Vector3(0.4f, 1.68f, 0.7f)), new Vector3(0.3f, 0.18f, 0.18f), rover.rotation, metal);
                foreach (float sx in new[] { -0.8f, 0.8f })
                foreach (float sz in new[] { -0.75f, 0f, 0.75f })
                    Prim(PrimitiveType.Cylinder, rover, rover.TransformPoint(new Vector3(sx, 0.32f, sz)), new Vector3(0.55f, 0.1f, 0.55f), rover.rotation * Quaternion.Euler(0f, 0f, 90f), SolidMat("Bus_Tyre", "#2B2A2A"));
                rover.gameObject.AddComponent<BoxCollider>().size = new Vector3(1.8f, 1.6f, 2.2f);
            }

            // Rocks along the purple sea.
            for (int k = 0; k < 140; k++)
            {
                float x = Rr(-WorldShape.PlayableRadius, WorldShape.PlayableRadius);
                float z = WorldShape.ShoreZ(x) + Rr(-1.5f, 3f);
                if (Blocked(x, z, 0f, 2.5f, fire) || WorldShape.IsOnDock(x, z, 3f)) continue;
                var rock = Place("NatureKit/" + PickR(BigRocks), small, Ground(x, z, 0.15f), Rr(0, 360), Rr(1.4f, 2.8f), style: mars);
                if (rock != null && WorldShape.TerrainHeight(x, z) > -0.3f) AddBoxCollider(rock, 0.75f);
            }
            Debug.Log($"[SceneBuilder] Mars: placed {rocks} rock formations, {crystals} crystal clusters and {Craters().Count} craters.");
        }

        /// <summary>The desert coast: pale dunes, ochre and red rock on the slopes and hills, a little dry scrub near the water.</summary>
        private static Color DesertColor(Vector3 p, Vector3 n)
        {
            float d = WorldShape.ShoreDistance(p.x, p.z);
            float r = new Vector2(p.x, p.z).magnitude;
            float h = p.y;
            float noise = Mathf.PerlinNoise(p.x * 0.06f + 3.1f, p.z * 0.06f + 7.7f);
            float fine = Mathf.PerlinNoise(p.x * 0.35f, p.z * 0.35f);
            float ripples = Mathf.PerlinNoise(p.x * 0.18f + 40f, p.z * 0.5f + 12f);
            Color c;
            if (h < WorldShape.WaterLevel - 0.15f)
                c = Color.Lerp(BedShallow, Color.Lerp(BedDeep, C("#4C6A6A"), 0.5f), Mathf.InverseLerp(0.2f, 3.5f, -h));
            else if (d < 6f + noise * 2f && h < 0.9f)
                c = Color.Lerp(WetSand, DuneB, Mathf.InverseLerp(0f, 1.5f, d));
            else
            {
                c = Color.Lerp(DuneA, DuneB, ripples);
                c = Color.Lerp(c, Ochre, Mathf.Clamp01((noise - 0.55f) * 2.2f));
                c = Color.Lerp(c, DesertFar, Mathf.InverseLerp(55f, 105f, r));
                if (n.y < 0.86f) c = Color.Lerp(c, RedRock, Mathf.InverseLerp(0.86f, 0.66f, n.y));
                // Dry scrub in patches behind the beach.
                if (d > 4f && d < 24f && fine > 0.62f && noise > 0.45f) c = Color.Lerp(c, DryScrub, Mathf.InverseLerp(0.62f, 0.8f, fine) * 0.6f);
            }

            float path = WorldShape.DistanceToPath(p.x, p.z);
            if (d > 0.4f && path < 1.35f + fine * 0.4f) c = Color.Lerp(DesertPath, c, Mathf.InverseLerp(0.7f, 1.6f, path) * 0.5f);
            float camp = Vector2.Distance(new Vector2(p.x, p.z), WorldShape.CampCenter);
            if (camp < 4.5f) c = Color.Lerp(c, DesertPath, Mathf.InverseLerp(4.5f, 2f, camp) * 0.45f);
            float plaza = Vector2.Distance(new Vector2(p.x, p.z), WorldShape.MarketCenter);
            if (plaza < WorldShape.MarketRadius) c = Color.Lerp(c, DesertPlaza, Mathf.InverseLerp(WorldShape.MarketRadius, WorldShape.MarketRadius * 0.7f, plaza) * (0.75f + fine * 0.2f));
            c = BusStopColor(p, c, DesertPlaza, DesertPath, fine);

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

            // One coloured mesh per region (same shape), and one shared collider.
            for (int region = 0; region < Regions.All.Length; region++)
            {
                _terrainRegion = region;
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
                string id = Regions.All[region].id;
                var mesh = mb.ToMesh("Terrain_" + id);
                SaveMesh(mesh, region == 0 ? "Terrain" : "Terrain_" + id);
                var look = RegionRoot(_env, "Terrain", region);
                look.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = look.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ComfyAssets.TerrainMat;
                mr.shadowCastingMode = ShadowCastingMode.On;
                GameObjectUtility.SetStaticEditorFlags(look.gameObject, StaticEditorFlags.BatchingStatic);
            }
            _terrainRegion = 0;

            var go = new GameObject("TerrainCollider");
            go.transform.SetParent(_env, false);

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

        private static GameObject Place(string kitPath, Transform parent, Vector3 pos, float yaw, float scale, bool isStatic = true, bool shadows = true, string style = null)
        {
            // The furniture kit is authored with corner pivots; use the re-pivoted prefab.
            var prefab = kitPath.StartsWith("FurnitureKit/") ? ComfyAssets.RemappedPrefab(kitPath) : ComfyAssets.Model(kitPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[SceneBuilder] Missing model {kitPath}");
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            KenneyMaterials.Remap(go, KenneyMaterials.KitOf(kitPath), style);
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
            // Every region has its own house on the same footprint: Willow Bay's log cabin is region 0's look of each
            // group (BuildHouseStyles adds the others), so Cabin can still hide whole walls between the camera and player.
            var logLook = new Dictionary<Transform, Transform>();
            Transform Log(Transform g)
            {
                if (!logLook.TryGetValue(g, out var r)) logLook[g] = r = RegionRoot(g, "Style", 0);
                return r;
            }

            GameObject Piece(Transform group, string name, float x, float z, float rotY, bool isStatic = true)
            {
                var prefab = ComfyAssets.Model("HolidayKit/" + name);
                if (prefab == null) return null;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, Log(group));
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

            var roofGroup = Group("Roof");
            var roofGo = Log(roofGroup).gameObject;
            roofGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            roofGo.AddComponent<MeshRenderer>().sharedMaterial = ComfyAssets.PlainLitMat;
            GameObjectUtility.SetStaticEditorFlags(roofGo, StaticEditorFlags.BatchingStatic);
            BuildHouseStyles(root, floorGroup, front, back, left, right, roofGroup);

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

            // Porch lantern (the log cabin's) + a warm glow in the window at night.
            var lantern = Place("HolidayKit/lantern-hanging", RegionRoot(root, "Porch", 0), root.TransformPoint(new Vector3(0.62f, 0.98f, 1.12f)), yaw, 1f);
            if (lantern != null) AddLight(lantern.transform, new Vector3(0, -0.3f, 0.2f), C("#FFB866"), 7f, 2.2f, 0f, true, 0.7f);
            var inside = new GameObject("WindowGlow").transform;
            inside.SetParent(root, false);
            inside.localPosition = new Vector3(0, 0.5f, 0.2f);
            AddLight(inside, Vector3.zero, C("#FFB060"), 6f, 1.6f, 0f, true, 0f);
        }

        /// <summary>
        /// The other regions' houses, on the log cabin's footprint (cabin-local units: x ±1.5, z ±1.0, door gap ±0.225 wide
        /// and 0.78 high in the front wall at +z). Each region's pieces go under the same groups as the cabin's
        /// (Front/Back/Left/Right/Roof/Floor), in a RegionStyle root, plus its own door leaf named "door".
        /// - Golden Sand Bay: an adobe house of the Taklamakan oases: thick earthen walls, a flat roof with beam ends,
        ///   a painted blue door, and a grapevine trellis over the porch; a carpet on the floor.
        /// - Snow Bay: a Dongbei red-brick house: a steep grey roof under snow, red lanterns, couplets and a 福 on the
        ///   door, paper-cut windows, strings of corn and icicles.
        /// - Mars: a habitat module: white panels with an orange stripe, a curved roof with solar panels and an antenna,
        ///   portholes and an airlock door; a metal floor.
        /// </summary>
        private static void BuildHouseStyles(Transform root, Transform floor, Transform front, Transform back, Transform left, Transform right, Transform roof)
        {
            const float t = 0.28f, door = 0.225f, doorH = 0.78f;
            var groups = new Dictionary<string, Transform> { { "Floor", floor }, { "Front", front }, { "Back", back }, { "Left", left }, { "Right", right }, { "Roof", roof } };

            for (int region = 1; region < Regions.All.Length; region++)
            {
                string id = Regions.All[region].id;
                var mbs = new Dictionary<string, MeshBuilder>();
                MeshBuilder M(string g) => mbs.TryGetValue(g, out var mb) ? mb : (mbs[g] = new MeshBuilder());
                Quaternion q0 = Quaternion.identity;

                float H = id == "snow" ? 1.0f : 1.05f;
                Color wall = id == "desert" ? C("#C9A26E") : id == "snow" ? C("#A9473A") : C("#E9ECEF");
                Color baseBand = id == "desert" ? C("#B08655") : id == "snow" ? C("#7D7A78") : C("#5E646C");

                // Walls (front with the doorway, back, sides), and a darker band along the bottom.
                void Walls(float y0, float y1, Color c, float extra)
                {
                    float h = y1 - y0, yc = (y0 + y1) * 0.5f, th = t + extra;
                    float sideLen = 1.5f + th / 2 - door;
                    M("Front").Box(new Vector3(-(door + sideLen / 2), yc, 1.0f), new Vector3(sideLen, h, th), q0, c);
                    M("Front").Box(new Vector3(door + sideLen / 2, yc, 1.0f), new Vector3(sideLen, h, th), q0, c);
                    if (y1 > doorH) M("Front").Box(new Vector3(0, (Mathf.Max(y0, doorH) + y1) * 0.5f, 1.0f), new Vector3(door * 2, y1 - Mathf.Max(y0, doorH), th), q0, c);
                    M("Back").Box(new Vector3(0, yc, -1.0f), new Vector3(3f + th, h, th), q0, c);
                    M("Left").Box(new Vector3(-1.5f, yc, 0), new Vector3(th, h, 2f + th), q0, c);
                    M("Right").Box(new Vector3(1.5f, yc, 0), new Vector3(th, h, 2f + th), q0, c);
                }
                Walls(0f, H, wall, 0f);
                Walls(0f, 0.15f, baseBand, 0.03f);

                // Windows on the front (either side of the door) and the back.
                Color frame = id == "desert" ? C("#3E7FA8") : id == "snow" ? C("#EDE6D8") : C("#9AA3AD");
                Color glass = id == "desert" ? C("#2A2018") : id == "snow" ? C("#2E3A48") : C("#2E4E7A");
                foreach (float wx in new[] { -0.95f, 0.95f })
                {
                    M("Front").Box(new Vector3(wx, 0.56f, 1.0f + t / 2 + 0.01f), new Vector3(0.38f, 0.34f, 0.04f), q0, frame);
                    M("Front").Box(new Vector3(wx, 0.56f, 1.0f + t / 2 + 0.025f), new Vector3(0.28f, 0.25f, 0.03f), q0, glass);
                    if (id == "snow") M("Front").Box(new Vector3(wx, 0.56f, 1.0f + t / 2 + 0.04f), new Vector3(0.1f, 0.1f, 0.01f), Quaternion.Euler(0, 0, 45f), C("#C8302A")); // paper-cut
                }
                M("Back").Box(new Vector3(0f, 0.56f, -1.0f - t / 2 - 0.01f), new Vector3(0.38f, 0.34f, 0.04f), q0, frame);
                M("Back").Box(new Vector3(0f, 0.56f, -1.0f - t / 2 - 0.025f), new Vector3(0.28f, 0.25f, 0.03f), q0, glass);

                // Floor.
                M("Floor").Box(new Vector3(0, 0.0375f, 0), new Vector3(3f, 0.075f, 2f), q0, id == "desert" ? C("#B99468") : id == "snow" ? C("#9A6B45") : C("#8A9098"), bottom: false);

                // The door leaf turns on its hinge (Cabin swings every leaf named "door").
                var doorLeaf = new MeshBuilder();
                Color doorC = id == "desert" ? C("#2F7F9A") : id == "snow" ? C("#B8322E") : C("#9AA3AD");
                doorLeaf.Box(new Vector3(door, doorH / 2, 0), new Vector3(door * 2, doorH, 0.05f), q0, doorC);

                if (id == "desert")
                {
                    // Flat roof with a parapet and beam ends; a grapevine trellis over the porch.
                    M("Roof").Box(new Vector3(0, H + 0.04f, 0), new Vector3(3.3f, 0.08f, 2.3f), q0, C("#B98F5C"));
                    foreach (var (c, sz) in new[] { (new Vector3(0, H + 0.13f, 1.12f), new Vector3(3.3f, 0.1f, 0.08f)), (new Vector3(0, H + 0.13f, -1.12f), new Vector3(3.3f, 0.1f, 0.08f)),
                                                    (new Vector3(1.62f, H + 0.13f, 0), new Vector3(0.08f, 0.1f, 2.3f)), (new Vector3(-1.62f, H + 0.13f, 0), new Vector3(0.08f, 0.1f, 2.3f)) })
                        M("Roof").Box(c, sz, q0, C("#A87E4E"));
                    for (float bx = -1.4f; bx <= 1.41f; bx += 0.35f)
                        M("Roof").Box(new Vector3(bx, H - 0.04f, 1.2f), new Vector3(0.07f, 0.07f, 0.26f), q0, C("#7A5530"));
                    Color post = C("#8A6040"), vine = C("#5E8A3A"), vine2 = C("#78A04A"), grape = C("#6B3A8A");
                    foreach (float px in new[] { -1.25f, 1.25f })
                        M("Roof").Box(new Vector3(px, (H + 0.25f) / 2, 1.95f), new Vector3(0.06f, H + 0.25f, 0.06f), q0, post);
                    M("Roof").Box(new Vector3(0, H + 0.22f, 1.95f), new Vector3(2.7f, 0.05f, 0.06f), q0, post);
                    for (float sx = -1.2f; sx <= 1.21f; sx += 0.3f)
                        M("Roof").Box(new Vector3(sx, H + 0.25f, 1.55f), new Vector3(0.04f, 0.04f, 0.9f), q0, post);
                    var rng = new System.Random(77);
                    for (int k = 0; k < 26; k++)
                    {
                        float lx = -1.3f + (float)rng.NextDouble() * 2.6f, lz = 1.15f + (float)rng.NextDouble() * 0.85f;
                        M("Roof").Box(new Vector3(lx, H + 0.29f, lz), new Vector3(0.22f, 0.06f, 0.2f), Quaternion.Euler(0, (float)rng.NextDouble() * 90f, 0), k % 2 == 0 ? vine : vine2);
                        if (k % 3 == 0) M("Roof").Box(new Vector3(lx, H + 0.17f, lz), new Vector3(0.06f, 0.12f, 0.06f), q0, grape);
                    }
                    // The door's gold studs, and a carpet on the floor.
                    for (int k = 0; k < 6; k++)
                        doorLeaf.Box(new Vector3(door + (k % 2 == 0 ? -0.1f : 0.1f), 0.15f + (k / 2) * 0.22f, 0.03f), new Vector3(0.03f, 0.03f, 0.02f), q0, C("#E2B23E"));
                    M("Floor").Box(new Vector3(0.1f, 0.078f, -0.05f), new Vector3(2.1f, 0.006f, 1.3f), q0, C("#E2B23E"), bottom: false);
                    M("Floor").Box(new Vector3(0.1f, 0.082f, -0.05f), new Vector3(1.9f, 0.006f, 1.1f), q0, C("#A8323A"), bottom: false);
                    M("Floor").Box(new Vector3(0.1f, 0.086f, -0.05f), new Vector3(0.5f, 0.006f, 0.5f), Quaternion.Euler(0, 45f, 0), C("#2F5A8A"), bottom: false);
                }
                else if (id == "snow")
                {
                    // A steep gable roof of grey tiles under thick snow, brick gables and chimney, icicles and red lanterns.
                    float eave = H - 0.03f, ridge = 1.95f, over = 0.3f, x0 = -1.5f - over, x1 = 1.5f + over, zF = 1.0f + over, zB = -1.0f - over;
                    void Slope(float zEave, Color c, float lift, float thick, float trim)
                    {
                        Vector3 a = new Vector3(0, eave, zEave), b = new Vector3(0, ridge, 0);
                        Quaternion q = Quaternion.LookRotation(b - a, Vector3.up);
                        M("Roof").Box((a + b) * 0.5f + q * Vector3.up * lift, new Vector3(x1 - x0 - trim, thick, Vector3.Distance(a, b) + 0.04f - trim), q, c);
                    }
                    foreach (float ze in new[] { zF, zB })
                    {
                        Slope(ze, C("#5E6670"), 0.05f, 0.1f, 0f);
                        Slope(ze, C("#F1F5F8"), 0.13f, 0.08f, 0.08f);
                    }
                    foreach (float gx in new[] { -1.5f, 1.5f })
                    {
                        Vector3 g0 = new Vector3(gx, eave, -1.0f), g1 = new Vector3(gx, eave, 1.0f), g2 = new Vector3(gx, ridge - 0.05f, 0f);
                        M("Roof").Triangle(g0, g1, g2, wall);
                        M("Roof").Triangle(g0, g2, g1, wall);
                    }
                    M("Roof").Box(new Vector3(0.9f, 1.85f, -0.45f), new Vector3(0.3f, 0.7f, 0.3f), q0, wall);
                    M("Roof").Box(new Vector3(0.9f, 2.23f, -0.45f), new Vector3(0.36f, 0.07f, 0.36f), q0, C("#F1F5F8"));
                    for (float ix = -1.65f; ix <= 1.66f; ix += 0.22f)
                        M("Roof").Box(new Vector3(ix, eave - 0.07f - (Mathf.Abs(ix * 7f) % 0.06f), zF - 0.02f), new Vector3(0.025f, 0.12f + (Mathf.Abs(ix * 13f) % 0.08f), 0.025f), q0, C("#CFE6F2"));
                    // Couplets either side of the door, and a 福 diamond on it.
                    foreach (float cx in new[] { -0.33f, 0.33f })
                    {
                        M("Front").Box(new Vector3(cx, 0.46f, 1.0f + t / 2 + 0.01f), new Vector3(0.09f, 0.62f, 0.02f), q0, C("#C8302A"));
                        for (int k = 0; k < 4; k++) M("Front").Box(new Vector3(cx, 0.24f + k * 0.15f, 1.0f + t / 2 + 0.022f), new Vector3(0.04f, 0.04f, 0.01f), q0, C("#E8B83A"));
                    }
                    M("Front").Box(new Vector3(0, 0.88f, 1.0f + t / 2 + 0.01f), new Vector3(0.62f, 0.09f, 0.02f), q0, C("#C8302A"));
                    doorLeaf.Box(new Vector3(door, 0.5f, 0.035f), new Vector3(0.18f, 0.18f, 0.02f), Quaternion.Euler(0, 0, 45f), C("#E8B83A"));
                    doorLeaf.Box(new Vector3(door, 0.5f, 0.045f), new Vector3(0.12f, 0.12f, 0.01f), Quaternion.Euler(0, 0, 45f), C("#C8302A"));
                    // Strings of corn on the side wall.
                    for (int k = 0; k < 3; k++)
                        M("Right").Box(new Vector3(1.5f + t / 2 + 0.05f, 0.55f, 0.2f + k * 0.18f), new Vector3(0.08f, 0.38f, 0.08f), q0, C("#E8B83A"));
                    // Red lanterns at the eaves (lit at night).
                    var look = RegionRoot(roof, "Lanterns", region);
                    foreach (float lx in new[] { -0.62f, 0.62f })
                    {
                        Vector3 lp = root.TransformPoint(new Vector3(lx * 1.25f, 0.8f, 1.24f));
                        float k = 1f / root.lossyScale.x; // world-sized under the scaled cabin
                        var lantern = Prim(PrimitiveType.Sphere, look, lp, new Vector3(0.42f, 0.36f, 0.42f) * k, root.rotation, SolidMat("Lantern_Red", "#D9261E"), "Lantern");
                        Prim(PrimitiveType.Cylinder, look, lp + Vector3.up * 0.19f, new Vector3(0.2f, 0.03f, 0.2f) * k, root.rotation, SolidMat("Lantern_Gold", "#E8B83A"));
                        Prim(PrimitiveType.Cylinder, look, lp - Vector3.up * 0.19f, new Vector3(0.2f, 0.03f, 0.2f) * k, root.rotation, SolidMat("Lantern_Gold", "#E8B83A"));
                        AddLight(lantern.transform, Vector3.zero, C("#FF5A3A"), 6f, 1.8f, 0f, true, 0.8f);
                    }
                }
                else // mars
                {
                    // An orange stripe round the module, a curved roof, solar panels and an antenna.
                    Walls(0.44f, 0.52f, C("#E8743B"), 0.03f);
                    const int segs = 10;
                    float rad = 1.16f;
                    for (int k = 0; k < segs; k++)
                    {
                        float a0 = Mathf.PI * k / segs, a1 = Mathf.PI * (k + 1) / segs, am = (a0 + a1) * 0.5f;
                        Vector3 mid = new Vector3(0, H + Mathf.Sin(am) * rad, Mathf.Cos(am) * rad);
                        float len = 2f * rad * Mathf.Sin(Mathf.PI / segs / 2f) + 0.02f;
                        var q = Quaternion.Euler(-(am * Mathf.Rad2Deg - 90f), 0, 0);
                        M("Roof").Box(mid, new Vector3(3.2f, 0.08f, len), q, C("#E9ECEF"));
                        foreach (float rx in new[] { -1.62f, 0f, 1.62f })
                            M("Roof").Box(mid + q * Vector3.up * 0.03f + Vector3.right * rx, new Vector3(0.08f, 0.1f, len), q, C("#9AA3AD"));
                        foreach (float ex in new[] { -1.6f, 1.6f }) // end caps
                        {
                            Vector3 c = new Vector3(ex, H, 0), p0 = new Vector3(ex, H + Mathf.Sin(a0) * rad, Mathf.Cos(a0) * rad), p1 = new Vector3(ex, H + Mathf.Sin(a1) * rad, Mathf.Cos(a1) * rad);
                            M("Roof").Triangle(c, p0, p1, C("#D5DAE0"));
                            M("Roof").Triangle(c, p1, p0, C("#D5DAE0"));
                        }
                    }
                    M("Roof").Box(new Vector3(0.7f, H + rad + 0.12f, 0.15f), new Vector3(1.0f, 0.04f, 0.7f), Quaternion.Euler(18f, 0, 0), C("#2E3E6E"));
                    M("Roof").Box(new Vector3(0.7f, H + rad + 0.04f, 0.15f), new Vector3(0.06f, 0.14f, 0.06f), q0, C("#9AA3AD"));
                    M("Roof").Box(new Vector3(-1.0f, H + rad + 0.25f, -0.3f), new Vector3(0.03f, 0.55f, 0.03f), q0, C("#9AA3AD"));
                    M("Roof").Box(new Vector3(-1.0f, H + rad + 0.55f, -0.3f), new Vector3(0.07f, 0.07f, 0.07f), q0, C("#FF4A3A"));
                    // The airlock door: a small window and a hazard stripe.
                    doorLeaf.Box(new Vector3(door, 0.58f, 0.035f), new Vector3(0.16f, 0.12f, 0.02f), q0, C("#2E4E7A"));
                    doorLeaf.Box(new Vector3(door, 0.12f, 0.035f), new Vector3(door * 2, 0.06f, 0.02f), q0, C("#E8B83A"));
                    // A metal floor with grid lines.
                    for (float gx = -1.0f; gx <= 1.01f; gx += 0.5f) M("Floor").Box(new Vector3(gx, 0.077f, 0), new Vector3(0.015f, 0.004f, 2f), q0, C("#6E747C"), bottom: false);
                    for (float gz = -0.5f; gz <= 0.51f; gz += 0.5f) M("Floor").Box(new Vector3(0, 0.077f, gz), new Vector3(3f, 0.004f, 0.015f), q0, C("#6E747C"), bottom: false);
                }

                foreach (var kv in mbs)
                {
                    var look = RegionRoot(groups[kv.Key], "Style", region);
                    var mesh = kv.Value.ToMesh($"House_{id}_{kv.Key}");
                    SaveMesh(mesh, $"House_{id}_{kv.Key}");
                    look.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    look.gameObject.AddComponent<MeshRenderer>().sharedMaterial = ComfyAssets.PlainLitMat;
                }
                var leafRoot = RegionRoot(front, "Door", region);
                var leaf = new GameObject("door");
                leaf.transform.SetParent(leafRoot, false);
                leaf.transform.localPosition = new Vector3(-door, 0f, 1.0f + t / 2 + 0.03f);
                var leafMesh = doorLeaf.ToMesh($"House_{id}_Door");
                SaveMesh(leafMesh, $"House_{id}_Door");
                var leafGo = new GameObject("Leaf");
                leafGo.transform.SetParent(leaf.transform, false);
                leafGo.AddComponent<MeshFilter>().sharedMesh = leafMesh;
                leafGo.AddComponent<MeshRenderer>().sharedMaterial = ComfyAssets.PlainLitMat;
            }
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

        private static readonly string[] MaleModels = { "male-b", "male-c", "male-d", "male-e", "male-f" };
        private static readonly string[] FemaleModels = { "female-a", "female-b", "female-c", "female-d", "female-e" };

        /// <summary>Willow Bay keepers keep their models; the others get one that matches them, varied by stall and region.</summary>
        private static string KeeperModel(ShopDef shop, int stall, int region)
        {
            if (region == 0) return KeeperModels[stall];
            var list = shop.female ? FemaleModels : MaleModels;
            return "MiniCharacters/character-" + list[(stall * 3 + region * 2) % list.Length];
        }

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

            for (int i = 0; i < WorldShape.StallCount; i++)
            {
                var shop = Catalog.StallShops(0).First(sh => sh.stall == i);
                Vector2 s2 = WorldShape.StallPosition(i);
                Vector3 pos = Ground(s2.x, s2.y, 0.02f);
                Vector2 f2 = WorldShape.StallFront(i);
                Vector3 front = new Vector3(f2.x, 0f, f2.y);
                float yaw = Quaternion.LookRotation(front).eulerAngles.y;

                var stallRoot = new GameObject("Stall_" + shop.id).transform;
                stallRoot.SetParent(market, false);
                stallRoot.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

                // Stall: scale to ~3.4 m wide, open side towards the plaza. One per region's style (same model, recoloured).
                const float scale = StallScale;
                float depth = 0f, counterY = 0f;
                Vector3 rightDir = Vector3.Cross(Vector3.up, front);
                for (int region = 0; region < Regions.All.Length; region++)
                {
                    var look = RegionRoot(stallRoot, "Style", region);
                    // Measure the footprint unrotated (the model's long side runs along its local Z), then turn it.
                    var stall = Place(StallModels[i], look, pos, 0f, scale, style: RegionStyles[region]);
                    Bounds local = ModelBounds(stall);
                    depth = StallYawOffset % 180f == 0f ? local.size.z : local.size.x;
                    var col = stall.AddComponent<BoxCollider>();
                    col.center = stall.transform.InverseTransformPoint(local.center);
                    col.size = local.size / scale * 0.85f;
                    stall.transform.rotation = Quaternion.Euler(0f, yaw + StallYawOffset, 0f);
                    Bounds b = ModelBounds(stall);
                    counterY = b.min.y + b.size.y * CounterHeightFraction;

                    // A lantern beside each stall.
                    var lamp = Place("FantasyTown/lantern", look, Ground(pos.x + rightDir.x * 2.3f + front.x * 0.8f, pos.z + rightDir.z * 2.3f + front.z * 0.8f), 0f, 1.25f, style: RegionStyles[region]);
                    if (lamp != null) AddLight(lamp.transform, new Vector3(0, 1.38f, 0), C("#FFC477"), 8f, 2.0f, 0f, false, 0.9f);
                    if (region == 1) DesertStallDressing(look, pos, front, rightDir, i);
                    if (region == 2) SnowStallDressing(look, pos, front, rightDir, i);
                    if (region == 3) MarsStallDressing(look, pos, front, rightDir, i);
                }

                // The shopkeeper behind the counter, one for each region (only the current region's is there), with
                // their goods on the counter.
                Vector3 right = Vector3.Cross(Vector3.up, front);
                Vector3 counterFront = pos + front * CounterForward;
                for (int region = 0; region < Regions.All.Length; region++)
                {
                    var keeperShop = Catalog.ShopFor(shop.role, region);
                    var home = RegionRoot(stallRoot, "Keeper", region);
                    var keeperGo = new GameObject(keeperShop.keeperEnglish);
                    keeperGo.transform.SetParent(home, false);
                    keeperGo.transform.position = pos - front * (depth * 0.5f + KeeperBehind);
                    keeperGo.transform.rotation = Quaternion.LookRotation(front);
                    string model = KeeperModel(keeperShop, i, region);
                    var anim = AttachModel(keeperGo, model, CharacterScale);
                    OutfitBuilder.Dress(keeperGo, anim, model, i + region * 10, OutfitBuilder.Kind.Keeper, region);
                    keeperGo.AddComponent<AudioSource>();
                    var voice = keeperGo.AddComponent<CharacterVoice>();
                    voice.Configure(keeperShop.voice, "", keeperShop.pitch);
                    var brain = keeperGo.AddComponent<ShopkeeperBrain>();
                    brain.Configure(keeperShop.id, voice, player.transform);
                    keeperGo.AddComponent<TalkingHead>().Configure(anim, voice);
                    BuildDisplay(keeperShop, home, counterFront, right, counterY, yaw);
                }
            }

            // A bit of market clutter and a sign at the entrance.
            Vector2 entrance = c2 - new Vector2(WorldShape.MarketRadius * 0.75f, 0f);
            Place("NatureKit/sign", market, Ground(entrance.x, entrance.y + 2f), 90f + 180f, 2.8f);
            // (Kept clear of the bookshop and trainer stalls north and south of the entrance.)
            Place("PirateKit/barrel", market, Ground(c2.x - 10.6f, c2.y + 8.2f), 0f, 0.55f);
            Place("PirateKit/crate", market, Ground(c2.x - 11.4f, c2.y + 7.4f), 20f, 0.55f);
            Place("FantasyTown/stall-bench", market, Ground(c2.x + 1.5f, c2.y - 0.5f), 0f, 1.6f);

            BuildCrabPots(market);
            BuildGameStalls(market);
        }

        /// <summary>Desert market touches for a stall: a woven rug in front, clay jars, and a palm or cactus pot beside it.</summary>
        private static void DesertStallDressing(Transform parent, Vector3 pos, Vector3 front, Vector3 right, int i)
        {
            string[] rugs = { "#B5483A", "#2F4D7A", "#C7832F", "#7A3B5C" };
            var rug = SolidMat("Rug_" + (i % rugs.Length), rugs[i % rugs.Length]);
            var trim = SolidMat("Rug_Trim", "#EBD9B0");
            float yaw = Quaternion.LookRotation(front).eulerAngles.y;
            Vector3 rp = Ground(pos.x + front.x * 1.9f, pos.z + front.z * 1.9f, -0.015f);
            Prim(PrimitiveType.Cube, parent, rp, new Vector3(2.1f, 0.02f, 1.2f), Quaternion.Euler(0f, yaw, 0f), trim);
            Prim(PrimitiveType.Cube, parent, rp + Vector3.up * 0.006f, new Vector3(1.9f, 0.02f, 1.0f), Quaternion.Euler(0f, yaw, 0f), rug);
            var clay = SolidMat("Clay_Jar", "#C0703F");
            for (int k = 0; k < 2; k++)
            {
                Vector3 jp = Ground(pos.x - right.x * (2.0f + k * 0.45f) + front.x * (0.6f - k * 0.3f), pos.z - right.z * (2.0f + k * 0.45f) + front.z * (0.6f - k * 0.3f), 0f);
                float hgt = k == 0 ? 0.55f : 0.4f;
                Prim(PrimitiveType.Sphere, parent, jp + Vector3.up * hgt * 0.5f, new Vector3(0.42f, hgt, 0.42f) * (k == 0 ? 1f : 0.85f), Quaternion.identity, clay);
                Prim(PrimitiveType.Cylinder, parent, jp + Vector3.up * hgt * 0.98f, new Vector3(0.18f, 0.05f, 0.18f), Quaternion.identity, clay);
            }
            if (i % 2 == 0)
                Place("NatureKit/cactus_short", parent, Ground(pos.x + right.x * 2.9f - front.x * 0.6f, pos.z + right.z * 2.9f - front.z * 0.6f), (i * 47) % 360, 2.4f, style: "desert");
        }

        /// <summary>Space-station touches for a stall on Mars: air tanks, a little antenna with a red light, a crystal.</summary>
        private static void MarsStallDressing(Transform parent, Vector3 pos, Vector3 front, Vector3 right, int i)
        {
            var white = SolidMat("Mars_White", "#E9ECEF");
            var orange = SolidMat("Mars_Orange", "#E8743B");
            for (int k = 0; k < 2; k++)
            {
                Vector3 tp = Ground(pos.x - right.x * (2.0f + k * 0.4f) + front.x * 0.5f, pos.z - right.z * (2.0f + k * 0.4f) + front.z * 0.5f, 0f);
                Prim(PrimitiveType.Cylinder, parent, tp + Vector3.up * 0.45f, new Vector3(0.3f, 0.45f, 0.3f), Quaternion.identity, white);
                Prim(PrimitiveType.Cylinder, parent, tp + Vector3.up * 0.7f, new Vector3(0.31f, 0.05f, 0.31f), Quaternion.identity, orange);
            }
            Vector3 ap = Ground(pos.x + right.x * 2.6f - front.x * 0.8f, pos.z + right.z * 2.6f - front.z * 0.8f, 0f);
            Prim(PrimitiveType.Cylinder, parent, ap + Vector3.up * 1.1f, new Vector3(0.05f, 1.1f, 0.05f), Quaternion.identity, SolidMat("Mars_Metal", "#9AA3AD"));
            var tip = Prim(PrimitiveType.Sphere, parent, ap + Vector3.up * 2.25f, Vector3.one * 0.12f, Quaternion.identity, SolidMat("Mars_Red", "#FF4A3A"));
            AddLight(tip.transform, Vector3.zero, C("#FF4A3A"), 3f, 1.2f, 0.2f, true, 0.5f);
            if (i % 2 == 0) Crystal(parent, Ground(pos.x + right.x * 2.2f + front.x * 0.9f, pos.z + right.z * 2.2f + front.z * 0.9f, 0f), 0.6f, i);
        }

        /// <summary>A cluster of glowing alien crystals (a few long prisms leaning outwards).</summary>
        private static void Crystal(Transform parent, Vector3 at, float size, int seed, bool light = false)
        {
            var rng = new System.Random(seed * 131 + 7);
            float Rr(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var mat = SolidMat(seed % 2 == 0 ? "Crystal_Violet" : "Crystal_Cyan", seed % 2 == 0 ? "#B48CFF" : "#5FE8E0");
            int n = 3 + rng.Next(3);
            for (int k = 0; k < n; k++)
            {
                float h = size * Rr(0.6f, 1.4f);
                var q = Quaternion.Euler(Rr(-25f, 25f), Rr(0f, 360f), Rr(-25f, 25f));
                Prim(PrimitiveType.Cube, parent, at + q * Vector3.up * h * 0.45f, new Vector3(size * 0.22f, h, size * 0.22f), q, mat, "Crystal");
            }
            if (light) AddLight(parent, parent.InverseTransformPoint(at + Vector3.up * size * 0.8f), seed % 2 == 0 ? C("#B48CFF") : C("#5FE8E0"), 6f, 1.8f, 0f, false, 1.1f);
        }

        /// <summary>Snowy market touches: drifts against the stall, a stack of firewood, and a snowman now and then.</summary>
        private static void SnowStallDressing(Transform parent, Vector3 pos, Vector3 front, Vector3 right, int i)
        {
            Place("HolidayKit/snow-pile", parent, Ground(pos.x - right.x * 2.1f - front.x * 0.4f, pos.z - right.z * 2.1f - front.z * 0.4f, 0.05f), (i * 61) % 360, 1.6f);
            Place("HolidayKit/snow-pile", parent, Ground(pos.x + right.x * 1.9f - front.x * 1.0f, pos.z + right.z * 1.9f - front.z * 1.0f, 0.05f), (i * 37) % 360, 1.2f);
            Place("NatureKit/log_stack", parent, Ground(pos.x - right.x * 2.4f + front.x * 0.7f, pos.z - right.z * 2.4f + front.z * 0.7f), Quaternion.LookRotation(front).eulerAngles.y + 90f, 1.8f, style: "snow");
            if (i % 3 == 1)
                Place("HolidayKit/snowman", parent, Ground(pos.x + right.x * 2.9f + front.x * 0.6f, pos.z + right.z * 2.9f + front.z * 0.6f), Quaternion.LookRotation(front).eulerAngles.y, 1.5f);
        }

        /// <summary>
        /// The bus stop south of the camp-market path: a little green-and-cream bus parked facing west (door on the path
        /// side), a bench, a sign post (BusStop puts up the 汽车站 board), and 张师傅 the driver by the door, who only appears
        /// once the first HSK test is passed.
        /// </summary>
        private static void BuildBusStop(GameObject player)
        {
            var root = new GameObject("BusStop").transform;
            root.SetParent(_env, false);
            Vector2 c2 = WorldShape.BusStopCenter;
            root.position = new Vector3(c2.x, WorldShape.BusStopHeight, c2.y);

            // The bus (procedural, in bus-local metres: +z forward, +x right/door side).
            var bus = new GameObject("Bus").transform;
            bus.SetParent(root, false);
            Vector2 b2 = WorldShape.BusPosition;
            Vector3 fwd = new Vector3(WorldShape.BusForward.x, 0f, WorldShape.BusForward.y);
            bus.SetPositionAndRotation(new Vector3(b2.x, WorldShape.BusStopHeight, b2.y), Quaternion.LookRotation(fwd));
            var mb = new MeshBuilder();
            Color cream = C("#F2E8CC"), green = C("#3F8F6A"), glass = C("#2E4A5E"), roof = C("#F7F3EA"), grey = C("#5E5A57"), lamp = C("#FFF1BE"), board = C("#E8B53A");
            Quaternion q = Quaternion.identity;
            mb.Box(new Vector3(0f, 1.5f, 0f), new Vector3(2.3f, 2.1f, 7.2f), q, cream);              // body
            mb.Box(new Vector3(0f, 0.72f, 0f), new Vector3(2.34f, 0.56f, 7.24f), q, green);          // skirt
            mb.Box(new Vector3(0f, 1.3f, 0f), new Vector3(2.35f, 0.16f, 7.25f), q, green);           // stripe
            mb.Box(new Vector3(0f, 2.6f, 0f), new Vector3(2.2f, 0.12f, 7.0f), q, roof);              // roof
            foreach (float sx in new[] { -1f, 1f })
            {
                mb.Box(new Vector3(sx * 1.165f, 2.0f, -0.5f), new Vector3(0.04f, 0.72f, 5.4f), q, glass); // side windows
                for (float z = -3.2f; z <= 2.3f; z += 1.1f)
                    mb.Box(new Vector3(sx * 1.18f, 2.0f, z), new Vector3(0.04f, 0.74f, 0.1f), q, cream); // pillars
            }
            mb.Box(new Vector3(1.17f, 1.38f, 2.75f), new Vector3(0.05f, 1.86f, 0.9f), q, glass);     // door (right side, near the front)
            mb.Box(new Vector3(1.19f, 1.38f, 2.75f), new Vector3(0.03f, 1.9f, 0.06f), q, cream);     // door split
            mb.Box(new Vector3(0f, 1.95f, 3.61f), new Vector3(2.0f, 1.0f, 0.04f), q, glass);         // windscreen
            mb.Box(new Vector3(0f, 2.36f, 3.63f), new Vector3(1.5f, 0.26f, 0.04f), q, board);        // destination board
            mb.Box(new Vector3(0f, 2.0f, -3.61f), new Vector3(1.8f, 0.6f, 0.04f), q, glass);         // rear window
            foreach (float bz in new[] { 3.66f, -3.66f })
                mb.Box(new Vector3(0f, 0.55f, bz), new Vector3(2.3f, 0.24f, 0.12f), q, grey);       // bumpers
            foreach (float sx in new[] { -0.8f, 0.8f })
                mb.Box(new Vector3(sx, 0.88f, 3.63f), new Vector3(0.3f, 0.18f, 0.04f), q, lamp);    // headlights
            var mesh = mb.ToMesh("Bus");
            SaveMesh(mesh, "Bus");
            var body = new GameObject("Body");
            body.transform.SetParent(bus, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = ComfyAssets.PlainLitMat;
            GameObjectUtility.SetStaticEditorFlags(body, StaticEditorFlags.BatchingStatic);
            var tyre = SolidMat("Bus_Tyre", "#2B2A2A");
            var hub = SolidMat("Bus_Hub", "#BDB7AE");
            foreach (float sx in new[] { -1.1f, 1.1f })
            foreach (float sz in new[] { -2.4f, 2.4f })
            {
                Vector3 wp = bus.TransformPoint(new Vector3(sx, 0.46f, sz));
                Quaternion wr = bus.rotation * Quaternion.Euler(0f, 0f, 90f);
                Prim(PrimitiveType.Cylinder, bus, wp, new Vector3(0.92f, 0.13f, 0.92f), wr, tyre, "Wheel");
                Prim(PrimitiveType.Cylinder, bus, wp + bus.right * sx * 0.06f, new Vector3(0.45f, 0.08f, 0.45f), wr, hub, "Hub");
            }
            // On Mars, a little rocket on a launch pad behind the stop (that's how the bus gets here).
            var rocketLook = RegionRoot(root, "Rocket", 3);
            Vector2 rp2 = WorldShape.BusStopCenter + new Vector2(7.5f, -5.5f);
            Vector3 rp = Ground(rp2.x, rp2.y, 0f);
            var rWhite = SolidMat("Mars_White", "#E9ECEF");
            var rOrange = SolidMat("Mars_Orange", "#E8743B");
            Prim(PrimitiveType.Cylinder, rocketLook, rp + Vector3.up * 0.05f, new Vector3(4.2f, 0.05f, 4.2f), Quaternion.identity, SolidMat("Mars_Metal", "#9AA3AD"));
            Prim(PrimitiveType.Cylinder, rocketLook, rp + Vector3.up * 2.6f, new Vector3(1.3f, 2.4f, 1.3f), Quaternion.identity, rWhite);
            Prim(PrimitiveType.Cylinder, rocketLook, rp + Vector3.up * 5.25f, new Vector3(1.0f, 0.3f, 1.0f), Quaternion.identity, rOrange);
            Prim(PrimitiveType.Cylinder, rocketLook, rp + Vector3.up * 5.75f, new Vector3(0.62f, 0.25f, 0.62f), Quaternion.identity, rWhite);
            Prim(PrimitiveType.Sphere, rocketLook, rp + Vector3.up * 6.1f, new Vector3(0.45f, 0.7f, 0.45f), Quaternion.identity, rOrange);
            Prim(PrimitiveType.Sphere, rocketLook, rp + new Vector3(0f, 3.6f, -0.62f), new Vector3(0.45f, 0.45f, 0.1f), Quaternion.identity, SolidMat("Mars_Window", "#3E6FB0"));
            for (int k = 0; k < 4; k++)
            {
                var fq = Quaternion.Euler(0f, k * 90f + 45f, 0f);
                Prim(PrimitiveType.Cube, rocketLook, rp + fq * new Vector3(0f, 0.9f, 0.85f), new Vector3(0.1f, 1.4f, 0.8f), fq, rOrange);
            }

            // Snow on the roof in the snowy region.
            var roofSnow = RegionRoot(bus, "RoofSnow", 2);
            Prim(PrimitiveType.Cube, roofSnow, bus.TransformPoint(new Vector3(0f, 2.71f, 0f)), new Vector3(2.12f, 0.1f, 6.8f), bus.rotation, SolidMat("Snow_Cover", "#F1F5F8"));
            var busCol = bus.gameObject.AddComponent<BoxCollider>();
            busCol.center = new Vector3(0f, 1.35f, 0f);
            busCol.size = new Vector3(2.4f, 2.6f, 7.4f);

            // A bench and the sign post (the board itself is made at runtime, with pinyin).
            Vector3 bench = Ground(c2.x + 1.4f, c2.y + 0.3f);
            var benchGo = Place("FantasyTown/stall-bench", root, bench, 0f, 1.6f);
            if (benchGo != null) AddBoxCollider(benchGo, 0.9f);
            var post = SolidMat("Sign_Post", "#6B4A2E");
            Vector3 sign = WorldShape.BusSignSpot3D();
            foreach (float sx in new[] { -0.7f, 0.7f })
            {
                Vector3 pp = Ground(sign.x + sx, sign.z, 0.05f);
                float top = sign.y - 0.2f;
                Prim(PrimitiveType.Cylinder, root, new Vector3(pp.x, (pp.y + top) * 0.5f, pp.z), new Vector3(0.08f, (top - pp.y) * 0.5f, 0.08f), Quaternion.identity, post);
            }
            Place("FantasyTown/cart", root, Ground(c2.x + 6.2f, c2.y - 3.2f), 100f, 1.4f); // for the luggage
            var stopLamp = Place("FantasyTown/lantern", root, Ground(c2.x + 4.2f, c2.y + 0.9f), 0f, 1.25f);
            if (stopLamp != null) AddLight(stopLamp.transform, new Vector3(0, 1.38f, 0), C("#FFC477"), 8f, 2.0f, 0f, false, 0.9f);

            // 张师傅, by the door (hidden until the first HSK test; BusStop switches him on).
            var shop = Catalog.Shops.Find(s => s.busDriver);
            var driverGo = new GameObject(shop.keeperEnglish);
            driverGo.transform.SetParent(root, false);
            Vector2 d2 = WorldShape.BusDriverPosition;
            driverGo.transform.SetPositionAndRotation(Ground(d2.x, d2.y, 0.02f), Quaternion.LookRotation(new Vector3(WorldShape.StallFront(WorldShape.BusStall).x, 0f, WorldShape.StallFront(WorldShape.BusStall).y)));
            var anim = AttachModel(driverGo, "MiniCharacters/character-male-d", CharacterScale);
            OutfitBuilder.Dress(driverGo, anim, "MiniCharacters/character-male-d", 10, OutfitBuilder.Kind.Driver);
            // A driver's cap, so he doesn't look like 老王 (sized in world metres from the model, then pinned to the head bone).
            var head = anim.FindBone("head");
            if (head != null)
            {
                Bounds mbnd = ModelBounds(anim.gameObject);
                Vector3 df = driverGo.transform.forward;
                Vector3 top = new Vector3(driverGo.transform.position.x, mbnd.max.y, driverGo.transform.position.z);
                void Piece(PrimitiveType type, string name, Vector3 world, Vector3 size, string hex)
                {
                    var go = Prim(type, head, world, Vector3.one, driverGo.transform.rotation, SolidMat(name, hex), name);
                    go.transform.position = world;
                    go.transform.rotation = driverGo.transform.rotation;
                    Vector3 ls = head.lossyScale;
                    go.transform.localScale = new Vector3(size.x / ls.x, size.y / ls.y, size.z / ls.z);
                }
                float w = Mathf.Min(mbnd.size.x, mbnd.size.z) * 0.62f;
                Piece(PrimitiveType.Cylinder, "Driver_Cap", top + Vector3.up * 0.02f, new Vector3(w, 0.05f, w), "#2F4D7A");
                Piece(PrimitiveType.Cube, "Driver_Brim", top - Vector3.up * 0.01f + df * w * 0.5f, new Vector3(w * 0.8f, 0.025f, w * 0.4f), "#22385A");
            }
            driverGo.AddComponent<AudioSource>();
            var voice = driverGo.AddComponent<CharacterVoice>();
            voice.Configure(shop.voice, "", shop.pitch);
            var brain = driverGo.AddComponent<ShopkeeperBrain>();
            brain.Configure(shop.id, voice, player.transform);
            driverGo.AddComponent<TalkingHead>().Configure(anim, voice);
            driverGo.SetActive(false);

            root.gameObject.AddComponent<BusStop>().Configure(driverGo);
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
                GameObject model = null;
                for (int region = 0; region < Regions.All.Length; region++)
                {
                    var m = Place(g.model, RegionRoot(built, "Style", region), pos, yaw + StallYawOffset, StallScale, style: RegionStyles[region]);
                    if (region == 0) model = m;
                }
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
            if (shop.region > 0 && (shop.role == "furniture" || shop.role == "books"))
            {
                // The other stops' furniture makers show their own pieces; their booksellers, their own books.
                var goods = shop.items.Select(Catalog.Get).Where(d => d != null && d.model != null && d.category != ItemCategory.Bed).ToList();
                if (shop.role == "books")
                    for (int k = 0; k < goods.Count && k < 3; k++) PlaceItem(goods[k].id, parent, At(-0.55f + k * 0.5f), yaw + 8f - k * 7f);
                else
                {
                    var spots = new[] { g(-1.7f, 1.9f), g(1.8f, 1.7f), g(-1.6f, 0.2f) };
                    for (int k = 0; k < goods.Count && k < spots.Length; k++) PlaceItem(goods[k].id, parent, spots[k], yaw + 200f - k * 40f);
                }
                return;
            }
            switch (shop.role)
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
                        // (shared by every region's display: load it if it's there, never delete it)
                        var mat = SolidMat("DisplayRod_" + ids[k], "#" + ColorUtility.ToHtmlStringRGB(colors[k]));
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
                    Material Mat(string name, string hex) => SolidMat(name, hex);
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
                    PlaceItem("book_shore", parent, At(-0.05f), yaw - 6f);
                    PlaceItem("book_bay", parent, At(0.45f), yaw + 14f);
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
                    var dark = SolidMat("Blackboard", "#2F4A3A");
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
                        pot.GetComponent<Renderer>().sharedMaterial = SolidMat("PaintPot" + k, pots[k]);
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
            if (WorldShape.InBusStop(x, z, campPad + 1f) || DistanceToBusRoad(x, z) < pathPad) return true;
            if (WorldShape.DistanceToPath(x, z) < pathPad) return true;
            if (WorldShape.IsOnDock(x, z, 3f)) return true;
            if (WorldShape.InMarket(x, z, campPad + 1f)) return true;
            if (WorldShape.InSeafront(x, z, campPad + 1f)) return true;
            if (Vector2.Distance(new Vector2(x, z), new Vector2(fire.x, fire.z)) < 3f) return true;
            return false;
        }

        private static void ScatterNature(Vector3 fire)
        {
            var nature = RegionRoot(_env, "Nature", 0);
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

        private static readonly string[] SnowTrees = { "HolidayKit/tree-snow-a", "HolidayKit/tree-snow-b", "HolidayKit/tree-snow-c" };

        /// <summary>
        /// The snowy coast (region 2): snow-laden pines (thick inland), frosted bushes and stumps, drifts and snow piles,
        /// cold grey rocks, snowmen and a sled by the camp, and ice floes drifting along the shore away from the dock.
        /// </summary>
        private static void ScatterSnow(Vector3 fire)
        {
            var nature = RegionRoot(_env, "Nature", 2);
            var trees = new GameObject("Trees").transform;
            trees.SetParent(nature, false);
            var small = new GameObject("Plants").transform;
            small.SetParent(nature, false);
            const string snow = "snow";

            void Collide(GameObject t)
            {
                if (t == null) return;
                var cap = t.AddComponent<CapsuleCollider>();
                cap.radius = 0.06f;
                cap.height = 0.5f;
                cap.center = new Vector3(0, 0.25f, 0);
            }

            var placed = new List<Vector2>();
            int count = 0;
            for (float gx = -118f; gx <= 118f; gx += 3.6f)
            for (float gz = -118f; gz <= 118f; gz += 3.6f)
            {
                float x = gx + R(-1.5f, 1.5f), z = gz + R(-1.5f, 1.5f);
                float d = WorldShape.ShoreDistance(x, z);
                float r = new Vector2(x, z).magnitude;
                if (d < 5f || r > 122f) continue;
                if (Blocked(x, z, 2f, 3.2f, fire)) continue;
                float cluster = Mathf.PerlinNoise(x * 0.045f + 61f, z * 0.045f + 17f);
                float p = Mathf.SmoothStep(0f, 0.3f, Mathf.InverseLerp(5f, 18f, d)) + Mathf.SmoothStep(0f, 0.35f, Mathf.InverseLerp(20f, 45f, d)) + Mathf.InverseLerp(60f, 90f, r) * 0.3f;
                p *= cluster > 0.45f ? 1.5f : 0.4f;
                if (_rng.NextDouble() > p) continue;
                bool crowded = false;
                foreach (var q in placed) if ((q - new Vector2(x, z)).sqrMagnitude < 6f) { crowded = true; break; }
                if (crowded) continue;
                placed.Add(new Vector2(x, z));
                var t = _rng.NextDouble() < 0.75
                    ? Place(Pick(SnowTrees), trees, Ground(x, z, 0.08f), R(0, 360), R(3.0f, 4.6f))
                    : Place("NatureKit/" + Pick(Pines), trees, Ground(x, z, 0.08f), R(0, 360), R(3.6f, 5.4f), style: snow);
                if (t == null) continue;
                count++;
                if (r < WorldShape.PlayableRadius + 6f) Collide(t);
            }

            // Ground cover: drifts, frosted bushes, stumps and rocks.
            for (int k = 0; k < 800; k++)
            {
                float x = R(-78f, 78f), z = R(-78f, 78f);
                float d = WorldShape.ShoreDistance(x, z);
                if (d < 1.5f || Blocked(x, z, -3f, 1.2f, fire)) continue;
                double roll = _rng.NextDouble();
                if (roll < 0.3)
                    Place("HolidayKit/" + (_rng.NextDouble() < 0.5 ? "snow-flat" : "snow-flat-large"), small, Ground(x, z, 0.03f), R(0, 360), R(1.4f, 2.4f), shadows: false);
                else if (roll < 0.5)
                    Place("HolidayKit/snow-pile", small, Ground(x, z, 0.05f), R(0, 360), R(1.0f, 1.8f));
                else if (roll < 0.65 && d > 5f)
                    Place("NatureKit/" + Pick(Bushes), small, Ground(x, z, 0.05f), R(0, 360), R(2.4f, 3.4f), style: snow);
                else if (roll < 0.8)
                    Place("NatureKit/" + Pick(SmallRocks), small, Ground(x, z, 0.05f), R(0, 360), R(2f, 3.2f), style: snow);
                else if (roll < 0.86 && d > 6f)
                {
                    var dw = Place("NatureKit/" + Pick(Deadwood), small, Ground(x, z, 0.03f), R(0, 360), R(2.2f, 3f), style: snow);
                    if (dw != null) AddBoxCollider(dw, 0.8f);
                }
            }

            // Rocks along the waterline.
            for (int k = 0; k < 140; k++)
            {
                float x = R(-WorldShape.PlayableRadius, WorldShape.PlayableRadius);
                float z = WorldShape.ShoreZ(x) + R(-1.5f, 3f);
                if (Blocked(x, z, 0f, 2.5f, fire) || WorldShape.IsOnDock(x, z, 3f)) continue;
                var rock = Place("NatureKit/" + Pick(BigRocks), small, Ground(x, z, 0.15f), R(0, 360), R(1.4f, 2.8f), style: snow);
                if (rock != null && WorldShape.TerrainHeight(x, z) > -0.3f) AddBoxCollider(rock, 0.75f);
            }

            // Snowmen and a sled by the camp.
            Vector2 cc = WorldShape.CampCenter;
            foreach (var (dx, dz, model, scale) in new[] { (7.5f, 2.5f, "snowman", 2.2f), (-6.5f, 4.5f, "snowman-hat", 2.0f), (5.5f, 5.5f, "sled", 2.0f) })
            {
                float x = cc.x + dx, z = cc.y + dz;
                if (InCabin(x, z) || WorldShape.DistanceToPath(x, z) < 1.4f) continue;
                var go = Place("HolidayKit/" + model, small, Ground(x, z, 0.02f), R(150f, 210f), scale);
                if (go != null) AddBoxCollider(go, 0.8f);
            }

            // Ice floes along the shore, kept clear of the dock and the casting water in front of it.
            var mb = new MeshBuilder();
            Color top = C("#F2F7FA"), side = C("#B9D2E0");
            int floes = 0;
            for (int k = 0; k < 120 && floes < 46; k++)
            {
                float x = R(-90f, 90f);
                if (Mathf.Abs(x) < 20f) continue;
                float z = WorldShape.ShoreZ(x) + R(3f, 26f);
                if (WorldShape.TerrainHeight(x, z) > -0.4f) continue;
                float size = R(0.8f, 2.8f);
                int sides = _rng.Next(5, 8);
                float yaw = R(0f, 360f), y = WorldShape.WaterLevel + 0.06f, depth = 0.22f;
                var ring = new Vector3[sides];
                for (int v = 0; v < sides; v++)
                {
                    float a = (yaw + v * 360f / sides + R(-12f, 12f)) * Mathf.Deg2Rad;
                    float rr = size * R(0.7f, 1.1f);
                    ring[v] = new Vector3(x + Mathf.Cos(a) * rr, y, z + Mathf.Sin(a) * rr);
                }
                Vector3 centre = new Vector3(x, y, z);
                for (int v = 0; v < sides; v++)
                {
                    Vector3 a = ring[v], b = ring[(v + 1) % sides];
                    mb.Triangle(centre, b, a, top);
                    Vector3 a2 = a - Vector3.up * depth, b2 = b - Vector3.up * depth;
                    mb.Triangle(a, b, b2, side);
                    mb.Triangle(a, b2, a2, side);
                }
                floes++;
            }
            var floeMesh = mb.ToMesh("IceFloes");
            SaveMesh(floeMesh, "IceFloes");
            var floeGo = new GameObject("IceFloes");
            floeGo.transform.SetParent(nature, false);
            floeGo.AddComponent<MeshFilter>().sharedMesh = floeMesh;
            var fr = floeGo.AddComponent<MeshRenderer>();
            fr.sharedMaterial = ComfyAssets.PlainLitMat;
            fr.shadowCastingMode = ShadowCastingMode.Off;
            Debug.Log($"[SceneBuilder] Snow: placed {count} trees and {floes} ice floes.");
        }

        private static readonly string[] Palms = { "tree_palm", "tree_palmBend", "tree_palmDetailedShort", "tree_palmDetailedTall", "tree_palmShort", "tree_palmTall" };
        private static readonly string[] Cacti = { "cactus_short", "cactus_tall" };
        private static readonly string[] SandRocks = { "PirateKit/rocks-sand-a", "PirateKit/rocks-sand-b", "PirateKit/rocks-sand-c", "SurvivalKit/rock-sand-a", "SurvivalKit/rock-sand-b", "SurvivalKit/rock-sand-c" };
        private static readonly string[] DryPlants = { "plant_bushSmall", "plant_flatShort", "grass", "plant_bushDetailed" };

        /// <summary>
        /// The desert's plants and rocks (region 1): palm groves along the beach and round the camp, cacti and sandstone
        /// further inland, tall red rocks on the hills, a few ruined columns, and dry tufts here and there.
        /// </summary>
        private static void ScatterDesert(Vector3 fire)
        {
            var nature = RegionRoot(_env, "Nature", 1);
            var trees = new GameObject("Trees").transform;
            trees.SetParent(nature, false);
            var small = new GameObject("Plants").transform;
            small.SetParent(nature, false);
            const string desert = "desert";

            void Collide(GameObject t)
            {
                if (t == null) return;
                var cap = t.AddComponent<CapsuleCollider>();
                cap.radius = 0.06f;
                cap.height = 0.5f;
                cap.center = new Vector3(0, 0.25f, 0);
            }

            var placed = new List<Vector2>();
            bool Crowded(float x, float z, float minSq)
            {
                foreach (var q in placed) if ((q - new Vector2(x, z)).sqrMagnitude < minSq) return true;
                return false;
            }

            // Palms along the beach and in groves, cacti further inland, sparser than the forest.
            int count = 0;
            for (float gx = -118f; gx <= 118f; gx += 4.2f)
            for (float gz = -118f; gz <= 118f; gz += 4.2f)
            {
                float x = gx + R(-1.8f, 1.8f), z = gz + R(-1.8f, 1.8f);
                float d = WorldShape.ShoreDistance(x, z);
                float r = new Vector2(x, z).magnitude;
                if (d < 2.5f || r > 122f) continue;
                if (Blocked(x, z, 2f, 3.2f, fire)) continue;
                float grove = Mathf.PerlinNoise(x * 0.05f + 21f, z * 0.05f + 3f);
                bool beach = d < 16f;
                float p = beach ? (grove > 0.5f ? 0.55f : 0.12f) : (grove > 0.6f ? 0.3f : 0.07f);
                if (r > 60f) p *= 0.6f;
                if (_rng.NextDouble() > p) continue;
                if (Crowded(x, z, 9f)) continue;
                placed.Add(new Vector2(x, z));
                bool palm = beach || grove > 0.6f;
                var t = palm
                    ? Place("NatureKit/" + Pick(Palms), trees, Ground(x, z, 0.08f), R(0, 360), R(3.4f, 4.8f), style: desert)
                    : Place("NatureKit/" + Pick(Cacti), trees, Ground(x, z, 0.05f), R(0, 360), R(2.6f, 4.0f), style: desert);
                if (t == null) continue;
                count++;
                if (r < WorldShape.PlayableRadius + 6f) Collide(t);
            }

            // Big red rocks and mesas on the hills behind.
            for (int k = 0; k < 90; k++)
            {
                float a = R(Mathf.PI * 1.05f, Mathf.PI * 1.95f), rr = R(50f, 115f);
                float x = Mathf.Cos(a) * rr, z = Mathf.Sin(a) * rr;
                if (WorldShape.ShoreDistance(x, z) < 12f || Blocked(x, z, 3f, 3f, fire)) continue;
                var rock = Place("NatureKit/" + Pick(new[] { "rock_tallA", "rock_tallB", "rock_tallC", "rock_tallD", "rock_tallE", "rock_tallF", "rock_tallG", "rock_largeA", "rock_largeC", "rock_largeE" }),
                    trees, Ground(x, z, 0.3f), R(0, 360), R(4f, 9f), style: desert);
                if (rock != null && rr < WorldShape.PlayableRadius + 6f) AddBoxCollider(rock, 0.7f);
            }

            // Ground cover: sandstone, dry tufts, the odd cactus.
            for (int k = 0; k < 900; k++)
            {
                float x = R(-78f, 78f), z = R(-78f, 78f);
                float d = WorldShape.ShoreDistance(x, z);
                if (d < 1.5f || Blocked(x, z, -3f, 1.2f, fire)) continue;
                double roll = _rng.NextDouble();
                if (roll < 0.35)
                    Place("NatureKit/" + Pick(DryPlants), small, Ground(x, z, 0.02f), R(0, 360), R(2.0f, 3.0f), shadows: false, style: desert);
                else if (roll < 0.6)
                {
                    var sr = Place(Pick(SandRocks), small, Ground(x, z, 0.05f), R(0, 360), R(0.8f, 1.6f), style: desert);
                    if (sr != null && _rng.NextDouble() < 0.3) AddBoxCollider(sr, 0.7f);
                }
                else if (roll < 0.72 && d > 8f)
                    Place("NatureKit/cactus_short", small, Ground(x, z, 0.03f), R(0, 360), R(1.6f, 2.4f), style: desert);
                else if (roll < 0.8)
                    Place("NatureKit/" + Pick(SmallRocks), small, Ground(x, z, 0.05f), R(0, 360), R(2f, 3.2f), style: desert);
                else if (roll < 0.86)
                    Place("PirateKit/patch-sand-foliage", small, Ground(x, z, 0.02f), R(0, 360), R(0.8f, 1.2f), shadows: false, style: desert);
            }

            // Palms round the camp and either side of the market entrance (the oasis feel).
            for (int k = 0; k < 10; k++)
            {
                Vector2 pc = WorldShape.CampCenter + Random2(R(10.5f, 14f));
                if (Blocked(pc.x, pc.y, 0f, 2f, fire) || WorldShape.ShoreDistance(pc.x, pc.y) < 3f) continue;
                Collide(Place("NatureKit/" + Pick(Palms), trees, Ground(pc.x, pc.y, 0.08f), R(0, 360), R(3.6f, 4.6f), style: desert));
            }
            Vector2 entrance = WorldShape.MarketCenter - new Vector2(WorldShape.MarketRadius + 1.5f, 0f);
            foreach (float side in new[] { -4.5f, 4.5f })
                Collide(Place("NatureKit/tree_palmTall", trees, Ground(entrance.x, entrance.y + side, 0.08f), R(0, 360), 4.4f, style: desert));

            // Ruins: a few weathered columns and an obelisk on the rise behind the camp.
            Vector2 ruin = WorldShape.CampCenter + new Vector2(-24f, -14f);
            var ruins = new[] { ("statue_column", 0f, 0f), ("statue_columnDamaged", 3.2f, 0.5f), ("statue_column", 6.4f, 0f), ("statue_block", 2f, -3f), ("statue_obelisk", -5f, -4f) };
            foreach (var (model, dx, dz) in ruins)
            {
                float x = ruin.x + dx, z = ruin.y + dz;
                if (Blocked(x, z, 0f, 2f, fire)) continue;
                var col = Place("NatureKit/" + model, small, Ground(x, z, 0.1f), R(-8f, 8f), 3.2f, style: desert);
                if (col != null) AddBoxCollider(col, 0.8f);
            }

            // Rocks along the waterline (sandstone).
            for (int k = 0; k < 120; k++)
            {
                float x = R(-WorldShape.PlayableRadius, WorldShape.PlayableRadius);
                float z = WorldShape.ShoreZ(x) + R(-1.5f, 3f);
                if (Blocked(x, z, 0f, 2.5f, fire) || WorldShape.IsOnDock(x, z, 3f)) continue;
                var rock = Place("NatureKit/" + Pick(BigRocks), small, Ground(x, z, 0.15f), R(0, 360), R(1.4f, 2.8f), style: desert);
                if (rock != null && WorldShape.TerrainHeight(x, z) > -0.3f) AddBoxCollider(rock, 0.75f);
            }
            Debug.Log($"[SceneBuilder] Desert: placed {count} palms and cacti.");
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
            OutfitBuilder.Dress(go, anim, "MiniCharacters/character-male-a", 3, OutfitBuilder.Kind.Player);
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
            OutfitBuilder.Dress(go, anim, "MiniCharacters/character-female-f", 5, OutfitBuilder.Kind.Mei);
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

            // A couple of shy deer and bunnies around the woods; foxes and crabs in the desert.
            var forest = RegionRoot(critters, "Wildlife", 0);
            var dunes = RegionRoot(critters, "Wildlife", 1);
            var ice = RegionRoot(critters, "Wildlife", 2);
            var spots = new[]
            {
                (a: 40f, r: 18f, m: "CubePets/animal-deer", s: 0.55f, home: forest), (a: 150f, r: 22f, m: "CubePets/animal-deer", s: 0.5f, home: forest),
                (a: 250f, r: 9f, m: "CubePets/animal-bunny", s: 0.2f, home: forest), (a: 300f, r: 12f, m: "CubePets/animal-fox", s: 0.3f, home: forest),
                (a: 60f, r: 14f, m: "CubePets/animal-fox", s: 0.28f, home: dunes), (a: 280f, r: 16f, m: "CubePets/animal-fox", s: 0.3f, home: dunes),
                (a: 120f, r: -4.5f, m: "CubePets/animal-crab", s: 0.22f, home: dunes), (a: 230f, r: -4.5f, m: "CubePets/animal-crab", s: 0.2f, home: dunes),
                (a: 100f, r: -4.8f, m: "CubePets/animal-penguin", s: 0.3f, home: ice), (a: 108f, r: -4.2f, m: "CubePets/animal-penguin", s: 0.26f, home: ice),
                (a: 250f, r: -4.6f, m: "CubePets/animal-penguin", s: 0.3f, home: ice), (a: 320f, r: 22f, m: "CubePets/animal-polar", s: 0.6f, home: ice),
                (a: 40f, r: 16f, m: "CubePets/animal-fox", s: 0.3f, home: ice),
            };
            foreach (var s in spots)
            {
                // (a = x position along the beach, r = how far inland)
                float cx = Mathf.Lerp(-60f, 60f, s.a / 360f);
                Vector2 spot = WorldShape.ShorePoint(cx) + new Vector2(0f, -s.r - 6f);
                var go = new GameObject(Path.GetFileName(s.m));
                go.transform.SetParent(s.home, false);
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

            // Snowflakes for the snowy region (Weather follows the camera with them and sets how many fall).
            var snowGo = new GameObject("Snow");
            snowGo.transform.SetParent(weatherGo.transform, false);
            var snow = snowGo.AddComponent<ParticleSystem>();
            var sm = snow.main;
            sm.startLifetime = 7f;
            sm.startSpeed = 0f;
            sm.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.13f);
            sm.maxParticles = 3000;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            var sem = snow.emission;
            sem.rateOverTime = 0f;
            var ssh = snow.shape;
            ssh.shapeType = ParticleSystemShapeType.Box;
            ssh.scale = new Vector3(44f, 1f, 44f);
            var svel = snow.velocityOverLifetime;
            svel.enabled = true;
            svel.space = ParticleSystemSimulationSpace.World;
            svel.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.2f);
            svel.y = new ParticleSystem.MinMaxCurve(-1.4f, -1.0f);
            svel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            var snoise = snow.noise;
            snoise.enabled = true;
            snoise.strength = 0.35f;
            snoise.frequency = 0.3f;
            var sr = snowGo.GetComponent<ParticleSystemRenderer>();
            sr.sharedMaterial = GameAssets.Instance.splashMaterial;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            weather.SetSnow(snow);

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
