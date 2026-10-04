using System;
using System.Collections.Generic;
using System.Linq;
using UntitledGame.Core;
using UntitledGame.Fishing;

namespace UntitledGame.Economy
{
    public enum BuyResult { Ok, NotEnoughMoney, AlreadyOwned, Unknown, NoRoom }

    /// <summary>One bag slot: a stack of one kind of item, or of one kind of fish.</summary>
    public class BagSlot
    {
        public string itemId;      // or null for fish
        public string speciesId;   // fish
        public int count;
        public int value;          // what it's worth (shop price or what Auntie Chen would pay)
        public string label;
    }

    /// <summary>
    /// Money, owned items, the fish bucket and equipment (all persisted in the save file).
    /// Only fish go in the bag: up to 20 fish of one kind per slot. You start with 4 slots; Old Wang's buckets give 8, 12
    /// and 16. Every other item is a key item (bait, gifts, books, furniture...) and never takes bag space.
    /// </summary>
    public static class Inventory
    {
        public static event Action Changed;

        private static SaveData D => SaveSystem.Data;

        public static int Money => D.money;

        private static void Notify()
        {
            SaveSystem.Save();
            Changed?.Invoke();
        }

        public static void Earn(int amount)
        {
            D.money += Math.Max(0, amount);
            Notify();
        }

        // ------------------------------------------------------------------ items

        public static int Count(string id) => D.items.FirstOrDefault(i => i.id == id)?.count ?? 0;

        public static bool Owns(string id) =>
            id == Catalog.StarterRod || id == "bed_mat" || Count(id) > 0 || (id == "collar" && D.pet.collar) || D.placed.Any(p => p.id == id);

        public static void Add(string id, int count)
        {
            var stack = D.items.FirstOrDefault(i => i.id == id);
            if (stack == null) D.items.Add(stack = new ItemStack { id = id, count = 0 });
            stack.count += count;
            Notify();
        }

        public static bool Remove(string id, int count = 1)
        {
            var stack = D.items.FirstOrDefault(i => i.id == id);
            if (stack == null || stack.count < count) return false;
            stack.count -= count;
            if (stack.count <= 0) D.items.Remove(stack);
            if (D.selectedBait == id && stack.count <= 0) D.selectedBait = "";
            Notify();
            return true;
        }

        public static IEnumerable<(ItemDef def, int count)> Owned() =>
            D.items.Select(s => (Catalog.Get(s.id), s.count)).Where(x => x.Item1 != null);

        public static bool CanBuy(string itemId, int quantity, out BuyResult reason, out int cost)
        {
            var def = Catalog.Get(itemId);
            cost = 0;
            reason = BuyResult.Ok;
            if (def == null) { reason = BuyResult.Unknown; return false; }
            if (def.unique) quantity = 1;
            cost = def.price * Math.Max(1, quantity);
            if (def.unique && Owns(itemId)) { reason = BuyResult.AlreadyOwned; return false; }
            if (def.category == ItemCategory.Upgrade)
            {
                // One level at a time, at the current level's price; nothing past the maximum.
                if (UntitledGame.Progression.PlayerStats.Level(def.stat) >= UntitledGame.Progression.PlayerStats.MaxLevel) { reason = BuyResult.AlreadyOwned; return false; }
                cost = Catalog.PriceOf(def);
            }
            if (D.money < cost) { reason = BuyResult.NotEnoughMoney; return false; }
            if (def.TakesSlot && !HasRoomFor(itemId, Math.Max(1, quantity) * def.packSize)) { reason = BuyResult.NoRoom; return false; }
            return true;
        }

        public static BuyResult Buy(string itemId, int quantity)
        {
            if (!CanBuy(itemId, quantity, out var reason, out int cost)) return reason;
            var def = Catalog.Get(itemId);
            if (def.unique) quantity = 1;
            D.money -= cost;
            if (def.category == ItemCategory.Upgrade) UntitledGame.Progression.PlayerStats.Upgrade(def.stat);
            else if (def.category == ItemCategory.Wearable && itemId == "collar") D.pet.collar = true;
            else Add(itemId, quantity * def.packSize);
            if (def.category == ItemCategory.Rod) D.equippedRod = itemId;                     // new rods are equipped right away
            if (def.category == ItemCategory.Bait && string.IsNullOrEmpty(D.selectedBait)) D.selectedBait = itemId;
            Notify();
            return BuyResult.Ok;
        }

