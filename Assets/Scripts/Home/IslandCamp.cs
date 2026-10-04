using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Progression;

namespace UntitledGame.Home
{
    /// <summary>
    /// The camp spot on the island (only reachable with the best boat). Press F to set up camp the first time; after
    /// that, F to sleep in the tent (any time) and wake on the island at 6am - so you can stay out for days, filling
    /// your bag before rowing back to sell. (Passing out still sends you home.)
    /// </summary>
    public class IslandCamp : Interactable
    {
        [SerializeField] private GameObject campVisuals;

        public static IslandCamp Instance { get; private set; }

        public bool IsSetUp => SaveSystem.Data.islandCamp;

        public override string Prompt => !IsSetUp ? "[F] Set up camp here"
            : "[F] Sleep in your tent until 6am";

        public void Configure(GameObject visuals) => campVisuals = visuals;

        private void Awake()
        {
            Instance = this;
            radius = 3.2f;
        }

        private void Start() => Refresh();

        private void Refresh()
        {
            if (campVisuals != null) campVisuals.SetActive(IsSetUp);
        }

        public Vector3 WakeSpot => transform.position + transform.forward * 2f + Vector3.up * 0.1f;

        public override void Interact()
        {
            if (!IsSetUp)
            {
                SaveSystem.Data.islandCamp = true;
                SaveSystem.Save();
                Refresh();
                AudioManager.Instance?.PlaySfx("SFX/rpg_cloth1", 0.6f);
                ChatAudit.Write("WORLD", "set up camp on the island");
                GameEvents.Toast("Camp set up! You can sleep here any time and wake up on the island.", 4.5f);
                return;
            }
            SleepSystem.Instance?.TrySleep(WakeSpot, transform.eulerAngles.y, "the island tent");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
