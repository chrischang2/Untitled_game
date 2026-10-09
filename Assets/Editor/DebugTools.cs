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
        /// <summary>Bones, and per bone the atlas colours its vertices use, for every mini character (Captures/characters.txt).</summary>
        public static void DumpCharacters()
        {
            var sb = new StringBuilder();
            var atlas = new Texture2D(2, 2);
            atlas.LoadImage(File.ReadAllBytes("Assets/ThirdParty/Kenney/MiniCharacters/Textures/colormap.png"));
            foreach (var name in new[] { "male-a", "male-b", "male-c", "male-d", "male-e", "male-f", "female-a", "female-b", "female-c", "female-d", "female-e", "female-f" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ThirdParty/Kenney/MiniCharacters/character-{name}.fbx");
                var smr = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
                var mf = prefab.GetComponentInChildren<MeshFilter>();
                var mesh = smr != null ? smr.sharedMesh : mf?.sharedMesh;
                sb.AppendLine($"== {name}: smr={smr != null} verts={mesh?.vertexCount} bones=[{(smr != null ? string.Join(",", smr.bones.Select(b => b.name)) : "")}]");
                if (mesh == null || smr == null) continue;
                var uv = mesh.uv;
                var bw = mesh.boneWeights;
                foreach (var g in Enumerable.Range(0, mesh.vertexCount).GroupBy(i => smr.bones[bw[i].boneIndex0].name))
                {
                    var cols = g.GroupBy(i => ColorUtility.ToHtmlStringRGB(atlas.GetPixelBilinear(uv[i].x, uv[i].y)))
                        .OrderByDescending(c => c.Count()).Select(c => $"#{c.Key}x{c.Count()}");
                    sb.AppendLine($"   {g.Key}: {g.Count()} verts  {string.Join(" ", cols)}");
                }
            }
            File.WriteAllText("Captures/characters.txt", sb.ToString());
            Debug.Log("[Automation] wrote Captures/characters.txt");
        }

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
