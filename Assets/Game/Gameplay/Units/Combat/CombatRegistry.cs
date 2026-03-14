using System.Collections.Generic;

namespace Game.Combat
{
    /// <summary>
    /// Central registry for all combat definitions (weapons, warheads, projectiles).
    /// Preloaded from YAML on the main thread, then injected into LogicWorld.
    /// Immutable after construction — safe for logic-thread reads.
    /// </summary>
    public sealed class CombatRegistry
    {
        private readonly Dictionary<string, WeaponDef> _weapons;
        private readonly Dictionary<string, WarheadDef> _warheads;
        private readonly Dictionary<string, ProjectileDef> _projectiles;

        public CombatRegistry(
            Dictionary<string, WeaponDef> weapons,
            Dictionary<string, WarheadDef> warheads,
            Dictionary<string, ProjectileDef> projectiles)
        {
            _weapons = weapons ?? new Dictionary<string, WeaponDef>();
            _warheads = warheads ?? new Dictionary<string, WarheadDef>();
            _projectiles = projectiles ?? new Dictionary<string, ProjectileDef>();
        }

        /// <summary>Empty registry (no combat data loaded).</summary>
        public static readonly CombatRegistry Empty = new CombatRegistry(null, null, null);

        // ── Lookups ──────────────────────────────────────────────────────

        public bool TryGetWeapon(string id, out WeaponDef def)
        {
            def = null;
            if (string.IsNullOrEmpty(id)) return false;
            return _weapons.TryGetValue(id, out def);
        }

        public bool TryGetWarhead(string id, out WarheadDef def)
        {
            def = null;
            if (string.IsNullOrEmpty(id)) return false;
            return _warheads.TryGetValue(id, out def);
        }

        public bool TryGetProjectile(string id, out ProjectileDef def)
        {
            def = null;
            if (string.IsNullOrEmpty(id)) return false;
            return _projectiles.TryGetValue(id, out def);
        }

        // ── Convenience (throws on missing — use in validated paths only) ─

        public WeaponDef GetWeapon(string id) =>
            _weapons.TryGetValue(id, out var d) ? d : null;

        public WarheadDef GetWarhead(string id) =>
            _warheads.TryGetValue(id, out var d) ? d : null;

        public ProjectileDef GetProjectile(string id) =>
            _projectiles.TryGetValue(id, out var d) ? d : null;

        // ── Stats ────────────────────────────────────────────────────────

        public int WeaponCount => _weapons.Count;
        public int WarheadCount => _warheads.Count;
        public int ProjectileCount => _projectiles.Count;
    }
}