        // ------------------------------------------------------------------ fishing gear

        public static ItemDef Rod => Catalog.Get(D.equippedRod) ?? Catalog.Get(Catalog.StarterRod);

        public static void EquipRod(string id)
        {
            if (!Owns(id)) return;
            D.equippedRod = id;
            Notify();
        }

        public static IEnumerable<ItemDef> OwnedRods() =>
            Catalog.Items.Where(i => i.category == ItemCategory.Rod && Owns(i.id));

        /// <summary>Currently selected bait if any is left.</summary>
        public static ItemDef Bait => string.IsNullOrEmpty(D.selectedBait) || Count(D.selectedBait) <= 0 ? null : Catalog.Get(D.selectedBait);

        public static void SelectBait(string id)
        {
            D.selectedBait = id ?? "";
            Notify();
        }

        public static void ConsumeBait()
        {
            var b = Bait;
            if (b != null) Remove(b.id, 1);
        }

        public static bool HasAccessory(string id) => Count(id) > 0;

        /// <summary>The strongest line you own (everyone starts with a 3 kg white line).</summary>
        public static float LineKg => Owned().Where(x => x.def.category == ItemCategory.Line).Select(x => x.def.lineKg)
            .DefaultIfEmpty(Catalog.StarterLineKg).Max();

        public static string LineName => Owned().Where(x => x.def.category == ItemCategory.Line).OrderByDescending(x => x.def.lineKg)
            .Select(x => x.def.english).FirstOrDefault() ?? "White Line (3 kg)";

        public static string LineHanzi => Owned().Where(x => x.def.category == ItemCategory.Line).OrderByDescending(x => x.def.lineKg)
            .Select(x => x.def.hanzi).FirstOrDefault() ?? "白线";

        // ------------------------------------------------------------------ bag slots

        public const int StartingSlots = 4;
        public const int ItemStack = 99;
        public const int FishStack = 20;

        public static int SlotCapacity => Owned().Where(x => x.def.bagSlots > 0).Select(x => x.def.bagSlots).DefaultIfEmpty(StartingSlots).Max();

        private static bool InBag(ItemDef def) => def != null && def.TakesSlot && !(def.category == ItemCategory.Book && UntitledGame.Progression.PlayerStats.HasRead(def.id));

        private static int SlotsFor(int count, int stack) => count <= 0 ? 0 : (count + stack - 1) / stack;

        /// <summary>What's in the bag, slot by slot.</summary>
        public static List<BagSlot> Slots()
        {
            var slots = new List<BagSlot>();
            foreach (var s in D.items)
            {
                var def = Catalog.Get(s.id);
                if (!InBag(def)) continue;
                int left = s.count;
                float unit = def.packSize > 1 ? Catalog.PriceOf(def) / (float)def.packSize : Catalog.PriceOf(def);
                while (left > 0)
                {
                    int n = Math.Min(ItemStack, left);
                    slots.Add(new BagSlot { itemId = s.id, count = n, value = (int)Math.Round(unit * n), label = def.english });
                    left -= n;
                }
            }
            foreach (var g in D.bucket.Where(b => FishDatabase.Get(b.speciesId) != null).GroupBy(b => b.speciesId))
            {
                var fish = g.ToList();
                var species = FishDatabase.Get(g.Key);
                for (int i = 0; i < fish.Count; i += FishStack)
                {
                    var part = fish.Skip(i).Take(FishStack).ToList();
                    slots.Add(new BagSlot { speciesId = g.Key, count = part.Count, value = part.Sum(b => Catalog.FishPrice(species, b.length)), label = species.name });
                }
            }
            return slots;
        }

        public static int SlotsUsed => Slots().Count;
        public static int SlotsFree => Math.Max(0, SlotCapacity - SlotsUsed);

