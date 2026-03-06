using Game.Unit.Ability.BaseAbilities;
using Game.World;
using Game.World.Logic;

namespace Game.Unit.Activity
{
    /// <summary>
    /// Guard loop activity:
    /// - Runs when the unit is otherwise idle
    /// - Reads guard params from Guard ability (range + alert layers)
    /// - Queries EnemySearchService for hostiles in range
    /// - (Next step) transitions into chase/attack activities
    /// </summary>
    public sealed class GuardActivity : IActivity
    {
        private readonly Actor _self;
        private readonly Guard _guard;

        public GuardActivity(Actor self, Guard guard)
        {
            _self = self;
            _guard = guard;
        }

        public bool IsFinished() => false;

        public void Tick()
        {
            if (_self == null || _guard == null) return;

            // If we're already chasing, don't start another chase from guard loop.
            if (_guard.IsChasing) return;

            // Enemy search service is provided by LogicWorld via Actor.World.
            var world = _self.World as LogicWorld;
            var es = world != null ? world.EnemySearch : null;
            if (es == null) return;

            // Need current position
            Location loc = null;
            foreach (var ab in _self.Abilities)
            {
                if (ab is Location l) { loc = l; break; }
            }
            if (loc == null) return;

            // Query: guard range + filter by alert layers.
            // NOTE: EnemySearchRequest currently supports MovementMask; we extend it with AlertMask on the service next.
            var req = new EnemySearchRequest(
                world: world,
                self: _self,
                origin: loc.Position,
                range: _guard.AlertRange,
                alertMask: _guard.AlertLayers,
                movementMask: 0u,
                maxResults: 1,
                tick: world.Tick);

            // Until EnemySearchService implements filtering by UnitAlertLayer, this will simply return false.
            if (!es.TryFindNearest(req, out var enemy))
                return;

            // Acquire target id + its current grid cell for deterministic validation.
            var targetCell = default(Game.Grid.GridPosition);
            var map = world.Map;
            if (map != null)
                targetCell = map.Grid.WorldToCell(new Game.Scripts.Fixed.FixedVector2(enemy.Position.x, enemy.Position.z));

            // v1: Chase+path (B1) is implemented inside ChaseTarget for MoveOnly/MoveThenRotate.
            _self.Activities?.Push(new ChaseTarget(_self, enemy.ActorId, targetCell, enemy.Position,
                ChaseTarget.ChaseMode.MoveThenRotate, stopDistance: Game.Scripts.Fixed.Fixed.FromInt(5)));

            _guard.NotifyChaseStarted();
        }
    }
}
