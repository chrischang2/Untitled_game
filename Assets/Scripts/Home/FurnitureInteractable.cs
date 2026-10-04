using UntitledGame.Core;
using UntitledGame.Economy;

namespace UntitledGame.Home
{
    /// <summary>Placed furniture: F picks it up so it can be put somewhere else (click to put down, Esc to leave it).</summary>
    public class FurnitureInteractable : Interactable
    {
        private PlacedItem _placed;
        private ItemDef _def;

        public void Configure(PlacedItem placed, ItemDef def)
        {
            _placed = placed;
            _def = def;
            radius = 1.4f; // closer than the bed or the cat, so it doesn't get in their way
        }

        public override string Prompt => $"[F] Move the {_def?.english.ToLower()}";

        public override bool Available => _placed != null && !PlacementController.Active;

        public override void Interact()
        {
            HomeItems.Instance?.StartMoving(_placed);
            GameEvents.Toast("Moving: click to put it down, R or scroll to turn it, Esc to leave it where it was.", 3.5f);
        }
    }
}
