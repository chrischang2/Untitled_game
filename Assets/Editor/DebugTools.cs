using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UntitledGame.EditorTools
{
    /// <summary>Diagnostics used while building the project (material dumps etc.).</summary>
    public static class DebugTools
    {
        [MenuItem("Untitled Game/Debug/Dump Kenney Materials")]
        public static void DumpMaterials()
        {
            var sb = new StringBuilder();
            string[] models =
            {
                "NatureKit/tree_default", "NatureKit/rock_largeA", "MiniCharacters/character-male-a", "SurvivalKit/fish", "HolidayKit/cabin-wall",
            };
            foreach (var m in models)
            {
                string path = $"Assets/ThirdParty/Kenney/{m}.fbx";
                foreach (var mat in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                {
                    sb.AppendLine($"== {m} :: {mat.name} shader={mat.shader.name} keywords=[{string.Join(",", mat.shaderKeywords)}]");
                    var s = mat.shader;
                    for (int i = 0; i < s.GetPropertyCount(); i++)
                    {
                        string n = s.GetPropertyName(i);
                        switch (s.GetPropertyType(i))
                        {
                            case UnityEngine.Rendering.ShaderPropertyType.Color: sb.AppendLine($"   {n} = {mat.GetColor(n)}"); break;
                            case UnityEngine.Rendering.ShaderPropertyType.Float:
                            case UnityEngine.Rendering.ShaderPropertyType.Range: sb.AppendLine($"   {n} = {mat.GetFloat(n)}"); break;
                            case UnityEngine.Rendering.ShaderPropertyType.Texture: sb.AppendLine($"   {n} = {(mat.GetTexture(n) ? mat.GetTexture(n).name : "null")}"); break;
                        }
                    }
                }
            }
            Directory.CreateDirectory("Captures");
            File.WriteAllText("Captures/materials.txt", sb.ToString());
            Debug.Log("[Automation] Wrote Captures/materials.txt");
        }

        /// <summary>Bounds of the cabin pieces plus a ray scan of where each wall is solid (to size colliders / doorways).</summary>
        public static void DumpCabinPieces()
        {
            var sb = new StringBuilder();
            foreach (var name in new[] { "cabin-wall", "cabin-doorway", "cabin-door-rotate", "cabin-window-a", "cabin-window-c", "cabin-corner-logs", "floor-wood" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ThirdParty/Kenney/HolidayKit/{name}.fbx");
                if (prefab == null) { sb.AppendLine($"== {name}: missing"); continue; }
                var go = Object.Instantiate(prefab);
                var rs = go.GetComponentsInChildren<Renderer>();
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.AppendLine($"== {name}: bounds min {b.min:F3} max {b.max:F3}");
                foreach (var t in go.GetComponentsInChildren<Transform>())
                {
                    var r = t.GetComponent<Renderer>();
                    sb.AppendLine($"   node {t.name} localPos {t.localPosition:F3} rot {t.localEulerAngles:F0} scale {t.localScale:F2}" + (r != null ? $" bounds {r.bounds.min:F3}..{r.bounds.max:F3}" : ""));
                }
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                Physics.SyncTransforms();
                foreach (float y in new[] { 0.05f, 0.3f, 0.6f, 0.8f, 0.95f })
                {
                    var line = new StringBuilder();
                    for (float x = -0.6f; x <= 0.601f; x += 0.025f)
                    {
                        bool hitZ = Physics.Raycast(new Vector3(x, y, -2f), Vector3.forward, out var h1, 4f);
                        line.Append(hitZ ? "#" : ".");
                    }
                    sb.AppendLine($"   y={y:0.00} x[-0.6..0.6] {line}");
                }
                if (Physics.Raycast(new Vector3(0, 0.5f, -2f), Vector3.forward, out var hz, 4f)) sb.AppendLine($"   wall face z={hz.point.z:F3}");
                if (Physics.Raycast(new Vector3(0, 0.5f, 2f), Vector3.back, out var hz2, 4f)) sb.AppendLine($"   wall back z={hz2.point.z:F3}");
                Object.DestroyImmediate(go);
            }
            Directory.CreateDirectory("Captures");
            File.WriteAllText("Captures/cabin-pieces.txt", sb.ToString());
            Debug.Log("[Automation] Wrote Captures/cabin-pieces.txt");
        }
    }
}
