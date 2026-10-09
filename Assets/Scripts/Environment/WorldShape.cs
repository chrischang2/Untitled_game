using UnityEngine;

namespace UntitledGame.Environment
{
    /// <summary>
    /// Deterministic description of the seaside world (every region on the bus route shares it: Regions): a beach running east-west with the sea to the
    /// north, getting deeper the further out you go. Used by the editor world generator to build the terrain mesh
    /// and at runtime for cheap "is this water / how deep / where's the ground / how far from shore" queries.
    /// </summary>
    public static class WorldShape
    {
        public const float WaterLevel = 0f;
        public const float TerrainSize = 360f; // big enough that its edge stays out of sight from the boat
        public const float PlayableRadius = 82f;
        /// <summary>The shoreline runs east-west at about this z; the sea is north of it (bigger z).</summary>
        public const float CoastZ = -28f;

        /// <summary>The dock heads north from the beach out to sea (DockDirection points inland, south).</summary>
        public const float DockAngleDeg = -90f;
        public const float DockLength = 7f; // short on purpose: deep water (big fish) needs the boat
        public const float DockWidth = 2.6f;
        public const float DockDeckHeight = 0.55f;

        /// <summary>A point well out at sea (straight out from the dock), e.g. for rowing tests.</summary>
        public static Vector2 OpenSea => new Vector2(0f, CoastZ + 60f);

        public static Vector2 DockDirection => new Vector2(Mathf.Cos(DockAngleDeg * Mathf.Deg2Rad), Mathf.Sin(DockAngleDeg * Mathf.Deg2Rad));

        /// <summary>Where the dock meets the shoreline.</summary>
        public static Vector2 DockShorePoint => new Vector2(0f, ShoreZ(0f));

        /// <summary>Centre of the little camp (cabin, campfire) behind the dock.</summary>
        public static Vector2 CampCenter => DockShorePoint + DockDirection * 13f;

        public const float CampRadius = 10f;
        public const float CampHeight = 0.9f;

        /// <summary>The cabin faces the lake: its front (door) points along this direction.</summary>
        public static Vector2 CabinForward => -DockDirection;
        /// <summary>The cabin's right-hand side (Unity's Cross(up, forward)).</summary>
        public static Vector2 CabinRight => new Vector2(CabinForward.y, -CabinForward.x);
        public static Vector2 CabinCenter => CampCenter - CabinForward * 5.2f - CabinRight * 1.0f;
        public const float CabinScale = 2.4f;
        /// <summary>Half size of the cabin's footprint in metres including the walls (x along CabinRight, y along CabinForward).</summary>
        public static readonly Vector2 CabinHalfSize = new Vector2(1.5f * CabinScale + 0.4f, 1.0f * CabinScale + 0.4f);

        /// <summary>How far (in metres) a point is outside the cabin's footprint; 0 inside.</summary>
        public static float DistanceOutsideCabin(float x, float z)
        {
            Vector2 p = new Vector2(x, z) - CabinCenter;
            float lx = Mathf.Abs(Vector2.Dot(p, CabinRight)) - CabinHalfSize.x;
            float lz = Mathf.Abs(Vector2.Dot(p, CabinForward)) - CabinHalfSize.y;
            return new Vector2(Mathf.Max(lx, 0f), Mathf.Max(lz, 0f)).magnitude;
        }

        /// <summary>The little market with the four shops, east of the camp.</summary>
        public static Vector2 MarketCenter => CampCenter + new Vector2(24f, 3f);
        public const float MarketRadius = 11f;
        public const float MarketHeight = 1.0f;
        public const float StallRing = 7.6f;
        // Nine stalls 37.5 degrees apart around the plaza, leaving the west side open as the entrance. In shop order:
        // tackle, fish, furniture, pet, books, gym, colours, gifts, and the test centre straight across from the entrance.
        private static readonly float[] StallAngles = { -75f, -37.5f, 37.5f, 75f, -112.5f, 112.5f, 150f, -150f, 0f };
        /// <summary>The nine plaza stalls, then 海叔's crab stall (stall 9) down the beach. (The bus driver, shop 10, has no stall.)</summary>
        public static int StallCount => StallAngles.Length + 1;
        public const int CrabberStall = 9;
        public const int BusStall = 10;

