using Game.Unit.Ability.BaseAbilities;

namespace Game.Unit
{
    public class BaseUnit : IAbility
    {
        public Actor Self { get; set; }

        public void Init()
        {
            // Add core abilities.
            if (Self == null) return;

            bool hasLoc = false;
            bool hasHp = false;
            foreach (var ab in Self.Abilities)
            {
                if (!hasLoc && ab is Location) hasLoc = true;
                else if (!hasHp && ab is Health) hasHp = true;
                if (hasLoc && hasHp) break;
            }

            if (!hasLoc)
            {
                var loc = new Location();
                loc.BindActor(Self);
                Self.Abilities.Add(loc);
            }

            if (!hasHp)
            {
                var hp = new Health();
                hp.BindActor(Self);
                hp.Init();
                Self.Abilities.Add(hp);
            }
        }

        public void Tick()
        {
            // Passive by default.
        }
    }
}