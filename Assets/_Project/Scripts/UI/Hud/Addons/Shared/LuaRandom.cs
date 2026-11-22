using System;

namespace HudAddons
{
    /// <summary>
    /// math.random as LuaJIT (Garry's Mod's Lua) computes it from one draw r in [0, 1): math.random() is r,
    /// math.random(m) is floor(r·m) + 1, math.random(m, n) is floor(r·(n − m + 1)) + m. Both bounds are inclusive and
    /// fractional bounds give LuaJIT's own (fractional) answers, so a ported roll lands on exactly the values the addon's
    /// did. Unity's Random.value can return 1, which would give n + 1 here, so the draw comes from System.Random. The
    /// sequence itself is this PC's own: these rolls only shape this player's HUD.
    /// </summary>
    public static class LuaRandom
    {
        private static readonly Random random = new Random();

        /// <summary>math.random(): a number in [0, 1).</summary>
        public static double Next() => random.NextDouble();

        /// <summary>math.random(m): floor(r·m) + 1, so 1..m for a whole m.</summary>
        public static double Next(double m) => Math.Floor(random.NextDouble() * m) + 1.0;

        /// <summary>math.random(m, n): floor(r·(n − m + 1)) + m, so m..n for whole bounds.</summary>
        public static double Next(double m, double n) => Math.Floor(random.NextDouble() * (n - m + 1.0)) + m;

        /// <summary>math.random(m, n) for whole bounds, as an int: m..n inclusive.</summary>
        public static int Range(int m, int n) => (int)Next(m, n);
    }
}
