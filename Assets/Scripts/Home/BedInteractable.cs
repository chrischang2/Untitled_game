using UnityEngine;
using UntitledGame.Economy;
using UntitledGame.Environment;
using UntitledGame.Progression;

namespace UntitledGame.Home
{
    /// <summary>The bed in the house. Its model follows the best bed you own; F to sleep (any time).</summary>
    public class BedInteractable : Interactable
    {
        public static BedInteractable Instance { get; private set; }

        private GameObject _model;
        private string _shownBed;

        /// <summary>You wake up standing in the middle of the room, facing the door.</summary>
        public Vector3 WakeSpot
        {
            get
            {
                var cabin = GetComponentInParent<Environment.Cabin>();
                if (cabin == null) return transform.position + Vector3.up * 0.1f;
                Vector3 c = cabin.transform.position;
                return new Vector3(c.x, cabin.FloorY + 0.05f, c.z);
            }
        }

        public float WakeYaw
        {
            get
            {
                var cabin = GetComponentInParent<Environment.Cabin>();
                return cabin != null ? cabin.transform.eulerAngles.y : transform.eulerAngles.y;
            }
        }

        public override string Prompt => $"[F] Sleep until 6am  <size=18>({Energy.Bed?.english}: {Energy.Max:0} energy tomorrow)</size>";

        private void Awake()
        {
            Instance = this;
            radius = 1.8f;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Inventory.Changed += RefreshModel;
            Regions.Changed += RefreshModel;
            RefreshModel();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Inventory.Changed -= RefreshModel;
            Regions.Changed -= RefreshModel;
        }

        public void RefreshModel()
        {
            var bed = Energy.Bed;
            if (bed == null || bed.id == _shownBed) return;
            _shownBed = bed.id;
            if (_model != null) Destroy(_model);
            _model = ItemVisuals.Create(bed);
            _model.transform.SetParent(transform, false);
            _model.transform.localPosition = Vector3.zero;
            _model.transform.localRotation = Quaternion.identity;
        }

        public override void Interact() => SleepSystem.Instance?.TrySleep(WakeSpot, WakeYaw, Energy.Bed?.english ?? "bed");

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
