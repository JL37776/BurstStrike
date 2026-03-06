using System;
using System.Collections.Generic;
using Game.Grid;
using Game.Scripts.Fixed;
using Game.Unit;
using Game.Unit.Ability.BaseAbilities;

namespace Game.World.Logic.Service
{
    /// <summary>
    /// Pure logic-thread damage applier.
    ///
    /// Contract:
    /// - This service applies damage only (it does not validate hit conditions, factions, armor rules, line-of-sight, etc.).
    /// - Intended to be used by bullets/projectiles/aoe logic after they've decided a hit happened.
    /// - Uses UnitsFetchService for candidate acquisition.
    /// </summary>
    public sealed class UnitsDamageService
    {
        private readonly LogicWorld _world;
        private readonly UnitsFetchService _fetch;

        public UnitsDamageService(LogicWorld world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _fetch = new UnitsFetchService(_world);
        }

        public UnitsDamageService(LogicWorld world, UnitsFetchService fetch)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
        }

        public bool ApplyDamageToUnit(int id, int damage)
        {
            if (damage <= 0) return false;

            var a = _fetch.GetUnitById(id);
            if (a == null) return false;

            var h = FindHealth(a);
            if (h == null) return false;

            h.Damage(damage);
            return true;
        }

        public int ApplyDamageToUnitByRange(FixedVector3 center, Fixed range, int damage, List<Actor> scratch = null)
        {
            if (damage <= 0) return 0;

            scratch ??= new List<Actor>(64);
            _fetch.GetRangeUnits(center, range, scratch);

            int applied = 0;
            for (int i = 0; i < scratch.Count; i++)
            {
                var a = scratch[i];
                var h = FindHealth(a);
                if (h == null) continue;
                h.Damage(damage);
                applied++;
            }

            return applied;
        }

        public int ApplyDamageToUnitByCellRange(GridPosition cell, int range, int damage, List<Actor> scratch = null)
        {
            if (damage <= 0) return 0;

            scratch ??= new List<Actor>(64);
            _fetch.GetRangeCellsUnits(cell, range, scratch);

            int applied = 0;
            for (int i = 0; i < scratch.Count; i++)
            {
                var a = scratch[i];
                var h = FindHealth(a);
                if (h == null) continue;
                h.Damage(damage);
                applied++;
            }

            return applied;
        }

        private static Health FindHealth(Actor actor)
        {
            if (actor == null) return null;
            foreach (var ab in actor.Abilities)
                if (ab is Health h) return h;
            return null;
        }
    }
}
