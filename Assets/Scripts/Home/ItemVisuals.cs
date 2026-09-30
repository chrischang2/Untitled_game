using UnityEngine;
using UnityEngine.Rendering;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Environment;

namespace UntitledGame.Home
{
    /// <summary>Builds the 3D model for a catalog item (Kenney prefab or a small code-built prop).</summary>
    public static class ItemVisuals
    {
        public static GameObject Create(ItemDef def)
        {
            var root = new GameObject("Item_" + def.id);
            var ga = GameAssets.Instance;
            if (!string.IsNullOrEmpty(def.model) && def.model.StartsWith("proc:"))
            {
                BuildProcedural(def.model.Substring(5), root.transform);
            }
            else
            {
                var prefab = ga.PrefabFor(def.id);
                if (prefab != null)
                {
                    var m = Object.Instantiate(prefab, root.transform);
                    m.transform.localPosition = Vector3.zero;
                    m.transform.localRotation = Quaternion.identity;
                    m.transform.localScale = Vector3.one * def.modelScale;
                }
            }
            if (def.id == "floor_lamp" || def.id == "street_lamp")
            {
                float h = def.id == "street_lamp" ? 1.38f * def.modelScale : 1.1f * def.modelScale;
                AddNightLight(root.transform, new Vector3(0, h, 0), new Color(1f, 0.8f, 0.5f), def.id == "street_lamp" ? 8f : 6f);
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            return root;
        }

        public static void AddCollider(GameObject go, float shrink = 0.85f)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(b.center);
            box.size = new Vector3(b.size.x * shrink, b.size.y, b.size.z * shrink);
        }

        private static void AddNightLight(Transform parent, Vector3 local, Color color, float range)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.shadows = LightShadows.None;
            go.AddComponent<NightLight>().Configure(l, null, 1.8f, 0f, false);
        }

        private static readonly MaterialPropertyBlock Mpb = new MaterialPropertyBlock();

        private static Transform Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localEulerAngles = euler;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = GameAssets.Instance.bobberWhite;
            Mpb.Clear();
            Mpb.SetColor("_BaseColor", color);
            r.SetPropertyBlock(Mpb);
            return go.transform;
        }

        private static void BuildProcedural(string kind, Transform root)
        {
            switch (kind)
            {
                case "cat_bed":
                    Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.09f, 0), new Vector3(0.95f, 0.09f, 0.95f), new Color(0.93f, 0.55f, 0.6f));
                    Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.13f, 0), new Vector3(0.72f, 0.07f, 0.72f), new Color(1f, 0.93f, 0.85f));
                    break;
                case "cat_bowl":
                    Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.05f, 0), new Vector3(0.42f, 0.05f, 0.42f), new Color(0.35f, 0.6f, 0.9f));
                    var food = Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.09f, 0), new Vector3(0.32f, 0.02f, 0.32f), new Color(0.55f, 0.35f, 0.2f));
                    food.name = "Food";
                    break;
                case "yarn":
                    Prim(PrimitiveType.Sphere, root, new Vector3(0, 0.14f, 0), Vector3.one * 0.28f, new Color(0.9f, 0.3f, 0.35f));
                    Prim(PrimitiveType.Cylinder, root, new Vector3(0.2f, 0.02f, 0.05f), new Vector3(0.03f, 0.14f, 0.03f), new Color(0.9f, 0.3f, 0.35f), new Vector3(0, 0, 80f));
                    break;
                case "scratcher":
                    Prim(PrimitiveType.Cube, root, new Vector3(0, 0.04f, 0), new Vector3(0.6f, 0.08f, 0.6f), new Color(0.82f, 0.7f, 0.5f));
                    Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.5f, 0), new Vector3(0.18f, 0.45f, 0.18f), new Color(0.76f, 0.66f, 0.46f));
                    Prim(PrimitiveType.Cube, root, new Vector3(0, 0.97f, 0), new Vector3(0.45f, 0.06f, 0.45f), new Color(0.6f, 0.45f, 0.35f));
                    break;
            }
        }

        public static void SetBowlFilled(GameObject bowl, bool filled)
        {
            var food = bowl.transform.Find("Food");
            if (food != null) food.gameObject.SetActive(filled);
        }
    }
}
