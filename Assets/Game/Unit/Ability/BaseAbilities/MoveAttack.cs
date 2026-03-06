using Game.Scripts.Fixed;
using Game.Unit.Activity;

namespace Game.Unit.Ability.BaseAbilities
{
    public class MovementAttack: IAbility
    {
        private FixedVector3? target;
        public Actor Self { get; set; }

        public void Init()
        {
            target = null;
        }

        private bool CheckArrived()
        {
            return true;
        }
        public void Tick()
        {
            if (target==null)
            {
                return;
            }
            if (!(Self.Activities.Peek() is MoveAttack))
            {
                Self.Activities.Push(new MoveAttack());
            }
            if (CheckArrived())
            {
                target = null;
                Self.Activities.Pop();
            }
        }
    }
}