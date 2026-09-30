using UnityEngine;

namespace UntitledGame.Core
{
    /// <summary>
    /// Central references to generated materials, sprites and models that runtime code spawns.
    /// Populated by the editor world builder; lives in Resources so anything can reach it.
    /// </summary>
    [CreateAssetMenu(menuName = "Untitled Game/Game Assets", fileName = "GameAssets")]
    public class GameAssets : ScriptableObject
    {
        private static GameAssets _instance;
        public static GameAssets Instance => _instance != null ? _instance : (_instance = Resources.Load<GameAssets>("GameAssets"));

        [Header("Fish & catches")]
        public GameObject fishSmallModel;
        public GameObject fishLargeModel;
        public GameObject bottleModel;
        public GameObject teacupModel;
        public GameObject driftwoodModel;
        public Material fishMaterial;

        [Header("Fishing gear")]
        public Material bobberRed;
        public Material bobberWhite;
        public Material rodMaterial;
        public Material lineMaterial;

        [Header("Effects")]
        public Material splashMaterial;
        public Material rippleMaterial;
        public Material fireflyMaterial;
        public Material fireMaterial;
        public Material smokeMaterial;
        public Material glowMaterial;
        public Material rainMaterial;
        public Material sparkleMaterial;

        [Header("Shop items (Kenney models, remapped materials)")]
        public System.Collections.Generic.List<ItemPrefab> itemPrefabs = new System.Collections.Generic.List<ItemPrefab>();

        public GameObject PrefabFor(string id) => itemPrefabs.Find(p => p.id == id)?.prefab;

        [Header("UI")]
        public Sprite roundedRect;
        public Sprite roundedRectSmall;
        public Sprite softShadow;
        public Sprite circle;
        public Sprite fishIcon;
        public Sprite ring;
    }

    [System.Serializable]
    public class ItemPrefab
    {
        public string id;
        public GameObject prefab;
    }
}
