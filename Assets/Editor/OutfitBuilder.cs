using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Environment;
using UntitledGame.Player;

namespace UntitledGame.EditorTools
{
    /// <summary>
    /// Climate clothes for the Kenney mini characters. They're one skinned body mesh (the head is separate) coloured from
    /// a shared palette texture, with no separate clothing, so each region's outfit is a copy of the body mesh whose
    /// clothing vertices (torso, arms and legs, but not skin) point at other palette colours, keeping their shading:
    /// light linen and khaki in the desert, bright coats and dark trousers in the snow, white space suits on Mars.
    /// Accessories go on the head bone (sun hats; beanies and scarves; a glass space helmet) and the torso (an air
    /// pack). RegionOutfit swaps them at runtime.
    /// </summary>
    public static class OutfitBuilder
    {
        public enum Kind { Player, Mei, Keeper, Driver }

        private const string AtlasPath = "Assets/ThirdParty/Kenney/MiniCharacters/Textures/colormap.png";
        private static Texture2D _atlas;
        private static List<(Color c, Vector2 uv)> _texels;

        private static Color C(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        private static readonly Color[] DesertTops = { C("#F3EADB"), C("#E8D9BC"), C("#DCE6EF"), C("#F0DFB0"), C("#E9E4DA") };
        private static readonly Color[] DesertBottoms = { C("#C7A675"), C("#B48F63"), C("#D6C6A2") };
        private static readonly Color[] DesertHats = { C("#E7CD8B"), C("#DDBE78"), C("#EAD7A6") };
        private static readonly Color[] SnowCoats = { C("#C8463E"), C("#3E6FB0"), C("#3F8F5A"), C("#E0893A"), C("#7A55B8"), C("#2F5A8A"), C("#B8434F") };
        private static readonly Color[] SnowBottoms = { C("#3A3D48"), C("#4A4E5E"), C("#3E3A36") };
        private static readonly Color[] SuitStripes = { C("#E8743B"), C("#3E6FB0"), C("#D9534A"), C("#E2B23E"), C("#5FAF8A"), C("#8A6AD8") };
        private static readonly Color[] Knits = { C("#F2EEE6"), C("#E2B23E"), C("#D45A4E"), C("#5E8FC9"), C("#6FAF6A") };

        private static void LoadAtlas()
        {
            if (_texels != null) return;
            _atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            _atlas.LoadImage(File.ReadAllBytes(AtlasPath));
            _texels = new List<(Color, Vector2)>();
            var seen = new HashSet<int>();
            int w = _atlas.width, h = _atlas.height;
            for (int y = 2; y < h; y += 4)
            for (int x = 2; x < w; x += 4)
            {
                Color c = _atlas.GetPixel(x, y);
                int key = ((int)(c.r * 63) << 12) | ((int)(c.g * 63) << 6) | (int)(c.b * 63);
                if (c.r + c.g + c.b < 0.02f || !seen.Add(key)) continue; // skip the empty black area
                _texels.Add((c, new Vector2((x + 0.5f) / w, (y + 0.5f) / h)));
            }
        }

        private static Color Sample(Vector2 uv) => _atlas.GetPixelBilinear(uv.x, uv.y);

        private static Vector2 UvFor(Color target)
        {
            float best = float.MaxValue;
            Vector2 uv = Vector2.zero;
            foreach (var (c, u) in _texels)
            {
                float d = (c.r - target.r) * (c.r - target.r) * 0.9f + (c.g - target.g) * (c.g - target.g) * 1.2f + (c.b - target.b) * (c.b - target.b) * 0.7f;
                if (d < best) { best = d; uv = u; }
            }
            return uv;
        }

        /// <summary>The palette's skin tones (peach to brown): kept on hands, necks and bare legs.</summary>
        private static bool IsSkin(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return h > 0.015f && h < 0.11f && s > 0.2f && s < 0.68f && v > 0.35f;
        }

        /// <summary>Gives a character its regional outfits (call after the model is attached).</summary>
        /// <param name="onlyRegion">Build just this region's outfit (a keeper who lives there); -1 = every region.</param>
        public static void Dress(GameObject owner, CharacterAnimator anim, string kitPath, int seed, Kind kind, int onlyRegion = -1)
        {
            LoadAtlas();
            // The body is the skinned mesh whose vertices follow the torso, arms and legs (the head is its own mesh).
            var body = anim.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(BodyVertices).FirstOrDefault();
            if (body == null || body.sharedMesh == null || BodyVertices(body) == 0) return;
            int regions = Regions.All.Length;
            var meshes = new Mesh[regions];
            var hats = new GameObject[regions];
            var extras = new GameObject[regions];
            var bodyExtras = new GameObject[regions];
            meshes[0] = body.sharedMesh;

            // The head's size and position: every renderer but the body.
            var headRenderers = anim.GetComponentsInChildren<Renderer>(true).Where(r => r != body).ToList();
            if (headRenderers.Count == 0) return;
            Bounds? measured = null;
            foreach (var r in headRenderers)
            {
                Bounds b = SkinnedBounds(r);
                if (measured == null) measured = b;
                else { var m = measured.Value; m.Encapsulate(b); measured = m; }
            }
            Bounds hb = measured.Value;
            var head = anim.FindBone("head") ?? anim.transform;
            Vector3 fwd = owner.transform.forward;

            string model = Path.GetFileName(kitPath);
            for (int region = 1; region < regions; region++)
            {
                if (onlyRegion >= 0 && region != onlyRegion) continue;
                string id = Regions.All[region].id;
                if (id == "desert")
                {
                    meshes[region] = Recolour(body, owner.transform, $"{model}_{id}_{seed}", Pick(DesertTops, seed), Pick(DesertBottoms, seed + 1), false);
                    if (kind != Kind.Driver) hats[region] = SunHat(head, hb, fwd, Pick(DesertHats, seed));
                }
                else if (id == "snow")
                {
                    meshes[region] = Recolour(body, owner.transform, $"{model}_{id}_{seed}", Pick(SnowCoats, seed), Pick(SnowBottoms, seed + 2), true);
                    Color knit = Pick(Knits, seed + 3);
                    if (kind != Kind.Driver) hats[region] = Beanie(head, hb, knit, Pick(Knits, seed + 4));
                    extras[region] = Scarf(head, hb, fwd, Pick(Knits, seed + (kind == Kind.Driver ? 1 : 4)));
                }
                else if (id == "mars")
                {
                    meshes[region] = Recolour(body, owner.transform, $"{model}_{id}_{seed}", C("#E9ECEF"), C("#C9CED6"), true);
                    extras[region] = Helmet(head, hb, Pick(SuitStripes, seed));
                    var torso = anim.FindBone("torso");
                    if (torso != null) bodyExtras[region] = AirPack(torso, SkinnedBounds(body), hb, fwd, Pick(SuitStripes, seed));
                }
            }
            var outfit = owner.AddComponent<RegionOutfit>();
            outfit.Configure(body, meshes, hats, extras, kind == Kind.Mei, bodyExtras);
            outfit.Apply(0);
        }

        private static int BodyVertices(SkinnedMeshRenderer r)
        {
            if (r.sharedMesh == null) return 0;
            var bones = r.bones;
            return r.sharedMesh.boneWeights.Count(w => w.boneIndex0 < bones.Length && bones[w.boneIndex0] != null &&
                (bones[w.boneIndex0].name == "torso" || bones[w.boneIndex0].name.StartsWith("arm") || bones[w.boneIndex0].name.StartsWith("leg")));
        }

        /// <summary>
        /// World bounds of a mesh as it's posed now, skinned by hand (bone matrix x bind pose for each vertex): exact in
        /// the editor, where a skinned renderer's own bounds are loose and BakeMesh loses the model's scale.
        /// </summary>
        private static Bounds SkinnedBounds(Renderer r)
        {
            if (!(r is SkinnedMeshRenderer smr) || smr.sharedMesh == null)
                return r.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh != null ? TransformBounds(r.transform, mf.sharedMesh) : r.bounds;
            var mesh = smr.sharedMesh;
            var verts = mesh.vertices;
            var bw = mesh.boneWeights;
            var bind = mesh.bindposes;
            var bones = smr.bones;
            Bounds? b = null;
            for (int i = 0; i < verts.Length; i++)
            {
                int bi = bw.Length > i ? bw[i].boneIndex0 : 0;
                Vector3 p = bi < bones.Length && bones[bi] != null && bi < bind.Length
                    ? (bones[bi].localToWorldMatrix * bind[bi]).MultiplyPoint3x4(verts[i])
                    : smr.transform.TransformPoint(verts[i]);
                if (b == null) b = new Bounds(p, Vector3.zero);
                else { var x = b.Value; x.Encapsulate(p); b = x; }
            }
            return b ?? r.bounds;
        }

        /// <summary>Each vertex's world position as posed now (skinned by hand, like SkinnedBounds).</summary>
        private static Vector3[] SkinnedPositions(SkinnedMeshRenderer smr)
        {
            var mesh = smr.sharedMesh;
            var verts = mesh.vertices;
            var bw = mesh.boneWeights;
            var bind = mesh.bindposes;
            var bones = smr.bones;
            var p = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                int bi = bw.Length > i ? bw[i].boneIndex0 : 0;
                p[i] = bi < bones.Length && bones[bi] != null && bi < bind.Length
                    ? (bones[bi].localToWorldMatrix * bind[bi]).MultiplyPoint3x4(verts[i])
                    : smr.transform.TransformPoint(verts[i]);
            }
            return p;
        }

