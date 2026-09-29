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
    }
}
