using System;
using System.Collections.Generic;
using UnityEngine;
using UntitledGame.Core;

namespace UntitledGame.Environment
{
    /// <summary>One place the bus goes.</summary>
    public class RegionDef
    {
        public string id;
        public string english;
        public string hanzi;
        public string blurb;
        /// <summary>The colour the daytime sky and fog drift towards (desert haze), and how far.</summary>
        public Color haze = Color.clear;
        public float hazeAmount;
        /// <summary>Chance of a shower at each weather check (Willow Bay: the Weather default).</summary>
        public float rainChance = 0.22f;
        /// <summary>Showers fall as snow (and it's always snowing a little).</summary>
        public bool snow;
        /// <summary>Sea colours (clear = the water material's own).</summary>
        public Color shallowWater = Color.clear, deepWater = Color.clear;
    }

    /// <summary>
    /// The places on the bus route. Each is the same stretch of coast (dock, cabin, market, seafront, bus stop) dressed
    /// for its region: the scene holds every region's look (terrain colours, plants, stall styles) and RegionStyle
    /// objects switch on only in their own region. You move on by bus after passing that region's HSK test and paying
    /// for a ticket (BusTrip). Each region's sea has its own twenty fish (tier = region: Willow Bay 0 ... Mars 3).
    /// </summary>
    public static class Regions
    {
        public static readonly RegionDef[] All =
        {
            new RegionDef { id = "willowbay", english = "Willow Bay", hanzi = "柳湾", blurb = "a forest beach" },
            new RegionDef { id = "desert", english = "Golden Sand Bay", hanzi = "金沙湾", blurb = "a desert coast",
                haze = new Color(0.93f, 0.78f, 0.55f), hazeAmount = 0.28f, rainChance = 0.04f },
            new RegionDef { id = "snow", english = "Snow Bay", hanzi = "雪湾", blurb = "a snowy coast with ice on the sea",
                haze = new Color(0.86f, 0.91f, 0.97f), hazeAmount = 0.25f, rainChance = 0.3f, snow = true,
                shallowWater = new Color(0.42f, 0.64f, 0.72f), deepWater = new Color(0.1f, 0.22f, 0.33f) },
            new RegionDef { id = "mars", english = "Mars", hanzi = "火星", blurb = "a red planet with a purple sea",
                haze = new Color(0.88f, 0.58f, 0.42f), hazeAmount = 0.38f, rainChance = 0f,
                shallowWater = new Color(0.66f, 0.38f, 0.84f), deepWater = new Color(0.26f, 0.07f, 0.42f) },
        };

        public static int Last => All.Length - 1;
        /// <summary>Editor captures: which region's sky to show outside play mode (-1 = none).</summary>
        public static int PreviewRegion = -1;
        public static int Current => Mathf.Clamp(SaveSystem.Data.location, 0, Last);
        public static RegionDef Here => All[Current];
        public static RegionDef Get(int i) => All[Mathf.Clamp(i, 0, Last)];

        /// <summary>Fired after the player arrives somewhere new (RegionStyle objects switch over).</summary>
        public static event Action Changed;

        /// <summary>The fish tier living in a region's sea (each region has its own twenty fish).</summary>
        public static int FishTier(int region) => Mathf.Clamp(region, 0, 3);
        public static int FishTier() => FishTier(Current);

        /// <summary>The region whose sea a fish tier lives in.</summary>
        public static int HomeOfTier(int tier) => Mathf.Clamp(tier, 0, Last);

        public static void MoveTo(int region)
        {
            region = Mathf.Clamp(region, 0, Last);
            var d = SaveSystem.Data;
            d.location = region;
            d.regionReached = Mathf.Max(d.regionReached, region);
            ChatAudit.Write("WORLD", $"arrived in {All[region].english} ({All[region].hanzi})");
            Apply();
            Changed?.Invoke();
        }

        /// <summary>
        /// Shows the current region's look: every RegionStyle in the scene on or off, everyone's clothes, the sea's colour,
        /// and which goods the shops put out (Economy.ShopStock).
        /// </summary>
        public static void Apply() => Show(Current);

        /// <summary>Dresses the scene as a region (Apply at runtime; editor captures pass any region).</summary>
        public static void Show(int region)
        {
            foreach (var s in UnityEngine.Object.FindObjectsByType<RegionStyle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                s.gameObject.SetActive(s.region == region);
            RegionOutfit.ApplyAll(region);
            TintSea(Get(region));
            if (Application.isPlaying) Economy.ShopStock.ApplyDisplays();
        }

        private static readonly int ShallowId = Shader.PropertyToID("_ShallowColor"), DeepId = Shader.PropertyToID("_DeepColor");

        private static void TintSea(RegionDef r)
        {
            var sea = GameObject.Find("Sea");
            var mr = sea != null ? sea.GetComponent<Renderer>() : null;
            if (mr == null) return;
            if (r.shallowWater.a <= 0f)
            {
                mr.SetPropertyBlock(null);
                return;
            }
            var block = new MaterialPropertyBlock();
            mr.GetPropertyBlock(block);
            block.SetColor(ShallowId, r.shallowWater);
            block.SetColor(DeepId, r.deepWater);
            mr.SetPropertyBlock(block);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Changed = null;
    }
}