        // ---- The bus stop (汽车站): south of the camp-market path, the bus parked facing west with its door on the path side.
        public static Vector2 BusStopCenter => CampCenter + new Vector2(12f, -9f);
        public const float BusStopRadius = 6.5f;
        public const float BusStopHeight = 1.0f;
        /// <summary>Middle of the parked bus (7 m long, along x), and the way it faces.</summary>
        public static Vector2 BusPosition => BusStopCenter + new Vector2(0f, -2.2f);
        public static Vector2 BusForward => new Vector2(-1f, 0f);
        /// <summary>The driver waits by the bus door (front right of the bus), facing the path.</summary>
        public static Vector2 BusDriverPosition => BusStopCenter + new Vector2(-2.6f, -0.2f);
        public static Vector2 BusArrivalSpot => BusStopCenter + new Vector2(-0.6f, 2.0f);
        public static Vector3 BusSignSpot3D() => new Vector3(BusStopCenter.x + 2.8f, BusStopHeight + 1.75f, BusStopCenter.y + 0.6f);

        public static float DistanceToBusPath(float x, float z)
        {
            Vector2 a = new Vector2(BusStopCenter.x, Mathf.Lerp(CampCenter.y + 1f, MarketCenter.y, 0.55f));
            Vector2 b = BusStopCenter + new Vector2(0f, 1.2f);
            return DistanceToSegment(new Vector2(x, z), a, b);
        }

        public static bool InBusStop(float x, float z, float margin = 0f) =>
            Vector2.Distance(new Vector2(x, z), BusStopCenter) < BusStopRadius + margin;

        // ---- The seafront east of the market: a beach path to 海叔's crab stall, past the three games stalls.
        public static Vector2 CrabberPosition => new Vector2(MarketCenter.x + 26f, ShoreZ(MarketCenter.x + 26f) - 4.5f);
        public const float SeafrontHeight = 0.55f;
        /// <summary>The games stalls (mahjong, jianzi, pitch-pot), in a row facing the beach path.</summary>
        public static Vector2 GameStallPosition(int k)
        {
            float x = MarketCenter.x + 14f + k * 4.6f;
            return new Vector2(x, ShoreZ(x) - 11.5f);
        }
        public static Vector2 GameStallFront => new Vector2(0f, 1f);

        public static float DistanceToSeafrontPath(float x, float z)
        {
            Vector2 a = MarketCenter + new Vector2(MarketRadius * 0.8f, 1.5f);
            Vector2 b = CrabberPosition + new Vector2(0f, -3.2f);
            return DistanceToSegment(new Vector2(x, z), a, b);
        }

        /// <summary>Near the seafront stalls (keeps trees and rocks away).</summary>
        public static bool InSeafront(float x, float z, float margin = 0f)
        {
            var p = new Vector2(x, z);
            if (Vector2.Distance(p, CrabberPosition) < 4.5f + margin) return true;
            for (int k = 0; k < 3; k++) if (Vector2.Distance(p, GameStallPosition(k)) < 3.6f + margin) return true;
            return false;
        }

        /// <summary>Stall i: plaza stalls face the plaza's centre; the crab stall faces inland, its back to the sea.</summary>
        public static Vector2 StallPosition(int i)
        {
            if (i == CrabberStall) return CrabberPosition;
            if (i == BusStall) return BusDriverPosition;
            float a = StallAngles[i] * Mathf.Deg2Rad;
            return MarketCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * StallRing;
        }

        /// <summary>The way stall i's counter faces (towards its customers).</summary>
        public static Vector2 StallFront(int i) => i == CrabberStall ? new Vector2(0f, -1f) : i == BusStall ? new Vector2(0f, 1f) : (MarketCenter - StallPosition(i)).normalized;

