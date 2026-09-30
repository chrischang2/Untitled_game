using UntitledGame.Core;
using UntitledGame.Economy;

namespace UntitledGame.Home
{
    /// <summary>Tangyuan's food bowl: F fills it with cat food.</summary>
    public class BowlInteractable : Interactable
    {
        public override string Prompt => SaveSystem.Data.pet.bowlFilled
            ? "Tangyuan's bowl is full"
            : Inventory.Count("cat_food") > 0 ? "[F] Fill Tangyuan's bowl" : "Bowl is empty (buy cat food at the pet shop)";

        public override bool Available => true;

        public override void Interact()
        {
            var pet = SaveSystem.Data.pet;
            if (pet.bowlFilled || !Inventory.Remove("cat_food", 1)) return;
            pet.bowlFilled = true;
            SaveSystem.Save();
            HomeItems.Instance?.RefreshBowl();
            AudioManager.Instance?.PlaySfx("SFX/rpg_cloth2", 0.6f);
            GameEvents.Toast("You filled Tangyuan's bowl.");
        }
    }
}
