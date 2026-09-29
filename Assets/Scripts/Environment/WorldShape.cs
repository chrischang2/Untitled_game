using UnityEngine;

namespace UntitledGame.Environment
{
    /// <summary>
    /// Deterministic description of the lake world. Used by the editor world generator to build
    /// the terrain mesh and at runtime for cheap "is this water / how deep / where's the ground" queries.
    /// </summary>
    public static class WorldShape
    {
        public const float WaterLevel = 0f;
        public const float TerrainSize = 260f;
        public const float PlayableRadius = 82f;
        public const float LakeBaseRadius = 27f;

        /// <summary>Dock heads out from the south shore towards the lake centre.</summary>
        public const float DockAngleDeg = -90f;
        public const float DockLength = 13f;
        public const float DockWidth = 2.6f;
        public const float DockDeckHeight = 0.55f;

        public static Vector2 LakeCenter => Vector2.zero;

        public static Vector2 DockDirection => new Vector2(Mathf.Cos(DockAngleDeg * Mathf.Deg2Rad), Mathf.Sin(DockAngleDeg * Mathf.Deg2Rad));

        /// <summary>Where the dock meets the shoreline.</summary>
        public static Vector2 DockShorePoint => LakeCenter + DockDirection * LakeRadiusAt(DockAngleDeg * Mathf.Deg2Rad);

        /// <summary>Centre of the little camp (cabin, campfire) behind the dock.</summary>
        public static Vector2 CampCenter => DockShorePoint + DockDirection * 13f;

        public const float CampRadius = 10f;
        public const float CampHeight = 0.9f;

        public static float LakeRadiusAt(float angle)
        {
            float r = LakeBaseRadius
                      + 4.5f * Mathf.Sin(2f * angle + 0.7f)
                      + 2.6f * Mathf.Sin(3f * angle + 2.1f)
                      + 1.3f * Mathf.Sin(5f * angle + 0.3f);
            // Keep the dock stretch of shoreline smooth and gently curved.
            float dockDelta = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, DockAngleDeg);
            float calm = Mathf.Clamp01(Mathf.Abs(dockDelta) / 25f);
            float calmR = LakeBaseRadius + 1f;
            return Mathf.Lerp(calmR, r, calm);
        }

        /// <summary>Signed horizontal distance to the shoreline (negative inside the lake).</summary>
        public static float ShoreDistance(float x, float z)
        {
            Vector2 p = new Vector2(x, z) - LakeCenter;
            float a = Mathf.Atan2(p.y, p.x);
            return p.magnitude - LakeRadiusAt(a);
        }

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
                // Lake bed: gentle shelf near the shore, deeper towards the middle.
                float depth = Mathf.Min(4.8f, -d * 0.24f + Mathf.Max(0f, -d - 6f) * 0.08f);
                h = -depth + (Fbm(x * 0.12f, z * 0.12f, 2) - 0.5f) * 0.4f * Mathf.Clamp01(-d / 5f);
            }
            else
            {
                float beach = Mathf.Min(d, 3f) * 0.11f;
                float land = Mathf.Max(0f, d - 3f) * 0.045f;
                float hills = (Fbm(x * 0.022f, z * 0.022f) - 0.35f) * 5.5f * Smooth(4f, 26f, d);
                h = beach + land + Mathf.Max(hills, -0.2f);
            }

            float r = new Vector2(x, z).magnitude;
            h += Smooth(58f, 120f, r) * 16f * (0.55f + 0.6f * Fbm(x * 0.03f + 5f, z * 0.03f + 9f, 3));

            // Flatten a cosy clearing for the camp.
            float campDist = Vector2.Distance(new Vector2(x, z), CampCenter);
            h = Mathf.Lerp(CampHeight, h, Smooth(CampRadius * 0.6f, CampRadius * 1.25f, campDist));

            // Flatten a path from camp to the dock.
            float pathDist = DistanceToPath(x, z);
            if (d > 0f)
            {
                float target = Mathf.Lerp(0.35f, CampHeight, Mathf.Clamp01(d / 13f));
                h = Mathf.Lerp(target, h, Smooth(1.5f, 4f, pathDist));
            }
            return h;
        }

        /// <summary>Distance from the camp-to-dock footpath (a gently curving line).</summary>
        public static float DistanceToPath(float x, float z)
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