        /// <summary>Where a player stands to talk to shop i (in front of the counter).</summary>
        public static Vector2 CustomerSpot(int i) => StallPosition(i) + StallFront(i) * (StallRing * 0.38f);

        /// <summary>z of the waterline at x: a gently wavy coast, straight and calm around the dock.</summary>
        public static float ShoreZ(float x)
        {
            float waves = 4.2f * Mathf.Sin(x * 0.045f + 0.7f) + 2.2f * Mathf.Sin(x * 0.11f + 2.1f) + 0.9f * Mathf.Sin(x * 0.23f + 0.3f);
            float calm = Mathf.Clamp01((Mathf.Abs(x) - 8f) / 16f); // the dock's stretch of beach stays straight
            float wavesAtDock = 4.2f * Mathf.Sin(0.7f) + 2.2f * Mathf.Sin(2.1f) + 0.9f * Mathf.Sin(0.3f);
            return CoastZ + Mathf.Lerp(0f, waves - wavesAtDock, calm);
        }

        /// <summary>The point on the waterline at x.</summary>
        public static Vector2 ShorePoint(float x) => new Vector2(x, ShoreZ(x));

        /// <summary>Signed horizontal distance to the shoreline: positive on land, negative out at sea.</summary>
        public static float ShoreDistance(float x, float z) => ShoreZ(x) - z;

        private static float Fbm(float x, float z, int octaves = 4)
        {
            float v = 0f, a = 0.5f, f = 1f;
            for (int i = 0; i < octaves; i++)
            {
                v += a * Mathf.PerlinNoise(x * f + 31.7f * i, z * f + 11.3f * i);
                f *= 2.03f;
                a *= 0.5f;
            }
            return v; // ~0..1
        }

        /// <summary>Terrain height (metres) at a world xz position.</summary>
        public static float TerrainHeight(float x, float z)
        {
            float d = ShoreDistance(x, z);
            float h;
            if (d < 0f)
            {
                // Sea bed: a sandy shelf near the beach, then deeper and deeper out to sea.
                float depth = Mathf.Min(16f, -d * 0.2f + Mathf.Max(0f, -d - 12f) * 0.09f);
                h = -depth + (Fbm(x * 0.12f, z * 0.12f, 2) - 0.5f) * 0.5f * Mathf.Clamp01(-d / 5f);
            }
            else
            {
                // A wide sandy beach, then gently rising land.
                float beach = Mathf.Min(d, 8f) * 0.06f;
                float land = Mathf.Max(0f, d - 8f) * 0.045f;
                float hills = (Fbm(x * 0.022f, z * 0.022f) - 0.35f) * 5.5f * Smooth(4f, 26f, d);
                h = beach + land + Mathf.Max(hills, -0.2f);
            }

            // Hills ring the land behind the beach (never out at sea).
            float r = new Vector2(x, z).magnitude;
            if (d > 0f) h += Smooth(58f, 120f, r) * Smooth(0f, 18f, d) * 16f * (0.55f + 0.6f * Fbm(x * 0.03f + 5f, z * 0.03f + 9f, 3));

            // Flatten a cosy clearing for the camp.
            float campDist = Vector2.Distance(new Vector2(x, z), CampCenter);
            h = Mathf.Lerp(CampHeight, h, Smooth(CampRadius * 0.6f, CampRadius * 1.25f, campDist));

            // Flatten the market plaza.
            float marketDist = Vector2.Distance(new Vector2(x, z), MarketCenter);
            h = Mathf.Lerp(MarketHeight, h, Smooth(MarketRadius * 0.75f, MarketRadius * 1.35f, marketDist));

            // Flatten a path from camp to the dock.
            float pathDist = DistanceToDockPath(x, z);
            if (d > 0f)
            {
                float target = Mathf.Lerp(0.35f, CampHeight, Mathf.Clamp01(d / 13f));
                h = Mathf.Lerp(target, h, Smooth(1.5f, 4f, pathDist));
            }

            // ...and from the camp to the market.
            float marketPath = DistanceToMarketPath(x, z);
            if (d > 0f) h = Mathf.Lerp((CampHeight + MarketHeight) * 0.5f, h, Smooth(1.5f, 4f, marketPath));

            // The seafront: a level beach path from the market to the crab stall, and flat pads for the stalls.
            if (d > -1f)
            {
                float sea = DistanceToSeafrontPath(x, z);
                h = Mathf.Lerp(SeafrontHeight, h, Smooth(1.5f, 4f, sea));
                var p2 = new Vector2(x, z);
                h = Mathf.Lerp(SeafrontHeight, h, Smooth(3.2f, 5.5f, Vector2.Distance(p2, CrabberPosition)));
                for (int k = 0; k < 3; k++) h = Mathf.Lerp(SeafrontHeight + 0.1f, h, Smooth(3f, 5f, Vector2.Distance(p2, GameStallPosition(k))));
            }

            // The bus stop: a level gravel pad, and a path up to the camp-market path.
            if (d > 0f)
            {
                float stop = Vector2.Distance(new Vector2(x, z), BusStopCenter);
                h = Mathf.Lerp(BusStopHeight, h, Smooth(BusStopRadius * 0.75f, BusStopRadius * 1.3f, stop));
                h = Mathf.Lerp(Mathf.Lerp(BusStopHeight, (CampHeight + MarketHeight) * 0.5f, 0.5f), h, Smooth(1.5f, 3.5f, DistanceToBusPath(x, z)));
            }

            // Last: level the ground just under the cabin floor so no terrain pokes through it
            // (the floor sits at CampHeight; the hill behind the camp used to show inside the house).
            float cabinDist = DistanceOutsideCabin(x, z);
            if (cabinDist < 4f) h = Mathf.Lerp(CampHeight - 0.08f, h, Smooth(0.6f, 4f, cabinDist));

            return h;
        }

