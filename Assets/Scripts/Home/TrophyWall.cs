using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Environment;
using UntitledGame.Fishing;

namespace UntitledGame.Home
{
    /// <summary>
    /// A wooden board on the back wall of the house with a mounted fish for every gold medal and every kind of fish
    /// caught golden. It grows as you fish; F there opens the journal.
    /// </summary>
    public class TrophyWall : Interactable
    {
        public static TrophyWall Instance { get; private set; }

        private const int PerRow = 4;
        private const int Rows = 3;
        private const float MountLength = 0.36f;
        private readonly List<GameObject> _mounts = new List<GameObject>();
        private Cabin _cabin;

        /// <summary>Creates the wall in the player's house (once).</summary>
        public static void Ensure()
        {
            if (Instance != null) return;
            var cabin = Cabin.PlayerHouse;
            if (cabin == null) return;
            var go = new GameObject("TrophyWall");
            var wall = go.AddComponent<TrophyWall>();
            wall._cabin = cabin;
            wall.radius = 1.8f;
            // The right-hand part of the back wall (clear of the window), out from the logs, at about chest height.
            go.transform.SetPositionAndRotation(cabin.transform.TransformPoint(new Vector3(0.95f, Cabin.FloorTop + 0.6f, -Cabin.HalfDepth + 0.16f)), cabin.transform.rotation);
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(board.GetComponent<Collider>());
            board.name = "Board";
            board.transform.SetParent(go.transform, false);
            board.transform.localPosition = new Vector3(0f, 0f, -0.03f);
            board.transform.localScale = new Vector3(PerRow * (MountLength + 0.08f) + 0.1f, Rows * 0.4f + 0.1f, 0.04f);
            var r = board.GetComponent<Renderer>();
            r.sharedMaterial = GameAssets.Instance.bobberWhite; // a URP-lit material that ships with the build
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", new Color(0.55f, 0.37f, 0.22f));
            r.SetPropertyBlock(mpb);
            wall.Rebuild();
        }

        public override string Prompt
        {
            get
            {
                int gold = CatchJournal.MedalCount(3), golden = SaveSystem.Data.journal.Count(j => j.goldenCount > 0);
                return gold + golden == 0
                    ? "Your trophy wall  <size=18>(gold medals and golden fish go here: F for the journal)</size>"
                    : $"[F] Trophy wall: {gold} gold medal{(gold == 1 ? "" : "s")}, {golden} golden fish  <size=18>(journal)</size>";
            }
        }

        public override void Interact() => FindFirstObjectByType<UI.GameUI>()?.ToggleJournal(false);

        private void Awake() => Instance = this;

        protected override void OnEnable()
        {
            base.OnEnable();
            CatchJournal.Recorded += OnCatch;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            CatchJournal.Recorded -= OnCatch;
        }

        private void OnCatch(CatchResult r)
        {
            if (r.newMedal == 3 || r.golden) Rebuild();
        }

        /// <summary>What hangs on the wall: (species, golden) for each gold medal and each kind caught golden.</summary>
        public static List<(FishSpecies species, bool golden)> Trophies()
        {
            var list = new List<(FishSpecies, bool)>();
            foreach (var rec in SaveSystem.Data.journal)
            {
                var s = FishDatabase.Get(rec.speciesId);
                if (s == null || !s.IsFish) continue;
                if (rec.medal >= 3) list.Add((s, false));
                if (rec.goldenCount > 0) list.Add((s, true));
            }
            return list;
        }

        public int MountCount => _mounts.Count;

        public void Rebuild()
        {
            foreach (var m in _mounts) if (m != null) Destroy(m);
            _mounts.Clear();
            var trophies = Trophies().Take(PerRow * Rows).ToList();
            for (int i = 0; i < trophies.Count; i++)
            {
                var (s, golden) = trophies[i];
                var fish = CatchVisuals.Spawn(s, s.maxWeight, golden);
                foreach (var l in fish.GetComponentsInChildren<Light>()) l.range = 1.2f;
                // Shrink to a plaque-sized fish and hang it side-on, facing into the room.
                var rs = fish.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    Bounds b = rs[0].bounds;
                    foreach (var rr in rs) b.Encapsulate(rr.bounds);
                    fish.transform.localScale = Vector3.one * (MountLength / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.y, b.size.z)));
                }
                int row = i / PerRow, col = i % PerRow;
                float x = (col - (PerRow - 1) * 0.5f) * (MountLength + 0.08f);
                fish.transform.SetParent(transform, true);
                fish.transform.localPosition = new Vector3(x, (Rows - 1) * 0.2f - row * 0.4f, 0.06f);
                fish.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                _mounts.Add(fish);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
    }
}
