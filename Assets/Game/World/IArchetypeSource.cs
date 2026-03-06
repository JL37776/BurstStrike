using System.Collections.Generic;
using Game.Serialization;

namespace Game.World
{
    /// <summary>
    /// Provides UnitData archetypes to the game at startup.
    /// 
    /// Implementations MUST:
    /// - Perform any IO (disk/network) outside of the logic thread.
    /// - Return deterministically ordered results.
    /// - Return validated UnitData objects.
    /// </summary>
    public interface IArchetypeSource
    {
        /// <summary>
        /// Load all archetypes and return mapping archetypeId -> UnitData.
        /// 
        /// Errors should be returned in <paramref name="errors"/> rather than thrown, so World can decide how to handle them.
        /// </summary>
        IReadOnlyDictionary<int, UnitData> LoadAll(out List<string> errors);
    }
}
