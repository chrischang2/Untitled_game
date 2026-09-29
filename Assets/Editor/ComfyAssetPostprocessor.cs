using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UntitledGame.EditorTools
{
    /// <summary>Import rules for the free third-party assets (Kenney models, OpenGameArt/Kenney audio).</summary>
    public class ComfyAssetPostprocessor : AssetPostprocessor
    {
        private static readonly string[] LoopingClips =
        {
            "static", "idle", "walk", "sprint", "run", "sit", "crouch", "drive", "fall",
            "holding-right", "holding-left", "holding-both", "dance", "eat",
            "wheelchair-sit",
        };

        private bool IsKenney => assetPath.Contains("/ThirdParty/Kenney/");
        private bool IsAnimatedKenney =>
            assetPath.Contains("/MiniCharacters/character-") || assetPath.Contains("/CubePets/");

        private void OnPreprocessModel()
        {
            if (!IsKenney) return;
            var mi = (ModelImporter)assetImporter;
            mi.importCameras = false;
            mi.importLights = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.addCollider = false;
            mi.isReadable = false;
            mi.importBlendShapes = false;

            if (IsAnimatedKenney)
            {
                mi.animationType = ModelImporterAnimationType.Legacy;
                mi.importAnimation = true;
            }
            else
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
            }
        }

        private void OnPreprocessAnimation()
        {
            if (!IsAnimatedKenney) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                string shortName = c.name.Contains("|") ? c.name.Substring(c.name.LastIndexOf('|') + 1) : c.name;
                c.name = shortName;
                bool loop = LoopingClips.Contains(shortName);
                c.loopTime = loop;
                c.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
            }
            mi.clipAnimations = clips;
        }

        private void OnPostprocessMaterial(Material material)
        {
            if (!IsKenney) return;
            // Kenney kits are flat-coloured low poly; keep them matte and instanced.
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.12f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 0f);
            if (material.HasProperty("_EnvironmentReflections")) material.SetFloat("_EnvironmentReflections", 0f);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            material.enableInstancing = true;
        }

        private void OnPreprocessTexture()
        {
            if (!IsKenney) return;
            var ti = (TextureImporter)assetImporter;
            // Colour-map palettes: avoid compression smearing neighbouring palette cells.
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.filterMode = FilterMode.Bilinear;
            ti.mipmapEnabled = true;
        }

        private void OnPreprocessAudio()
        {
            if (!assetPath.Contains("/ThirdParty/Audio/")) return;
            var ai = (AudioImporter)assetImporter;
            var s = ai.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            if (assetPath.Contains("/Music/"))
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.quality = 0.6f;
            }
            else if (assetPath.Contains("/Ambience/"))
            {
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.quality = 0.5f;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.quality = 0.7f;
            }
            ai.defaultSampleSettings = s;
            ai.loadInBackground = assetPath.Contains("/Music/");
        }
    }
}