        /// <summary>Distance to the nearest footpath (camp-dock or camp-market).</summary>
        public static float DistanceToPath(float x, float z) =>
            Mathf.Min(Mathf.Min(Mathf.Min(DistanceToDockPath(x, z), DistanceToMarketPath(x, z)), DistanceToSeafrontPath(x, z)), DistanceToBusPath(x, z));

        public static float DistanceToMarketPath(float x, float z)
        {
            Vector2 a = CampCenter + new Vector2(4.5f, 1f);
            Vector2 b = MarketCenter - new Vector2(MarketRadius * 0.55f, 0f);
            return DistanceToSegment(new Vector2(x, z), a, b);
        }

        public static bool InMarket(float x, float z, float margin = 0f) =>
            Vector2.Distance(new Vector2(x, z), MarketCenter) < MarketRadius + margin;

        /// <summary>Distance from the camp-to-dock footpath.</summary>
        public static float DistanceToDockPath(float x, float z)
        {
            Vector2 a = DockShorePoint + DockDirection * 1.0f;
            Vector2 b = CampCenter - DockDirection * 1.5f + new Vector2(1.5f, 0f);
            return DistanceToSegment(new Vector2(x, z), a, b);
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>True when the point is over the dock deck footprint.</summary>
        public static bool IsOnDock(float x, float z, float margin = 0f)
        {
            Vector2 start = DockShorePoint + DockDirection * 1.5f;
            Vector2 dir = -DockDirection;
            Vector2 p = new Vector2(x, z) - start;
            float along = Vector2.Dot(p, dir);
            float side = Mathf.Abs(Vector2.Dot(p, new Vector2(-dir.y, dir.x)));
            return along > -margin && along < DockLength + 1.5f + margin && side < DockWidth * 0.5f + margin;
        }

        public static bool IsWater(float x, float z) => TerrainHeight(x, z) < WaterLevel - 0.12f;

        public static float WaterDepth(float x, float z) => Mathf.Max(0f, WaterLevel - TerrainHeight(x, z));

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
