using UnityEngine;
using UntitledGame.Core;

namespace UntitledGame.Home
{
    /// <summary>On the player: finds the nearest <see cref="Interactable"/> and uses it on F.</summary>
    public class InteractionController : MonoBehaviour
    {
        [SerializeField] private KeyCode key = KeyCode.F;

        public Interactable Current { get; private set; }

        private void Update()
        {
            // In the boat, F means "get out" (handled by the boat).
            Current = PlacementController.Active || Fishing.Rowboat.PlayerAboard ? null : Interactable.Nearest(transform.position);
            if (Current != null && !InputGate.GameplayBlocked && Input.GetKeyDown(key)) Current.Interact();
        }
    }
}
