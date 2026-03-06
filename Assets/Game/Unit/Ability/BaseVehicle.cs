using Game.Unit.Ability.BaseAbilities;

namespace Game.Unit
{
    public class BaseVehicle : IAbility
    {
        public Actor Self { get; set; }

        public void Init()
        {
            var baseUnit = new BaseUnit();
            baseUnit.BindActor(Self);
            baseUnit.Init();
            Self.Abilities.Add(baseUnit);

            var movement = new Movement();
            movement.BindActor(Self);
            movement.Init();
            Self.Abilities.Add(movement);
        }

        public void Tick()
        {
            // Passive by default.
        }
    }
}