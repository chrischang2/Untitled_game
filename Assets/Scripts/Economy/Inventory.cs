using System;
using System.Collections.Generic;
using System.Linq;
using UntitledGame.Core;
using UntitledGame.Fishing;

namespace UntitledGame.Economy
{
    public enum BuyResult { Ok, NotEnoughMoney, AlreadyOwned, Unknown }

    /// <summary>Money, owned items, the fish bucket and equipment (all persisted in the save file).</summary>
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
            id == Catalog.StarterRod || Count(id) > 0 || (id == "collar" && D.pet.collar) || D.placed.Any(p => p.id == id);

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
            if (D.money < cost) { reason = BuyResult.NotEnoughMoney; return false; }
            return true;
        }

        public static BuyResult Buy(string itemId, int quantity)
        {
            if (!CanBuy(itemId, quantity, out var reason, out int cost)) return reason;
            var def = Catalog.Get(itemId);
            if (def.unique) quantity = 1;
            D.money -= cost;
            if (def.category == ItemCategory.Wearable && itemId == "collar") D.pet.collar = true;
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

        // ------------------------------------------------------------------ bucket

        public static int BucketCapacity => HasAccessory("bucket_big") ? 16 : 8;
        public static int BucketCount => D.bucket.Count;
        public static bool BucketFull => D.bucket.Count >= BucketCapacity;

        public static IEnumerable<(FishSpecies species, float length)> Bucket =>
            D.bucket.Select(b => (FishDatabase.Get(b.speciesId), b.length)).Where(x => x.Item1 != null);

        public static bool AddToBucket(FishSpecies species, float length)
        {
            if (BucketFull) return false;
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
