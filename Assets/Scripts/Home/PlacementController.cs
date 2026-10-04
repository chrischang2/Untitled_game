using UnityEngine;
using UnityEngine.EventSystems;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Player;

namespace UntitledGame.Home
{
    /// <summary>
    /// Place a bought item around the camp: a preview follows the mouse, a ring shows whether the spot
    /// is OK (green) or blocked (red). LMB places, R / scroll rotates, RMB / Esc cancels.
    /// </summary>
    public class PlacementController : MonoBehaviour
    {
        public static PlacementController Instance { get; private set; }
        public static bool Active => (Instance != null && Instance._itemId != null) || Time.frameCount - LastActiveFrame <= 1;
        private static int LastActiveFrame = -10;

        private string _itemId;
        private GameObject _ghost;
        private Transform _ring;
        private MaterialPropertyBlock _mpb;
        private float _yaw;
        private bool _valid;
        private int _startedFrame;

        public string ItemEnglish => Catalog.Get(_itemId)?.english;

        private void Awake() => Instance = this;

        private PlacedItem _moving;       // moving something already placed (null: placing from the bag)
        private GameObject _movingObject; // its object, hidden while it's being moved

        /// <summary>Picks up something already placed and lets the player put it somewhere else (Esc puts it back).</summary>
        public void BeginMove(PlacedItem placed, GameObject existing)
        {
            if (placed == null) return;
            Begin(placed.id, fromBag: false);
            if (_itemId == null) return;
            _moving = placed;
            _movingObject = existing;
            _yaw = placed.yaw;
            if (existing != null) existing.SetActive(false);
        }

        public void Begin(string itemId) => Begin(itemId, fromBag: true);

        private void Begin(string itemId, bool fromBag)
        {
            Cancel();
            var def = Catalog.Get(itemId);
            if (def == null || !def.placeable || (fromBag && Inventory.Count(itemId) <= 0)) return;
            _itemId = itemId;
            _startedFrame = Time.frameCount;
            _ghost = ItemVisuals.Create(def);
            foreach (var l in _ghost.GetComponentsInChildren<Light>()) l.enabled = false;

            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "PlacementRing";
            ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = Vector3.one * 1.6f;
            ring.GetComponent<Renderer>().sharedMaterial = GameAssets.Instance.rippleMaterial;
            _ring = ring.transform;
            _mpb = new MaterialPropertyBlock();
        }

        public void Cancel()
        {
            if (_movingObject != null) _movingObject.SetActive(true); // moving was cancelled: it stays where it was
            _moving = null;
            _movingObject = null;
            if (_ghost != null) Destroy(_ghost);
            if (_ring != null) Destroy(_ring.gameObject);
            _ghost = null;
            _ring = null;
            _itemId = null;
        }

        private void Update()
        {
            if (_itemId == null) return;
            LastActiveFrame = Time.frameCount;
            var cam = Camera.main;
            if (cam == null) return;

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                Cancel();
                return;
            }
            if (Input.GetKeyDown(KeyCode.R)) _yaw += 45f;
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f) _yaw += Mathf.Sign(scroll) * 15f;

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore) ||
                (_ghost != null && hit.transform.IsChildOf(_ghost.transform)))
            {
                _valid = false;
                return;
            }
            Vector3 p = hit.point;
            _ghost.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, _yaw, 0f));
            _ring.position = p + Vector3.up * 0.05f;

            bool flatGround = hit.normal.y > 0.8f;
            bool walkable = PlayerController.IsWalkable(p);
            bool home = HomeItems.InHomeArea(p);
            bool clear = true;
            foreach (var c in Physics.OverlapSphere(p + Vector3.up * 0.4f, 0.35f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c is MeshCollider || c is CharacterController) continue; // terrain / player
                if (c.transform.IsChildOf(_ghost.transform)) continue;
                if (c.GetComponent<Terrain>() != null) continue;
                if (c.name == "Dock") continue;
                clear = false;
                break;
            }
            _valid = flatGround && walkable && home && clear;
            _mpb.SetColor("_BaseColor", _valid ? new Color(0.4f, 1f, 0.5f, 0.9f) : new Color(1f, 0.35f, 0.3f, 0.9f));
            _ring.GetComponent<Renderer>().SetPropertyBlock(_mpb);

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (Input.GetMouseButtonDown(0) && !overUi && Time.frameCount > _startedFrame + 1)
            {
                if (_valid)
                {
                    string id = _itemId;
                    var moving = _moving;
                    _movingObject = null; // HomeItems replaces it
                    Cancel();
                    if (moving != null) HomeItems.Instance?.Move(moving, p, _yaw);
                    else HomeItems.Instance?.Place(id, p, _yaw);
                }
                else
                {
                    GameEvents.Toast(!home ? "Place things around your camp." : "Can't put it there.");
                    AudioManager.Instance?.PlaySfx("SFX/ui_error_004", 0.4f);
                }
            }
        }
    }
}
