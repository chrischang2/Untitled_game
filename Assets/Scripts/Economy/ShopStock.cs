using System.Collections.Generic;
using System.Linq;
using UntitledGame.Core;
using UntitledGame.Environment;
using UntitledGame.Progression;

namespace UntitledGame.Economy
{
    /// <summary>
    /// Which goods the shops put out. Goods of a later HSK tier are kept as a surprise for the region that goes with
    /// them: something needing the HSK t test first appears once you've reached region t by bus (Willow Bay shows only
    /// tier-0 goods, the desert adds HSK 1 goods...). The last region on the route also brings out goods for tests
    /// beyond it as you pass them. Hidden goods aren't listed, displayed on counters, offered or mentioned by anyone.
    /// </summary>
    public static class ShopStock
    {
        public static bool Revealed(ItemDef item)
        {
            if (item == null) return false;
            int reached = SaveSystem.Data.regionReached;
            if (item.minHsk <= reached) return true;
            return reached >= Regions.Last && Hsk.Level >= item.minHsk;
        }

        public static bool Revealed(string itemId) => Revealed(Catalog.Get(itemId));

        /// <summary>The shop's goods on show (in catalog order).</summary>
        public static IEnumerable<ItemDef> Goods(ShopDef shop) =>
            shop == null ? Enumerable.Empty<ItemDef>() : shop.items.Select(Catalog.Get).Where(Revealed);

        /// <summary>Shows or hides the goods on the stalls' counters.</summary>
        public static void ApplyDisplays()
        {
            foreach (var tag in PriceTag.Known)
                if (tag != null && !string.IsNullOrEmpty(tag.itemId) && Catalog.Get(tag.itemId) != null)
                    tag.gameObject.SetActive(Revealed(tag.itemId));
        }
    }
}
