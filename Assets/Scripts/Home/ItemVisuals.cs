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
            if (def.glows)
            {
                // Lamps, lanterns and stoves light up at night, at about the top of the model.
                float h = 0.6f;
                var rs = root.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    Bounds b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    h = Mathf.Max(0.3f, b.size.y * 0.85f);
                }
                Color c = def.id == "s_lantern" ? new Color(1f, 0.45f, 0.35f) : def.id == "m_crystal" ? new Color(0.7f, 0.55f, 1f) : def.id == "sig_mars" ? new Color(0.6f, 0.75f, 1f) : new Color(1f, 0.8f, 0.5f);
                AddNightLight(root.transform, new Vector3(0, h, 0), c, def.id == "street_lamp" ? 8f : 6f);
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
                case "dutar":
                    // A two-stringed lute leaning against the wall: a round bowl and a long neck.
                    Prim(PrimitiveType.Sphere, root, new Vector3(0, 0.16f, 0), new Vector3(0.28f, 0.3f, 0.16f), new Color(0.6f, 0.36f, 0.18f));
                    Prim(PrimitiveType.Cube, root, new Vector3(0, 0.62f, 0.02f), new Vector3(0.05f, 0.75f, 0.04f), new Color(0.45f, 0.27f, 0.14f), new Vector3(-8f, 0, 0));
                    Prim(PrimitiveType.Cube, root, new Vector3(0, 1.0f, 0.07f), new Vector3(0.07f, 0.1f, 0.05f), new Color(0.35f, 0.2f, 0.1f), new Vector3(-8f, 0, 0));
                    break;
                case "pomegranate":
                    Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.12f, 0), new Vector3(0.36f, 0.12f, 0.36f), new Color(0.72f, 0.42f, 0.25f));
                    Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.42f, 0), new Vector3(0.05f, 0.2f, 0.05f), new Color(0.45f, 0.3f, 0.18f));
                    Prim(PrimitiveType.Sphere, root, new Vector3(0, 0.78f, 0), new Vector3(0.6f, 0.5f, 0.6f), new Color(0.36f, 0.56f, 0.28f));
                    foreach (var at in new[] { new Vector3(0.2f, 0.72f, 0.18f), new Vector3(-0.22f, 0.8f, 0.05f), new Vector3(0.05f, 0.66f, -0.24f) })
                        Prim(PrimitiveType.Sphere, root, at, Vector3.one * 0.1f, new Color(0.82f, 0.18f, 0.2f));
                    break;
                case "tonur":
                    // A round clay naan oven with an opening on top.
                    Prim(PrimitiveType.Sphere, root, new Vector3(0, 0.3f, 0), new Vector3(0.75f, 0.65f, 0.75f), new Color(0.78f, 0.6f, 0.42f));
                    Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.6f, 0), new Vector3(0.28f, 0.03f, 0.28f), new Color(0.25f, 0.12f, 0.06f));
                    Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.62f, 0), new Vector3(0.18f, 0.01f, 0.18f), new Color(1f, 0.5f, 0.15f));
                    break;
                case "kang":
                    // The heated brick bed: a brick platform with a stove door, a straw mat and a floral quilt.
                    Prim(PrimitiveType.Cube, root, new Vector3(0, 0.22f, 0), new Vector3(1.9f, 0.44f, 1.1f), new Color(0.62f, 0.3f, 0.24f));
                    Prim(PrimitiveType.Cube, root, new Vector3(0, 0.45f, 0), new Vector3(1.92f, 0.03f, 1.12f), new Color(0.82f, 0.68f, 0.42f));
                    Prim(PrimitiveType.Cube, root, new Vector3(0.25f, 0.5f, 0), new Vector3(1.2f, 0.08f, 1.0f), new Color(0.82f, 0.22f, 0.24f));
                    Prim(PrimitiveType.Cube, root, new Vector3(0.25f, 0.545f, 0), new Vector3(1.0f, 0.01f, 0.8f), new Color(0.3f, 0.62f, 0.36f));
                    Prim(PrimitiveType.Cube, root, new Vector3(-0.72f, 0.54f, 0), new Vector3(0.3f, 0.1f, 0.6f), new Color(0.95f, 0.9f, 0.8f));
                    Prim(PrimitiveType.Cube, root, new Vector3(0.6f, 0.18f, 0.56f), new Vector3(0.26f, 0.2f, 0.02f), new Color(0.2f, 0.18f, 0.18f));
                    break;
                case "redlantern":
                    Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.6f, 0), new Vector3(0.03f, 0.6f, 0.03f), new Color(0.35f, 0.22f, 0.15f));
                    Prim(PrimitiveType.Sphere, root, new Vector3(0, 1.05f, 0), new Vector3(0.45f, 0.38f, 0.45f), new Color(0.88f, 0.15f, 0.12f));
                    Prim(PrimitiveType.Cylinder, root, new Vector3(0, 1.26f, 0), new Vector3(0.2f, 0.03f, 0.2f), new Color(0.9f, 0.72f, 0.2f));
                    Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.84f, 0), new Vector3(0.2f, 0.03f, 0.2f), new Color(0.9f, 0.72f, 0.2f));
                    Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.72f, 0), new Vector3(0.04f, 0.1f, 0.04f), new Color(0.9f, 0.72f, 0.2f));
                    Prim(PrimitiveType.Cube, root, new Vector3(0, 0.02f, 0), new Vector3(0.3f, 0.04f, 0.3f), new Color(0.35f, 0.22f, 0.15f));
                    break;
                case "sleeppod":
                    // A capsule bed with a tinted lid and a soft blue light strip.
                    Prim(PrimitiveType.Cube, root, new Vector3(0, 0.2f, 0), new Vector3(1.9f, 0.4f, 0.95f), new Color(0.9f, 0.92f, 0.94f));
                    Prim(PrimitiveType.Capsule, root, new Vector3(0, 0.45f, 0), new Vector3(0.85f, 0.95f, 0.6f), new Color(0.55f, 0.7f, 0.82f), new Vector3(0, 0, 90f));
                    Prim(PrimitiveType.Cube, root, new Vector3(0, 0.08f, 0.48f), new Vector3(1.7f, 0.04f, 0.02f), new Color(0.45f, 0.8f, 1f));
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
