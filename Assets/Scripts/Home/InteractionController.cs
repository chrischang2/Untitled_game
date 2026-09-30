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
            Current = PlacementController.Active ? null : Interactable.Nearest(transform.position);
            if (Current != null && !InputGate.GameplayBlocked && Input.GetKeyDown(key)) Current.Interact();
        }
    }
}
