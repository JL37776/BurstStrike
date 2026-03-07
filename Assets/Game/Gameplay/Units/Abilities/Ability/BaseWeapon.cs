using Game.Unit.Ability.BaseAbilities;

namespace Game.Unit
{
    public class BaseWeapon: IAbility
    {
        public Actor Self { get; set; }

        // Stats (wired from YAML via LogicUnitFactory.ApplyAbilityParams)
        public int Damage;
        public int Range;

        // Local offset from YAML Position. Interpreted in world axes for now.
        public Game.Scripts.Fixed.FixedVector3 LocalOffset = Game.Scripts.Fixed.FixedVector3.Zero;

        // Attachment rules (from UnitData)
        public bool UseParentPosition;
        public bool BindParentRotation;

        public void Init()
        {
            // No-op: weapon behaviour is driven by Tick() for transform sync.
        }

        public void Tick()
        {
            if (Self == null) return;
            if (Self.parent == null) return; // only meaningful as a child

            // Get parent + self location.
            Location parentLoc = null;
            foreach (var ab in Self.parent.Abilities)
                if (ab is Location l) { parentLoc = l; break; }

            Location myLoc = null;
            foreach (var ab in Self.Abilities)
                if (ab is Location l) { myLoc = l; break; }

            if (parentLoc == null || myLoc == null) return;

            if (UseParentPosition)
            {
                myLoc.Position = parentLoc.Position + LocalOffset;
            }

            if (BindParentRotation)
            {
                myLoc.Rotation = parentLoc.Rotation;
            }
        }
    }
}