using System;
using UnityEngine;
using UntitledGame.CameraControl;
using UntitledGame.Core;
using UntitledGame.Fishing;
using UntitledGame.Home;
using UntitledGame.Player;

namespace UntitledGame.Companion
{
    /// <summary>
    /// On the player. Press E next to a shopkeeper to start a conversation: they greet you, the camera
    /// moves in, and V (or typing) goes to them until you press E again or walk away. B always asks Mei
    /// on the side ("what did she say?", "how do I say...?"); the shopkeeper waits for you meanwhile.
    /// </summary>
    public class ShopConversation : MonoBehaviour
    {
        [SerializeField] private KeyCode key = KeyCode.E;
        [SerializeField] private float leaveDistanceFactor = 1.35f;

        public static ShopConversation Instance { get; private set; }

        /// <summary>The shopkeeper the player is talking with, or null.</summary>
        public static ShopkeeperBrain Active => Instance != null ? Instance._active : null;

        /// <summary>The keeper E would start talking to right now (close and roughly in front of the player).</summary>
        public ShopkeeperBrain Candidate { get; private set; }

        /// <summary>Index into <see cref="ConversationLog.Entries"/> where the current conversation began.</summary>
        public static int StartedAtLogIndex { get; private set; }

        public static event Action<ShopkeeperBrain> Started;
        public static event Action<ShopkeeperBrain> Ended;

        private ShopkeeperBrain _active;
        private CameraRig _rig;
        private PlayerController _player;
        private FishingController _fishing;

        private void Awake()
        {
            Instance = this;
            _rig = FindFirstObjectByType<CameraRig>();
            _player = GetComponent<PlayerController>();
            _fishing = GetComponent<FishingController>();
        }

        private void OnDisable()
        {
            if (_active != null) End(sayGoodbye: false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            bool fishing = _fishing != null && _fishing.State != FishingState.Idle;
            Candidate = _active == null && !fishing && !PlacementController.Active ? ShopkeeperBrain.Facing(transform) : null;

            if (_active != null)
            {
                // Walked off (or the keeper vanished): the conversation is over.
                if (!_active.isActiveAndEnabled || _active.DistanceToPlayer > _active.ServiceRadius * leaveDistanceFactor)
                {
                    End(sayGoodbye: _active.isActiveAndEnabled);
                    return;
                }
                // The stalls are close together: walking up to the next keeper leaves this one.
                var other = ShopkeeperBrain.Facing(transform);
                if (other != null && other != _active && other.DistanceToPlayer + 0.75f < _active.DistanceToPlayer)
                {
                    End(sayGoodbye: true);
                    return;
                }
                // They said goodbye and the keeper has answered.
                if (_active.ConversationOver && !_active.IsBusy)
                {
                    End(sayGoodbye: false);
                    return;
                }
                if (!_player.IsMoving) _player.FaceTowards(_active.transform.position);
            }

            if (InputGate.GameplayBlocked || !Input.GetKeyDown(key)) return;
            if (_active != null) End(sayGoodbye: true);
            else if (Candidate != null) Begin(Candidate);
        }

        public void Begin(ShopkeeperBrain keeper)
        {
            if (keeper == null || keeper == _active) return;
            if (_active != null) End(sayGoodbye: false);
            _active = keeper;
            StartedAtLogIndex = ConversationLog.Entries.Count;
            var mei = CompanionBrain.Current;
            if (mei != null && mei.IsBusy) mei.Interrupt();
            ChatAudit.Write("CONVERSATION", $"started talking with {keeper.DisplayName} ({keeper.DisplayNameEnglish}) - E");
            if (_rig != null)
            {
                _rig.DistanceOverride = 5.2f;
                Vector3 mid = (keeper.transform.position - transform.position) * 0.5f;
                mid.y = 0f;
                _rig.FocusOffset = mid;
            }
            _player?.FaceTowards(keeper.transform.position, instant: true);
            keeper.BeginConversation();
            Started?.Invoke(keeper);
        }

        public void End(bool sayGoodbye)
        {
            var keeper = _active;
            if (keeper == null) return;
            _active = null;
            ChatAudit.Write("CONVERSATION", $"stopped talking with {keeper.DisplayName} ({keeper.DisplayNameEnglish})");
            if (_rig != null)
            {
                _rig.DistanceOverride = null;
                _rig.FocusOffset = Vector3.zero;
            }
            keeper.EndConversation(sayGoodbye);
            Ended?.Invoke(keeper);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            Started = null;
            Ended = null;
            StartedAtLogIndex = 0;
        }
    }
}
