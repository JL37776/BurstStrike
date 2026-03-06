using System;
using System.Collections.Generic;

namespace Game.Serialization
{
    /// <summary>
    /// Validates UnitData loaded from YAML.
    /// Contract (per unit, including all children):
    /// - Position ("Location") must exist and contain at least 3 elements.
    /// - AbilityParams.Movement.MaxSpeed must be present.
    /// - AbilityParams.Movement.Acceleration must be present.
    /// Note: Whether a unit actually uses Movement/Location at runtime depends on attached abilities/components.
    /// This validator only enforces config presence.
    /// </summary>
    public static class UnitDataValidator
    {
        public static bool TryValidate(UnitData root, out List<string> errors)
        {
            errors = new List<string>();
            ValidateRecursive(root, "root", errors);
            return errors.Count == 0;
        }

        private static void ValidateRecursive(UnitData data, string path, List<string> errors)
        {
            if (data == null)
            {
                errors.Add($"{path}: UnitData is null");
                return;
            }

            // Required on root: ArchetypeId
            if (path == "root")
            {
                if (data.ArchetypeId <= 0)
                    errors.Add($"{path}: missing required field 'ArchetypeId' (must be > 0). This is used by spawn commands (Command.Int0).");
            }

            // Child reference stub rule:
            // Allow children entries to be lightweight references, e.g.
            //   Children:
            //     - Id: "weapon01"
            // In that case, the actual fields are resolved from the archetype registry later.
            // Only apply strict per-unit validation if this node isn't a ref-only stub.
            bool isRoot = path == "root";
            bool isRefOnlyChild = !isRoot
                                 && !string.IsNullOrWhiteSpace(data.Id)
                                 && data.Layer == Game.Map.MapLayer.None
                                 && (data.Position == null || data.Position.Length == 0)
                                 && data.AbilityParams == null
                                 && (data.Abilities == null || data.Abilities.Count == 0)
                                 && (data.Children == null || data.Children.Count == 0);

            if (!isRefOnlyChild)
            {
                // Required: Layer
                if (data.Layer == Game.Map.MapLayer.None)
                {
                    errors.Add($"{path}: missing required field 'Layer' or invalid value 'None'. Expected one of Game.Map.MapLayer (e.g. FootUnits, Tanks...).");
                }

                // Location -> Position
                if (data.Position == null || data.Position.Length < 3)
                {
                    errors.Add($"{path}: missing required field 'Position' (Location). Expected [x,y,z].");
                }

                // Movement params
                var mp = data.AbilityParams?.Movement;
                if (mp == null)
                {
                    errors.Add($"{path}: missing required field 'AbilityParams.Movement' (needed for MaxSpeed/Acceleration).");
                }
                else
                {
                    if (!mp.MaxSpeed.HasValue)
                        errors.Add($"{path}: missing required field 'AbilityParams.Movement.MaxSpeed'.");
                    if (!mp.Acceleration.HasValue)
                        errors.Add($"{path}: missing required field 'AbilityParams.Movement.Acceleration'.");
                    if (mp.TurnSpeedDeg.HasValue && mp.TurnSpeedDeg.Value < 0)
                        errors.Add($"{path}: AbilityParams.Movement.TurnSpeedDeg must be >= 0 (0 means infinite).");
                }
            }

            if (data.Children == null) return;
            for (int i = 0; i < data.Children.Count; i++)
            {
                ValidateRecursive(data.Children[i], $"{path}.Children[{i}]", errors);
            }
        }
    }
}