        /// <summary>Would adding this many of an item still fit in the bag?</summary>
        public static bool HasRoomFor(string itemId, int count)
        {
            var def = Catalog.Get(itemId);
            if (!InBag(def)) return true;
            int have = Count(itemId);
            int extra = SlotsFor(have + count, ItemStack) - SlotsFor(have, ItemStack);
            return extra <= 0 || SlotsUsed + extra <= SlotCapacity;
        }

        public static bool HasRoomForFish(FishSpecies species)
        {
            int have = D.bucket.Count(b => b.speciesId == species.id);
            int extra = SlotsFor(have + 1, FishStack) - SlotsFor(have, FishStack);
            return extra <= 0 || SlotsUsed + extra <= SlotCapacity;
        }

        /// <summary>
        /// Passing out: everything in the bag is lost except the most valuable slots. Returns what was lost.
        /// (Money, equipment, placed furniture and things at home are safe.)
        /// </summary>
        public static List<BagSlot> LoseAllBut(int keep)
        {
            var slots = Slots().OrderByDescending(s => s.value).ToList();
            var lost = slots.Skip(keep).ToList();
            foreach (var s in lost)
            {
                if (s.itemId != null)
                {
                    var stack = D.items.FirstOrDefault(i => i.id == s.itemId);
                    if (stack == null) continue;
                    stack.count -= s.count;
                    if (stack.count <= 0)
                    {
                        D.items.Remove(stack);
                        if (D.selectedBait == s.itemId) D.selectedBait = "";
                    }
                }
                else
                {
                    // The lightest (cheapest) fish of that kind go first.
                    var fish = D.bucket.Where(b => b.speciesId == s.speciesId).OrderBy(b => b.length).Take(s.count).ToList();
                    foreach (var f in fish) D.bucket.Remove(f);
                }
            }
            Notify();
            return lost;
        }

        // ------------------------------------------------------------------ bucket (the fish in the bag)

        public static int BucketCapacity => SlotCapacity;
        public static int BucketCount => D.bucket.Count;
        /// <summary>No free slot left (a fish of a kind you already carry may still fit in its stack).</summary>
        public static bool BucketFull => SlotsUsed >= SlotCapacity;

        public static IEnumerable<(FishSpecies species, float length)> Bucket =>
            D.bucket.Select(b => (FishDatabase.Get(b.speciesId), b.length)).Where(x => x.Item1 != null);

        public static bool AddToBucket(FishSpecies species, float length)
        {
            if (!HasRoomForFish(species)) return false;
            D.bucket.Add(new BucketFish { speciesId = species.id, length = length });
            Notify();
            return true;
        }

        public static int BucketValue => Bucket.Sum(b => Catalog.FishPrice(b.species, b.length));

        /// <summary>Sells every fish in the bucket; returns the money earned.</summary>
        public static int SellAllFish()
        {
            int total = BucketValue;
            D.bucket.Clear();
            D.money += total;
            Notify();
            return total;
        }

        /// <summary>Sells only fish of one species; returns (count, money).</summary>
        public static (int count, int money) SellSpecies(string speciesId)
        {
            var sold = D.bucket.Where(b => b.speciesId == speciesId).ToList();
            int total = sold.Sum(b => Catalog.FishPrice(FishDatabase.Get(b.speciesId), b.length));
            foreach (var b in sold) D.bucket.Remove(b);
            D.money += total;
            Notify();
            return (sold.Count, total);
        }

        /// <summary>"3条鲤鱼, 1条鲫鱼" - how a local would describe the bucket.</summary>
        public static string DescribeBucketChinese()
        {
            var groups = Bucket.GroupBy(b => b.species.id).Select(g => $"{g.Count()}{(g.First().species.IsFish ? "条" : "个")}{g.First().species.hanzi}");
            return string.Join("，", groups);
        }

        public static string DescribeBucketEnglish()
        {
            var groups = Bucket.GroupBy(b => b.species.id).Select(g => $"{g.Count()} {g.First().species.name}");
            return string.Join(", ", groups);
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Changed = null;
    }
}
