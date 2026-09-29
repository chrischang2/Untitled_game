using UnityEngine;
using UntitledGame.Core;

namespace UntitledGame.Fishing
{
    /// <summary>Builds a display model for a catch: Kenney fish recoloured per species, or a junk prop.</summary>
    public static class CatchVisuals
    {
        private static readonly int BodyId = Shader.PropertyToID("_BodyColor");
        private static readonly int FinId = Shader.PropertyToID("_FinColor");
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int SrcBodyId = Shader.PropertyToID("_SrcBody");
        private static readonly int SrcFinId = Shader.PropertyToID("_SrcFin");

        // Colours Kenney's palette uses on each fish model (sampled from the colormap via the mesh UVs).
        private static readonly Color SmallBody = new Color32(0xFF, 0x81, 0x44, 0xFF), SmallFin = new Color32(0xFF, 0xB4, 0x49, 0xFF);
        private static readonly Color LargeBody = new Color32(0x6A, 0x6F, 0x87, 0xFF), LargeFin = new Color32(0xE0, 0x5A, 0x4A, 0xFF);

        public static GameObject Spawn(FishSpecies species, float lengthCm)
        {
            var ga = GameAssets.Instance;
            GameObject prefab;
            float targetSize;
            bool recolor = true;
            switch (species.model)
            {
                case CatchModel.LargeFish:
                    prefab = ga.fishLargeModel;
                    targetSize = Mathf.Lerp(0.6f, 1.5f, Mathf.InverseLerp(25f, 200f, lengthCm));
                    break;
                case CatchModel.Bottle:
                    prefab = ga.bottleModel; targetSize = 0.45f; recolor = false; break;
                case CatchModel.Teacup:
                    prefab = ga.teacupModel; targetSize = 0.38f; recolor = false; break;
                case CatchModel.Driftwood:
                    prefab = ga.driftwoodModel; targetSize = 0.8f; recolor = false; break;
                default:
                    prefab = ga.fishSmallModel;
                    targetSize = Mathf.Lerp(0.32f, 0.62f, Mathf.InverseLerp(4f, 40f, lengthCm));
                    break;
            }

            var root = new GameObject($"Catch_{species.id}");
            if (prefab == null) return root;
            var model = Object.Instantiate(prefab, root.transform);
            model.transform.localPosition = Vector3.zero;

            // Normalise: longest horizontal extent -> targetSize, centred on the root.
            var rs = model.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                float longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
                float s = targetSize / Mathf.Max(0.001f, longest);
                model.transform.localScale = Vector3.one * s;
                model.transform.localPosition = -(b.center - root.transform.position) * s;
            }

            if (recolor && ga.fishMaterial != null)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor(BodyId, species.body);
                mpb.SetColor(FinId, species.fins);
                mpb.SetColor(GlowId, species.glow);
                bool large = species.model == CatchModel.LargeFish;
                mpb.SetColor(SrcBodyId, large ? LargeBody : SmallBody);
                mpb.SetColor(SrcFinId, large ? LargeFin : SmallFin);
                foreach (var r in rs)
                {
                    r.sharedMaterial = ga.fishMaterial;
                    r.SetPropertyBlock(mpb);
                }
            }
            if (species.glow.maxColorComponent > 0.01f)
            {
                var light = new GameObject("Glow").AddComponent<Light>();
                light.transform.SetParent(root.transform, false);
                light.type = LightType.Point;
                light.color = species.glow * 2f;
                light.range = 3f;
                light.intensity = 2f;
            }
            return root;
        }
    }
}
