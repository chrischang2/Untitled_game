using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Environment;

namespace UntitledGame.Home
{
    /// <summary>
    /// Spawns and tracks furniture / pet items the player has placed around their camp. Each region has its own house:
    /// only what was placed in the current region is shown, and arriving somewhere new shows that house's things.
    /// </summary>
    public class HomeItems : MonoBehaviour
    {
        public static HomeItems Instance { get; private set; }

        private readonly Dictionary<PlacedItem, GameObject> _spawned = new Dictionary<PlacedItem, GameObject>();

        public IEnumerable<PlacedItem> Placed => SaveSystem.Data.placed.Where(Here);

        private static bool Here(PlacedItem p) => p.region == Regions.Current;

        private void Awake() => Instance = this;

        private void OnEnable() => Regions.Changed += Respawn;
        private void OnDisable() => Regions.Changed -= Respawn;

        /// <summary>Shows the current region's house: everything placed here, nothing from the other houses.</summary>
        public void Respawn()
        {
            foreach (var go in _spawned.Values) if (go != null) Destroy(go);
            _spawned.Clear();
            foreach (var p in SaveSystem.Data.placed.Where(Here).Where(p => !Stranded(p)).ToList()) Spawn(p);
            RefreshBowl();
        }

        private void Start()
        {
            var placed = SaveSystem.Data.placed.Where(Here).ToList();
            foreach (var p in placed.Where(p => !Stranded(p))) Spawn(p);
            RescueStranded();
            RefreshBowl();
            TrophyWall.Ensure();
        }

        /// <summary>Moves every stranded placed item into the house (and respawns it there). Returns how many moved.</summary>
        public int RescueStranded()
        {
            var stranded = SaveSystem.Data.placed.Where(Here).Where(Stranded).ToList();
            Physics.SyncTransforms();
            foreach (var p in stranded)
            {
                if (_spawned.TryGetValue(p, out var old) && old != null) Destroy(old);
                _spawned.Remove(p);
                MoveIntoHouse(p);
                Spawn(p);
                Physics.SyncTransforms();
            }
            if (stranded.Count > 0) SaveSystem.Save();
            return stranded.Count;
        }

        /// <summary>
        /// Placed before the coast and dock were reshaped, a thing can be left floating over the water or on the old
        /// pier. Anything not standing on dry land (or a cabin floor) at the right height is stranded.
        /// </summary>
        private static bool Stranded(PlacedItem p)
        {
            var pos = new Vector3(p.x, p.y, p.z);
            if (Cabin.IsInside(pos)) return Mathf.Abs(p.y - Cabin.GroundHeight(pos)) > 0.3f;
            if (WorldShape.IsOnDock(p.x, p.z)) return true;
            if (!Player.PlayerController.IsWalkable(pos)) return true;
            return Mathf.Abs(p.y - WorldShape.TerrainHeight(p.x, p.z)) > 0.5f;
        }

        /// <summary>Moves a stranded item to a free spot on the house floor (along the back and side walls).</summary>
        private static void MoveIntoHouse(PlacedItem p)
        {
            var cabin = Cabin.PlayerHouse;
            if (cabin == null) return;
            var spots = new[]
            {
                new Vector2(-0.75f, -0.6f), new Vector2(0.75f, -0.6f), new Vector2(0f, -0.6f), new Vector2(-0.75f, 0.1f),
                new Vector2(0.75f, 0.1f), new Vector2(-0.35f, -0.6f), new Vector2(0.35f, -0.6f), new Vector2(0f, 0f),
            };
            Vector3 chosen = cabin.FloorPoint(0f, 0f);
            foreach (var s in spots)
            {
                Vector3 at = cabin.FloorPoint(s.x, s.y);
                bool clear = true;
                foreach (var c in Physics.OverlapSphere(at + Vector3.up * 0.4f, 0.4f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (c is MeshCollider || c is CharacterController || c.isTrigger) continue; // floor, walls' mesh, the player
                    clear = false;
                    break;
                }
                if (clear)
                {
                    chosen = at;
                    break;
                }
            }
            Debug.Log($"[Home] {p.id} was stranded at ({p.x:0.0}, {p.y:0.00}, {p.z:0.0}); moved into the house at {chosen}");
            ChatAudit.Write("HOME", $"{p.id} was stranded at ({p.x:0.0}, {p.y:0.00}, {p.z:0.0}) off the pier/coast; moved into the house");
            p.x = chosen.x;
            p.y = chosen.y;
            p.z = chosen.z;
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
            else go.AddComponent<FurnitureInteractable>().Configure(p, def);
            _spawned[p] = go;
        }

        public void Place(string id, Vector3 position, float yaw)
        {
            if (!Inventory.Remove(id, 1)) return;
            var p = new PlacedItem { id = id, x = position.x, y = position.y, z = position.z, yaw = yaw, region = Regions.Current };
            SaveSystem.Data.placed.Add(p);
            SaveSystem.Save();
            Spawn(p);
            RefreshBowl();
            AudioManager.Instance?.PlaySfx("SFX/rpg_dropLeather", 0.6f);
        }

        /// <summary>Puts a placed item somewhere else.</summary>
        public void Move(PlacedItem p, Vector3 position, float yaw)
        {
            if (_spawned.TryGetValue(p, out var go) && go != null) Destroy(go);
            _spawned.Remove(p);
            p.x = position.x;
            p.y = position.y;
            p.z = position.z;
            p.yaw = yaw;
            SaveSystem.Save();
            Spawn(p);
            RefreshBowl();
            AudioManager.Instance?.PlaySfx("SFX/rpg_dropLeather", 0.6f);
        }

        /// <summary>Starts moving a placed item (it follows the mouse until clicked down again).</summary>
        public void StartMoving(PlacedItem p)
        {
            _spawned.TryGetValue(p, out var go);
            PlacementController.Instance?.BeginMove(p, go);
        }

        public void PutAway(PlacedItem p)
        {
            if (!Inventory.HasRoomFor(p.id, 1))
            {
                GameEvents.Toast("Your bag is full: make room before putting this away.", 3f);
                return;
            }
            if (_spawned.TryGetValue(p, out var go)) Destroy(go);
            _spawned.Remove(p);
            SaveSystem.Data.placed.Remove(p);
            Inventory.Add(p.id, 1);
            AudioManager.Instance?.PlaySfx("SFX/rpg_cloth1", 0.5f);
        }

        /// <summary>The object showing this placed item (null if it isn't spawned).</summary>
        public GameObject ObjectFor(PlacedItem p) => _spawned.TryGetValue(p, out var go) ? go : null;

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