        private static Bounds TransformBounds(Transform t, Mesh mesh)
        {
            var verts = mesh.vertices;
            var b = new Bounds(t.TransformPoint(verts[0]), Vector3.zero);
            foreach (var v in verts) b.Encapsulate(t.TransformPoint(v));
            return b;
        }

        private static Color Pick(Color[] list, int i) => list[((i % list.Length) + list.Length) % list.Length];

        /// <summary>
        /// A copy of the body mesh with its clothes pointed at new colours (shading kept). Skin stays, unless
        /// <paramref name="coverSkin"/> (the snow): then bare arms get sleeves (all but the hands) and bare legs trousers.
        /// </summary>
        private static Mesh Recolour(SkinnedMeshRenderer body, Transform owner, string name, Color top, Color bottom, bool coverSkin)
        {
            var src = body.sharedMesh;
            var world = SkinnedPositions(body);
            float reach = 0f;
            for (int i = 0; i < world.Length; i++)
                if (body.bones[src.boneWeights[i].boneIndex0].name.StartsWith("arm"))
                    reach = Mathf.Max(reach, Mathf.Abs(Vector3.Dot(world[i] - owner.position, owner.right)));
            var mesh = Object.Instantiate(src);
            mesh.name = "Outfit_" + name;
            var uv = src.uv;
            var bw = src.boneWeights;
            var bones = body.bones;
            for (int i = 0; i < uv.Length; i++)
            {
                if (bw.Length <= i || bw[i].boneIndex0 >= bones.Length || bones[bw[i].boneIndex0] == null) continue;
                string bone = bones[bw[i].boneIndex0].name;
                bool legs = bone.StartsWith("leg"), upper = bone == "torso" || bone.StartsWith("arm");
                if (!legs && !upper) continue;
                Color c = Sample(uv[i]);
                if (IsSkin(c))
                {
                    bool hand = !legs && Mathf.Abs(Vector3.Dot(world[i] - owner.position, owner.right)) > reach * 0.8f;
                    if (!coverSkin || hand || bone == "torso") continue; // the neck stays skin
                    c = new Color(0.6f, 0.6f, 0.6f);                    // mid shading for the new sleeve / trouser leg
                }
                Color.RGBToHSV(c, out _, out _, out float v0);
                Color.RGBToHSV(legs ? bottom : top, out float th, out float ts, out float tv);
                float v = Mathf.Clamp01(tv * Mathf.Lerp(0.72f, 1.1f, v0));
                uv[i] = UvFor(Color.HSVToRGB(th, ts, v));
            }
            mesh.uv = uv;
            string path = $"{ComfyAssets.MeshFolder}/{mesh.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ------------------------------------------------------------------ accessories (world-sized, then pinned to the head bone)

        private static Material Mat(Color c)
        {
            string name = "Outfit_" + ColorUtility.ToHtmlStringRGB(c);
            string path = $"{ComfyAssets.MatFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var m = new Material(ComfyAssets.PlainLitMat) { name = name };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_VertexColorWeight", 0f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static GameObject Group(Transform head, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(head, false);
            g.SetActive(false);
            return g;
        }

        private static void Piece(GameObject group, PrimitiveType type, Vector3 worldPos, Vector3 worldSize, Quaternion rot, Color c)
        {
            var p = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(p.GetComponent<Collider>());
            p.GetComponent<Renderer>().sharedMaterial = Mat(c);
            p.transform.SetPositionAndRotation(worldPos, rot);
            p.transform.localScale = worldSize;
            p.transform.SetParent(group.transform, true);
        }

        private static Material _glass;

        /// <summary>A see-through visor material (the particle shader's alpha blending, plain white tinted pale blue).</summary>
        private static Material Glass()
        {
            if (_glass != null) return _glass;
            const string path = ComfyAssets.MatFolder + "/Outfit_Glass.mat";
            _glass = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (_glass != null) return _glass;
            _glass = new Material(GameAssets.Instance.splashMaterial) { name = "Outfit_Glass" };
            _glass.SetTexture("_BaseMap", null);
            _glass.SetColor("_BaseColor", new Color(0.78f, 0.9f, 1f, 0.22f));
            AssetDatabase.CreateAsset(_glass, path);
            return _glass;
        }

        /// <summary>A glass bubble helmet with a white collar and a little antenna.</summary>
        private static GameObject Helmet(Transform head, Bounds hb, Color stripe)
        {
            var g = Group(head, "Outfit_Helmet");
            float d = Mathf.Max(hb.size.x, hb.size.y, hb.size.z) * 1.15f;
            Vector3 c = hb.center + Vector3.up * hb.size.y * 0.05f;
            var bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(bubble.GetComponent<Collider>());
            var br = bubble.GetComponent<Renderer>();
            br.sharedMaterial = Glass();
            br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bubble.transform.SetPositionAndRotation(c, Quaternion.identity);
            bubble.transform.localScale = Vector3.one * d;
            bubble.transform.SetParent(g.transform, true);
            Vector3 neck = new Vector3(hb.center.x, hb.min.y + hb.size.y * 0.02f, hb.center.z);
            Piece(g, PrimitiveType.Cylinder, neck, new Vector3(d * 0.72f, hb.size.y * 0.07f, d * 0.72f), Quaternion.identity, C("#E9ECEF"));
            Piece(g, PrimitiveType.Cylinder, neck + Vector3.up * hb.size.y * 0.07f, new Vector3(d * 0.74f, hb.size.y * 0.02f, d * 0.74f), Quaternion.identity, stripe);
            Vector3 top = c + Vector3.up * d * 0.5f;
            Piece(g, PrimitiveType.Cylinder, top + Vector3.up * 0.08f + Vector3.right * d * 0.18f, new Vector3(0.015f, 0.1f, 0.015f), Quaternion.identity, C("#9AA3AD"));
            Piece(g, PrimitiveType.Sphere, top + Vector3.up * 0.19f + Vector3.right * d * 0.18f, Vector3.one * 0.045f, Quaternion.identity, stripe);
            return g;
        }

        /// <summary>An air pack on the back, with two tanks.</summary>
        private static GameObject AirPack(Transform torso, Bounds body, Bounds hb, Vector3 fwd, Color stripe)
        {
            var g = Group(torso, "Outfit_AirPack");
            float w = Mathf.Min(hb.size.x, hb.size.z);
            Vector3 back = new Vector3(hb.center.x, body.max.y - (body.max.y - body.min.y) * 0.3f, hb.center.z) - fwd * w * 0.42f;
            Quaternion rot = Quaternion.LookRotation(fwd);
            Piece(g, PrimitiveType.Cube, back, new Vector3(w * 0.7f, (body.max.y - body.min.y) * 0.42f, w * 0.22f), rot, C("#D5DAE0"));
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            foreach (float sx in new[] { -0.18f, 0.18f })
                Piece(g, PrimitiveType.Cylinder, back - fwd * w * 0.14f + side * w * sx, new Vector3(w * 0.16f, (body.max.y - body.min.y) * 0.2f, w * 0.16f), Quaternion.identity, stripe);
            return g;
        }

        private static GameObject SunHat(Transform head, Bounds hb, Vector3 fwd, Color straw)
        {
            var g = Group(head, "Outfit_SunHat");
            float w = Mathf.Min(hb.size.x, hb.size.z) * 1.1f; // depth: the width includes ears and pigtails
            float y = hb.max.y - hb.size.y * 0.16f;
            Vector3 c = new Vector3(hb.center.x, y, hb.center.z);
            Piece(g, PrimitiveType.Cylinder, c, new Vector3(w * 1.75f, 0.012f, w * 1.75f), Quaternion.identity, straw);
            Piece(g, PrimitiveType.Cylinder, c + Vector3.up * hb.size.y * 0.12f, new Vector3(w * 0.98f, hb.size.y * 0.12f, w * 0.98f), Quaternion.identity, straw);
            Piece(g, PrimitiveType.Cylinder, c + Vector3.up * hb.size.y * 0.035f, new Vector3(w * 1.0f, hb.size.y * 0.035f, w * 1.0f), Quaternion.identity, C("#8A5A3A"));
            return g;
        }

        private static GameObject Beanie(Transform head, Bounds hb, Color knit, Color pom)
        {
            var g = Group(head, "Outfit_Beanie");
            float sx = Mathf.Min(hb.size.x, hb.size.z) * 1.18f, sz = hb.size.z * 1.08f;
            Vector3 top = new Vector3(hb.center.x, hb.max.y, hb.center.z);
            Piece(g, PrimitiveType.Sphere, top - Vector3.up * hb.size.y * 0.08f, new Vector3(sx, hb.size.y * 0.62f, sz), Quaternion.identity, knit);
            Piece(g, PrimitiveType.Cylinder, top - Vector3.up * hb.size.y * 0.3f, new Vector3(sx * 1.03f, hb.size.y * 0.06f, sz * 1.03f), Quaternion.identity, knit * 0.85f);
            Piece(g, PrimitiveType.Sphere, top + Vector3.up * hb.size.y * 0.2f, Vector3.one * Mathf.Max(sx, sz) * 0.24f, Quaternion.identity, pom);
            return g;
        }

        private static GameObject Scarf(Transform head, Bounds hb, Vector3 fwd, Color wool)
        {
            var g = Group(head, "Outfit_Scarf");
            float w = Mathf.Min(hb.size.x, hb.size.z) * 1.1f;
            Vector3 neck = new Vector3(hb.center.x, hb.min.y + hb.size.y * 0.02f, hb.center.z);
            Piece(g, PrimitiveType.Cylinder, neck, new Vector3(w * 0.62f, hb.size.y * 0.06f, w * 0.62f), Quaternion.identity, wool);
            Vector3 tail = neck + fwd * w * 0.3f + Vector3.right * w * 0.12f - Vector3.up * hb.size.y * 0.16f;
            Piece(g, PrimitiveType.Cube, tail, new Vector3(w * 0.16f, hb.size.y * 0.3f, w * 0.05f), Quaternion.LookRotation(fwd), wool);
            return g;
        }
    }
}
