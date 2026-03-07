using System;
using UnityEngine;

namespace Game.Unit
{
    /// <summary>
    /// Hard-coded player palette (8 players maximum).
    /// PlayerId is stable => color is stable.
    /// 0-3: cool tones; 4-7: warm tones.
    /// </summary>
    public static class PlayerPalette
    {
        public const int MaxPlayers = 8;

        // Soft, aesthetic colors (pastel-ish), readable on dark/light.
        private static readonly Color[] Colors =
        {
            // cool (0-3): light blues / light cyans (more visible)
            // cool (0-3): vivid & bright blues / cyans (high visibility)

            new Color(0.00f, 1.00f, 0.55f, 1f), // bright green-teal (更绿，不偏白)
            new Color(0.05f, 0.55f, 1.00f, 1f), // pure vivid blue (更蓝)
            new Color(0.00f, 0.85f, 1.00f, 1f), // neon cyan (亮度高、偏蓝)
            new Color(0.30f, 0.00f, 1.00f, 1f), // 2 电光蓝（Electric Blue）——偏紫蓝，与 0 完全不同


            // warm (4-7): yellow / orange / red / purple
            new Color(1.00f, 0.92f, 0.35f, 1f), // pastel lemon yellow
            new Color(1.00f, 0.70f, 0.30f, 1f), // soft orange
            new Color(1.00f, 0.45f, 0.45f, 1f), // soft red
            new Color(0.78f, 0.55f, 1.00f, 1f), // light purple
        };

        public static Color GetColor(int playerId)
        {
            if ((uint)playerId >= MaxPlayers)
                throw new ArgumentOutOfRangeException(nameof(playerId), $"playerId must be in [0,{MaxPlayers - 1}] but was {playerId}.");
            return Colors[playerId];
        }

        public static int ClampPlayerId(int playerId)
        {
            if (playerId < 0) return 0;
            if (playerId >= MaxPlayers) return MaxPlayers - 1;
            return playerId;
        }
    }
}
