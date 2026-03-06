using Game.Scripts.Fixed;
using Game.Unit.Activity;

namespace Game.Unit.Ability.BaseAbilities
{
    public class Weapon:IAbility
    {
        // TODO: real target acquisition (likely via EnemySearchService + weapon range).
        private bool HasValidTarget(out FixedVector3 targetPos)
        {
            targetPos = default;
            return false;
        }
        
        private bool WeaponReady()
        {
            return false;
        }
        
        public Actor Self { get; set; }

        public void Init()
        {
            // Placeholder for future weapon stats/cooldowns init.
        }
        
        public void Tick()
        {
            if (Self == null) return;
            if (Self.Activities == null || Self.Activities.Count == 0) return;

            // If we're already attacking, don't stack more.
            if (Self.Activities.Peek() is Attack)
            {
                return;
            }
 
            if (HasValidTarget(out var targetPos))
            {
                if (WeaponReady())
                {
                    Self.Activities.Push(new Attack());
                }
                else
                {
                    // Avoid stacking multiple ChaseTarget activities.
                    if (!(Self.Activities.Peek() is Game.Unit.Activity.ChaseTarget))
                        Self.Activities.Push(new Game.Unit.Activity.ChaseTarget(Self, targetPos));
                }
            }
        }
    }
}