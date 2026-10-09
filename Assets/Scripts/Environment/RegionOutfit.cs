using UnityEngine;

namespace UntitledGame.Environment
{
    /// <summary>
    /// A character's clothes for each region (the player, Mei, the shopkeepers). The Kenney characters are one mesh
    /// coloured from a shared palette texture, so an outfit is a copy of the body mesh whose clothing points at other
    /// colours (light linen in the desert, warm coats in the snow, space suits on Mars; built by the editor's
    /// OutfitBuilder), plus accessories pinned to the head (sun hats, beanies, scarves, space helmets) and the body
    /// (an air pack). Index = region (0 keeps the original look).
    /// </summary>
    public class RegionOutfit : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer body;
        [SerializeField] private Mesh[] meshes = new Mesh[0];
        [SerializeField] private GameObject[] hats = new GameObject[0];
        [SerializeField] private GameObject[] extras = new GameObject[0];
        [SerializeField] private GameObject[] bodyExtras = new GameObject[0];
        /// <summary>Mei: her region hat stays off while she wears a sun hat from 小方's shop.</summary>
        [SerializeField] private bool hatYieldsToCosmetic;

        public SkinnedMeshRenderer Body => body;
        public int Shown { get; private set; } = -1;

        public void Configure(SkinnedMeshRenderer bodyRenderer, Mesh[] regionMeshes, GameObject[] regionHats, GameObject[] regionExtras, bool yieldsToCosmetic,
            GameObject[] regionBodyExtras = null)
        {
            bodyExtras = regionBodyExtras ?? new GameObject[0];
            body = bodyRenderer;
            meshes = regionMeshes;
            hats = regionHats;
            extras = regionExtras;
            hatYieldsToCosmetic = yieldsToCosmetic;
        }

        public GameObject Hat(int region) => region >= 0 && region < hats.Length ? hats[region] : null;
        public GameObject Extra(int region) => region >= 0 && region < extras.Length ? extras[region] : null;

        public void Apply(int region)
        {
            Shown = region;
            if (body != null && meshes.Length > 0)
            {
                var m = region < meshes.Length && meshes[region] != null ? meshes[region] : meshes[0];
                if (m != null && body.sharedMesh != m) body.sharedMesh = m;
            }
            bool cosmeticHat = hatYieldsToCosmetic && Application.isPlaying && Home.Cosmetics.Chosen("mei") != null;
            for (int r = 0; r < hats.Length; r++)
                if (hats[r] != null) hats[r].SetActive(r == region && !cosmeticHat);
            for (int r = 0; r < extras.Length; r++)
                if (extras[r] != null) extras[r].SetActive(r == region);
            for (int r = 0; r < bodyExtras.Length; r++)
                if (bodyExtras[r] != null) bodyExtras[r].SetActive(r == region);
        }

        /// <summary>Dresses everyone for a region (inactive characters too, like the driver before HSK 1).</summary>
        public static void ApplyAll(int region)
        {
            foreach (var o in FindObjectsByType<RegionOutfit>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                o.Apply(region);
        }
    }
}
