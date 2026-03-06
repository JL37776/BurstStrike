namespace Game.World
{
    /// <summary>
    /// Deterministic logic-side registry mapping archetypeId -> serialized UnitData.
    /// LogicWorld uses this to apply YAML-authored ability params (e.g., Movement speed) during UnitSpawn.
    /// </summary>
    public interface IArchetypeRegistry
    {
        bool TryGetUnitData(int archetypeId, out Game.Serialization.UnitData data);

        /// <summary>
        /// Lookup archetype by its YAML 'Id' field (e.g., "weapon01").
        /// Used when children entries are lightweight refs and only specify Id.
        /// </summary>
        bool TryGetUnitData(string archetypeStringId, out Game.Serialization.UnitData data);
    }
}
