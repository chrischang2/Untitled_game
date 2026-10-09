using System.Linq;
using UnityEngine;
using UntitledGame.Companion;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Environment;
using UntitledGame.Fishing;

namespace UntitledGame.Home
{
    /// <summary>
    /// Cosmetics from 小方's shop: paint for the boat, a hat for Tangyuan, a sun hat for Mei, and the colour of the
    /// house's roof. Buying one puts it on straight away; the bag lets you switch between the ones you own.
    /// </summary>
    public class Cosmetics : MonoBehaviour
    {
        public static readonly string[] Targets = { "boat", "cat", "mei", "house" };

        public static Cosmetics Instance { get; private set; }

        private GameObject _catHat, _meiHat;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int VertexWeight = Shader.PropertyToID("_VertexColorWeight");

        private void Awake() => Instance = this;

        private void Start()
        {
            Inventory.Changed += Apply;
            Apply();
        }

        private void OnDestroy()
        {
            Inventory.Changed -= Apply;
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ choices (saved)

        public static string Chosen(string target)
        {
            var d = SaveSystem.Data;
            string id = target switch { "boat" => d.cosBoat, "cat" => d.cosCat, "mei" => d.cosMei, "house" => d.cosHouse, _ => null };
            return !string.IsNullOrEmpty(id) && Inventory.Owns(id) ? id : null;
        }

        /// <summary>Wear / use a cosmetic (null takes it off).</summary>
        public static void Choose(string target, string itemId)
        {
            var d = SaveSystem.Data;
            switch (target)
            {
                case "boat": d.cosBoat = itemId ?? ""; break;
                case "cat": d.cosCat = itemId ?? ""; break;
                case "mei": d.cosMei = itemId ?? ""; break;
                case "house": d.cosHouse = itemId ?? ""; break;
            }
            SaveSystem.Save();
            Instance?.Apply();
        }

        /// <summary>Just bought: put it on.</summary>
        public static void OnBought(ItemDef item)
        {
            if (item?.category == ItemCategory.Cosmetic) Choose(item.cosmeticFor, item.id);
        }

        private static Color ColorOf(string itemId)
        {
            var def = Catalog.Get(itemId);
            return def != null && ColorUtility.TryParseHtmlString(def.colorHex, out var c) ? c : Color.white;
        }

        // ------------------------------------------------------------------ applying

        public void Apply()
        {
            ApplyBoat();
            ApplyHouse();
            _catHat = ApplyHat(_catHat, PetController.Instance != null ? PetController.Instance.transform : null, Chosen("cat"), 0.55f, cat: true);
            var mei = CompanionBrain.Current;
            _meiHat = ApplyHat(_meiHat, mei != null ? mei.transform : null, Chosen("mei"), 1f, cat: false);
            // Her region hat (sun hat, beanie) comes off while she wears one of these.
            if (mei != null) mei.GetComponent<RegionOutfit>()?.Apply(Regions.Current);
        }

        private static void Tint(Renderer r, Color? c, float vertexWeight)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            if (c.HasValue)
            {
                mpb.SetColor(BaseColor, c.Value);
                mpb.SetFloat(VertexWeight, vertexWeight);
                r.SetPropertyBlock(mpb);
            }
            else r.SetPropertyBlock(null);
        }

        private void ApplyBoat()
        {
            var boat = Rowboat.Instance;
            if (boat == null) return;
            string id = Chosen("boat");
            // Keep some of the model's own shading: paint mixes with white.
            Color? paint = id != null ? Color.Lerp(Color.white, ColorOf(id), 0.75f) : (Color?)null;
            foreach (var r in boat.GetComponentsInChildren<Renderer>()) Tint(r, paint, 1f);
        }

        private void ApplyHouse()
        {
            var cabin = FindFirstObjectByType<Cabin>();
            var roofGroup = cabin != null ? cabin.transform.Find("Roof") : null;
            var roof = roofGroup != null ? (roofGroup.Find("Style_willowbay") ?? roofGroup) : null; // the log cabin's roof
            if (roof == null) return;
            string id = Chosen("house");
            foreach (var r in roof.GetComponentsInChildren<Renderer>()) Tint(r, id != null ? ColorOf(id) : (Color?)null, 0.25f);
        }

        /// <summary>A little hat on top of a character's head (rebuilt when the choice changes).</summary>
        private static GameObject ApplyHat(GameObject current, Transform wearer, string itemId, float scale, bool cat)
        {
            if (current != null && (itemId == null || current.name != "Hat_" + itemId))
            {
                Destroy(current);
                current = null;
            }
            if (wearer == null || itemId == null || current != null) return current;

            var rs = wearer.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToList();
            if (rs.Count == 0) return null;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            // Follow the head bone if the rig has one, otherwise sit on top of the model.
            Transform head = wearer.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.ToLowerInvariant().Contains("head"));
            var hat = new GameObject("Hat_" + itemId);
            hat.transform.SetParent(head != null ? head : wearer, false);
            hat.transform.position = new Vector3(b.center.x, b.max.y - (cat ? 0.02f : 0.06f), b.center.z);
            hat.transform.rotation = wearer.rotation;

            var mat = new Material(ComfyLit()) { name = "HatMat" };
            mat.SetColor(BaseColor, ColorOf(itemId));
            mat.SetFloat(VertexWeight, 0f);
            GameObject Part(PrimitiveType type, Vector3 pos, Vector3 size)
            {
                var p = GameObject.CreatePrimitive(type);
                Destroy(p.GetComponent<Collider>());
                p.transform.SetParent(hat.transform, false);
                p.transform.localPosition = pos * scale;
                p.transform.localScale = size * scale;
                p.GetComponent<Renderer>().sharedMaterial = mat;
                return p;
            }
            if (cat)
            {
                Part(PrimitiveType.Cylinder, new Vector3(0f, 0.05f, 0f), new Vector3(0.16f, 0.06f, 0.16f));
                Part(PrimitiveType.Sphere, new Vector3(0f, 0.13f, 0f), new Vector3(0.07f, 0.07f, 0.07f));
            }
            else
            {
                Part(PrimitiveType.Cylinder, new Vector3(0f, 0.01f, 0f), new Vector3(0.62f, 0.012f, 0.62f)); // brim
                Part(PrimitiveType.Cylinder, new Vector3(0f, 0.08f, 0f), new Vector3(0.34f, 0.07f, 0.34f)); // crown
            }
            return hat;
        }

        private static Material _lit;

        private static Material ComfyLit()
        {
            if (_lit != null) return _lit;
            var shader = Shader.Find("Comfy/Lit");
            _lit = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Lit"));
            return _lit;
        }
    }
}
