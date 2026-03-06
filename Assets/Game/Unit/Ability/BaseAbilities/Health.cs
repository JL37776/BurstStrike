using System;

namespace Game.Unit.Ability.BaseAbilities
{
    public class Health : IAbility
    {
        public Actor Self { get; set; }

        public int MaxHP { get; set; } = 100;
        public int HP { get; set; } = 100;

        public void Init()
        {
            if (MaxHP <= 0) MaxHP = 1;
            if (HP <= 0) HP = MaxHP;
            if (HP > MaxHP) HP = MaxHP;
        }

        public void Tick()
        {
            // Passive by default.
        }

        public void Damage(int amount)
        {
            if (amount <= 0) return;
            HP -= amount;
            if (HP < 0) HP = 0;
        }

        public void Heal(int amount)
        {
            if (amount <= 0) return;
            HP += amount;
            if (HP > MaxHP) HP = MaxHP;
        }
    }
}