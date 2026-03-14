using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace Game.Core
{
    /// <summary>
    /// Unified game log facade with compile-time stripping.
    /// <para>
    /// Info / Warn calls are tagged with <c>[Conditional("ENABLE_GAME_LOG")]</c> so they (and their
    /// argument expressions) are completely stripped by the compiler when the symbol is not defined
    /// (e.g. in Release builds).
    /// </para>
    /// <para>
    /// Error calls are <b>always</b> compiled — errors should never be silently swallowed.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Usage:
    /// <code>
    ///   GameLog.Info(GameLog.Tag.World, $"tick={tick} units={count}");
    ///   GameLog.Warn(GameLog.Tag.Pathing, "FlowField fallback triggered");
    ///   GameLog.Error(GameLog.Tag.Config, "Missing required YAML field: Health");
    /// </code>
    /// To enable in a build, add <c>ENABLE_GAME_LOG</c> to
    /// <c>Project Settings &gt; Player &gt; Scripting Define Symbols</c>.
    /// </remarks>
    public static class GameLog
    {
        // ── Tag constants ────────────────────────────────────────────────
        /// <summary>Predefined log category tags. Use free-form strings if needed.</summary>
        public static class Tag
        {
            public const string World       = "World";
            public const string Snap        = "Snap";
            public const string Render      = "Render";
            public const string RenderIds   = "RenderIds";
            public const string Pathing     = "Pathing";
            public const string Command     = "Command";
            public const string Unit        = "Unit";
            public const string Archetype   = "Archetype";
            public const string Config      = "Config";
            public const string Partition   = "Partition";
            public const string Debug       = "Debug";
            public const string Test        = "Test";
        }

        // ── Info ─────────────────────────────────────────────────────────
        /// <summary>
        /// Log an informational message. Stripped in builds without <c>ENABLE_GAME_LOG</c>.
        /// </summary>
        [Conditional("ENABLE_GAME_LOG")]
        public static void Info(string tag, string msg)
        {
            Debug.Log($"[{tag}] {msg}");
        }

        /// <summary>Tagless variant (legacy migration convenience).</summary>
        [Conditional("ENABLE_GAME_LOG")]
        public static void Info(string msg)
        {
            Debug.Log(msg);
        }

        // ── Warn ─────────────────────────────────────────────────────────
        /// <summary>
        /// Log a warning. Stripped in builds without <c>ENABLE_GAME_LOG</c>.
        /// </summary>
        [Conditional("ENABLE_GAME_LOG")]
        public static void Warn(string tag, string msg)
        {
            Debug.LogWarning($"[{tag}] {msg}");
        }

        /// <summary>Tagless variant.</summary>
        [Conditional("ENABLE_GAME_LOG")]
        public static void Warn(string msg)
        {
            Debug.LogWarning(msg);
        }

        // ── Error (always compiled) ─────────────────────────────────────
        /// <summary>
        /// Log an error. <b>Always</b> compiled — errors must not be silently lost.
        /// </summary>
        public static void Error(string tag, string msg)
        {
            Debug.LogError($"[{tag}] {msg}");
        }

        /// <summary>Tagless variant.</summary>
        public static void Error(string msg)
        {
            Debug.LogError(msg);
        }
    }
}
