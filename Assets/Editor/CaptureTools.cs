using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UntitledGame.Environment;

namespace UntitledGame.EditorTools
{
    /// <summary>
    /// Renders preview screenshots of the scene from a few viewpoints / times of day into Captures/.
    /// Works in batch mode (with graphics) so the world can be checked without opening the editor.
    /// </summary>
    public static class CaptureTools
    {
        private struct View
        {
            public string name;
            public Vector3 pos;
            public Vector3 lookAt;
            public float fov;
        }

        [MenuItem("Untitled Game/Capture Preview Screenshots")]
        public static void CaptureAll()
        {
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath);
            ShaderUtil.allowAsyncCompilation = false;
            var cam = Camera.main;
            var dn = Object.FindFirstObjectByType<DayNightCycle>();
            var player = GameObject.Find("Player");
            var mei = GameObject.Find("Mei");
            PoseCharacters();

            Vector3 pp = player.transform.position;
            Vector3 fwd = player.transform.forward;
            var views = new[]
            {
                new View { name = "gameplay", pos = pp - fwd * 7.8f + Vector3.up * 4.2f, lookAt = pp + Vector3.up * 1.1f + fwd * 2f, fov = 48 },
                new View { name = "overview", pos = pp - fwd * 30f + Vector3.up * 26f + Vector3.right * 8f, lookAt = pp + fwd * 14f, fov = 50 },
                new View { name = "lake", pos = new Vector3(2f, 3.2f, -8f), lookAt = pp + fwd * 2f + Vector3.up * 1f, fov = 50 },
                new View { name = "closeup", pos = pp + fwd * 3.2f + Vector3.up * 1.6f + Vector3.right * 0.6f, lookAt = (pp + mei.transform.position) * 0.5f + Vector3.up * 0.8f, fov = 45 },
                new View { name = "market", pos = MarketPoint(-11f, 6f, -3f), lookAt = MarketPoint(2f, 0.5f, 0f), fov = 55 },
                new View { name = "stall", pos = MarketPoint(0.5f, 2.2f, -5.2f), lookAt = MarketPoint(5.5f, 1f, -5f), fov = 55 },
                KeeperView(1),
                new View { name = "plaza", pos = MarketPoint(13f, 11f, 0f), lookAt = MarketPoint(-3f, 0f, 0f), fov = 60 },
                KeeperView(4, "bookshop"),
                new View { name = "island", pos = new Vector3(WorldShape.IslandCenter.x - 22f, 9f, WorldShape.IslandCenter.y - 26f), lookAt = new Vector3(WorldShape.IslandCenter.x, 1f, WorldShape.IslandCenter.y), fov = 55 },
                new View { name = "camp", pos = pp - fwd * 4f + Vector3.up * 3f + Vector3.right * 6f, lookAt = pp - fwd * 12f, fov = 55 },
            };

            string[] args = System.Environment.GetCommandLineArgs();
            float[] times = { 7.5f, 13f, 19.2f, 23f };
            string only = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-captureView") only = args[i + 1];

            Directory.CreateDirectory("Captures");
            // The very first render after loading comes out with stale material data; render once and discard.
            Render(cam, views[0], "Captures/_warmup.png");
            foreach (var v in views)
            {
                if (only != null && only != v.name) continue;
                foreach (float t in times)
                {
                    if (v.name != "gameplay" && v.name != "overview" && t != 13f && t != 19.2f) continue;
                    dn.TimeOfDay = t;
                    dn.Apply();
                    Render(cam, v, $"Captures/{v.name}_{t:00.0}.png");
                }
            }
            Debug.Log("[Capture] Done.");
        }

        /// <summary>What a customer standing at stall i sees.</summary>
        private static View KeeperView(int i, string name = "keeper")
        {
            Vector2 c = WorldShape.CustomerSpot(i), st = WorldShape.StallPosition(i);
            Vector2 back = (c - st).normalized;
            Vector2 eye = c + back * 1.2f;
            float y = WorldShape.TerrainHeight(eye.x, eye.y);
            return new View { name = name, pos = new Vector3(eye.x, y + 1.7f, eye.y), lookAt = new Vector3(st.x, y + 0.9f, st.y), fov = 50 };
        }

        private static Vector3 MarketPoint(float x, float y, float z)
        {
            var c = WorldShape.MarketCenter;
            return new Vector3(c.x + x, WorldShape.MarketHeight + y, c.y + z);
        }

        private static void PoseCharacters()
        {
            foreach (var anim in Object.FindObjectsByType<Animation>(FindObjectsSortMode.None))
            {
                string pose = anim.transform.parent != null && anim.transform.parent.name == "Mei" ? "sit"
                    : anim.transform.parent != null && anim.transform.parent.name == "Player" ? "holding-right" : "idle";
                var clip = anim.GetClip(pose) ?? anim.GetClip("idle");
                if (clip != null) clip.SampleAnimation(anim.gameObject, 0.3f);
            }
        }

        private static void Render(Camera cam, View v, string path)
        {
            const int w = 1600, h = 900;
            cam.transform.position = v.pos;
            cam.transform.rotation = Quaternion.LookRotation(v.lookAt - v.pos, Vector3.up);
            cam.fieldOfView = v.fov;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Debug.Log($"[Capture] {path}");
        }

        /// <summary>Batch helper: rebuild the scene then capture.</summary>
        public static void BuildAndCapture()
        {
            SceneBuilder.Build();
            CaptureAll();
        }
    }
}
