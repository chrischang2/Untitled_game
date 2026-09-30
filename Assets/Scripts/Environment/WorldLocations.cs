using UnityEngine;
using UntitledGame.Companion;

namespace UntitledGame.Environment
{
    /// <summary>Human-readable place names for the characters' situation notes.</summary>
    public static class WorldLocations
    {
        public static string Describe(Vector3 p)
        {
            foreach (var k in ShopkeeperBrain.Keepers)
            {
                if (Vector3.Distance(k.transform.position, p) < k.ServiceRadius + 1f)
                    return $"at {k.DisplayName}'s {k.Shop.hanzi} [{k.Shop.english.ToLower()}] in the market";
            }
            if (WorldShape.InMarket(p.x, p.z, 1f)) return "at the market";
            if (WorldShape.IsOnDock(p.x, p.z)) return "on the dock";
            if (Vector2.Distance(new Vector2(p.x, p.z), WorldShape.CampCenter) < WorldShape.CampRadius + 3f) return "at the camp by the cabin";
            if (WorldShape.DistanceToPath(p.x, p.z) < 2f) return "on the path";
            return WorldShape.ShoreDistance(p.x, p.z) < 6f ? "on the lake shore" : "in the woods by the lake";
        }
    }
}
