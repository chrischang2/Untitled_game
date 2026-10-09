using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Environment;
using UntitledGame.Fishing;
using UntitledGame.Progression;

namespace UntitledGame.Economy
{
    /// <summary>
    /// The bus at the bus stop (汽车站), and its driver 张师傅. The bus is always there, but it only runs (and the driver
    /// only turns up) once the player has passed their first HSK test. To leave a region for the next one you need that
    /// region's HSK test (the first test leaves Willow Bay, the second the desert...) and a ticket: the sale price of
    /// ten of the region's most valuable fish at their biggest (Willow Bay: ten top-size hairtail, ¥720). A leg you've
    /// paid for stays paid, and going back to a region you've already been to is free.
    /// </summary>
    public static class BusTrip
    {
        public const int FareFishCount = 10;

        private static SaveData D => SaveSystem.Data;

        /// <summary>The driver is here and the bus runs once the first HSK test is passed.</summary>
        public static bool Running => Hsk.Level >= 1;

        /// <summary>The next stop on the route, or -1 at the end of the line (the road on isn't built yet).</summary>
        public static int Next => Regions.Current < Regions.Last ? Regions.Current + 1 : -1;
        /// <summary>The previous stop (always free), or -1 at the start of the route.</summary>
        public static int Previous => Regions.Current > 0 ? Regions.Current - 1 : -1;

        /// <summary>The HSK test you need to ride on from a region.</summary>
        public static int HskToLeave(int region) => region + 1;

        /// <summary>The most valuable fish of a region's own tier (by Auntie Chen's price at its biggest).</summary>
        public static FishSpecies FareFish(int region) =>
            FishDatabase.All.Where(f => f.IsFish && FishPower.TierOf(f) == region).OrderByDescending(f => Catalog.FishPrice(f, f.maxWeight)).FirstOrDefault();

        /// <summary>The ticket out of a region: what ten of its best fish at their biggest would sell for.</summary>
        public static int Fare(int region)
        {
            var f = FareFish(region);
            return f == null ? 0 : FareFishCount * Catalog.FishPrice(f, f.maxWeight);
        }

        public static int Fare() => Fare(Regions.Current);

        /// <summary>The next leg needs no ticket: you've been there before (a ticket is bought as you get on).</summary>
        public static bool NextPaid => Next >= 0 && D.regionReached >= Next;

        /// <summary>Why the bus can't take you on to the next stop now, or null if it can. ("closed", "end", "hsk", "money")</summary>
        public static string CannotGoOn()
        {
            if (!Running) return "closed";
            if (Next < 0) return "end";
            if (Hsk.Level < HskToLeave(Regions.Current)) return "hsk";
            if (!NextPaid && Inventory.Money < Fare()) return "money";
            return null;
        }

        /// <summary>Pays for the next leg if it isn't paid yet. False if there isn't enough money.</summary>
        public static bool BuyTicket()
        {
            if (NextPaid) return true;
            int fare = Fare();
            if (!Inventory.Spend(fare)) return false;
            ChatAudit.Write("BUS", $"bought a ticket to {Regions.Get(Next).english} for ¥{fare}; player now has ¥{Inventory.Money}");
            return true;
        }

        /// <summary>Takes the player to a region (the BusStop runs the ride itself).</summary>
        public static void Arrive(int region) => Regions.MoveTo(region);

        /// <summary>"ten top-size hairtail".</summary>
        public static string FareFishEnglish(int region)
        {
            var f = FareFish(region);
            return f == null ? "" : $"{FareFishCount} top-size {f.name.ToLower()}";
        }

        public static string Describe()
        {
            if (!Running) return "The bus starts running once you pass the HSK 1 test.";
            if (Next < 0) return $"{Regions.Here.english} is the end of the line for now: the road further on isn't built yet.";
            var to = Regions.Get(Next);
            if (NextPaid) return $"Ride on to {to.english} ({to.hanzi}) any time: you've paid this leg before.";
            string hsk = Hsk.Level < HskToLeave(Regions.Current) ? $" You also need the HSK {HskToLeave(Regions.Current)} test." : "";
            return $"Ticket to {to.english} ({to.hanzi}): ¥{Fare()} (what {FareFishEnglish(Regions.Current)} would sell for). You have ¥{Inventory.Money}.{hsk}";
        }
    }
}
