using System;
using System.Collections.Generic;
using Game.Unit;

namespace Game.Serialization
{
    public static class AbilityFactory
    {
        // Create ability instance by name. Uses explicit mapping first then reflection fallback.
        public static IAbility CreateAbility(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            switch (name)
            {
                case "BaseUnit":
                case "BaseUnitAbility":
                    return new BaseUnit();
                case "Location":
                    return new Game.Unit.Ability.BaseAbilities.Location();
                case "Health":
                    return new Game.Unit.Ability.BaseAbilities.Health();
                case "Movement":
                    return new Game.Unit.Ability.BaseAbilities.Movement();
                case "Navigation":
                    return new Game.Unit.Ability.Navigation();
                case "Guard":
                    return new Game.Unit.Ability.BaseAbilities.Guard();
                case "Weapon":
                    return new Game.Unit.Ability.BaseAbilities.Weapon();
                case "Armament":
                    // Armament requires a WeaponDef — created via CreateArmament() instead.
                    // Return null here; caller should use dedicated factory method.
                    return null;
                case "ArmorInfo":
                case "Armor":
                    return new Game.Unit.Ability.ArmorInfo();
                case "BaseWeapon":
                case "BaseWeaponAbility":
                    return new Game.Unit.BaseWeapon();
                default:
                    // Reflection fallback: try full type name in this assembly (Game.Unit.<name>)
                    var type = Type.GetType(name) ?? Type.GetType("Game.Unit." + name);
                    if (type == null)
                    {
                        // Try searching all loaded assemblies
                        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            type = asm.GetType(name) ?? asm.GetType("Game.Unit." + name);
                            if (type != null) break;
                        }
                    }

                    if (type == null) return null;
                    try
                    {
                        return Activator.CreateInstance(type) as IAbility;
                    }
                    catch
                    {
                        return null;
                    }
            }
        }

        public static List<IAbility> CreateAbilities(IEnumerable<string> names)
        {
            var list = new List<IAbility>();
            if (names == null) return list;
            foreach (var n in names)
            {
                var a = CreateAbility(n);
                if (a != null) list.Add(a);
            }
            return list;
        }
    }
}
