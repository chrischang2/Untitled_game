using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Environment;

namespace UntitledGame.Home
{
    /// <summary>Spawns and tracks furniture / pet items the player has placed around their camp.</summary>
    public class HomeItems : MonoBehaviour
    {
        public static HomeItems Instance { get; private set; }

        private readonly Dictionary<PlacedItem, GameObject> _spawned = new Dictionary<PlacedItem, GameObject>();

        public IEnumerable<PlacedItem> Placed => SaveSystem.Data.placed;

        private void Awake() => Instance = this;

        private void Start()
        {
            foreach (var p in SaveSystem.Data.placed.ToList()) Spawn(p);
            RefreshBowl();
        }

        private void Spawn(PlacedItem p)
        {
            var def = Catalog.Get(p.id);
            if (def == null) return;
            var go = ItemVisuals.Create(def);
            go.transform.SetParent(transform, true);
            go.transform.SetPositionAndRotation(new Vector3(p.x, p.y, p.z), Quaternion.Euler(0f, p.yaw, 0f));
            if (def.id != "rug") ItemVisuals.AddCollider(go);
            if (def.id == "cat_bowl") go.AddComponent<BowlInteractable>();
            _spawned[p] = go;
        }

        public void Place(string id, Vector3 position, float yaw)
        {
            if (!Inventory.Remove(id, 1)) return;
            var p = new PlacedItem { id = id, x = position.x, y = position.y, z = position.z, yaw = yaw };
            SaveSystem.Data.placed.Add(p);
            SaveSystem.Save();
            Spawn(p);
            RefreshBowl();
            AudioManager.Instance?.PlaySfx("SFX/rpg_dropLeather", 0.6f);
        }

        public void PutAway(PlacedItem p)
        {
            if (_spawned.TryGetValue(p, out var go)) Destroy(go);
            _spawned.Remove(p);
            SaveSystem.Data.placed.Remove(p);
            Inventory.Add(p.id, 1);
            AudioManager.Instance?.PlaySfx("SFX/rpg_cloth1", 0.5f);
        }

        /// <summary>Where the first placed item of this kind stands (null if none).</summary>
        public Transform Find(string id)
        {
            foreach (var kv in _spawned)
                if (kv.Key.id == id && kv.Value != null) return kv.Value.transform;
            return null;
        }

        public void RefreshBowl()
        {
            var bowl = Find("cat_bowl");
            if (bowl != null) ItemVisuals.SetBowlFilled(bowl.gameObject, SaveSystem.Data.pet.bowlFilled);
        }

        /// <summary>The home area: around the camp, the dock and the lake shore near them.</summary>
        public static bool InHomeArea(Vector3 p) =>
            Vector2.Distance(new Vector2(p.x, p.z), WorldShape.CampCenter) < 32f && !WorldShape.InMarket(p.x, p.z, 2f);
    }
}
