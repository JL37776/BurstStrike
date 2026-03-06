using Game.Scripts.Fixed;
using Game.World.Logic;

namespace Game.World
{
    /// <summary>
    /// Rendering-only snapshot.
    /// 
    /// Design goals:
    /// - Small payload (no strings, no nullable fields) for frequent cross-thread transfer.
    /// - Deterministic, immutable view of unit positions for a single logic tick.
    /// - Suitable for main-thread rendering.
    /// </summary>
    public readonly struct RenderSnapshot : ILogicOutput
    {
        public readonly int Tick;
        public readonly RenderUnitSnapshot[] Units;

        public RenderSnapshot(int tick, RenderUnitSnapshot[] units)
        {
            Tick = tick;
            Units = units;
        }
    }

    public readonly struct RenderUnitSnapshot
    {
        public readonly int Id;
        public readonly int Index;
        public readonly FixedVector3 Position;
        public readonly FixedQuaternion Rotation;

        public readonly int FactionId;
        public readonly int OwnerUserId;
        public readonly FixedVector3 ColorRgb;

        // Health (logic -> render)
        public readonly int CurrentHp;
        public readonly int MaxHp;

        /// <summary>
        /// Debug-only: current top activity name on the actor's activity stack.
        /// Null/empty if none.
        /// </summary>
        public readonly string TopActivity;

        /// <summary>
        /// Debug-only: full activity stack snapshot (top -> bottom).
        /// Null if unknown/unavailable.
        /// </summary>
        public readonly string[] ActivityStack;

        /// <summary>
        /// Debug-only: ability type names held by the actor.
        /// Null if unknown/unavailable.
        /// </summary>
        public readonly string[] Abilities;

        /// <summary>
        /// Debug-only: number of child actors attached to this root actor.
        /// Null if not synced.
        /// </summary>
        public readonly int? ChildCount;

        /// <summary>
        /// Debug-only: per-child ability summaries (each entry is a sorted list of ability type names for that child).
        /// Null if not synced.
        /// </summary>
        public readonly string[][] ChildAbilities;

        /// <summary>
        /// Debug-only: root archetype string id (UnitData.Id) used to build this unit.
        /// Null if not synced.
        /// </summary>
        public readonly string RootArchetypeId;

        // Legacy constructor (kept for backwards compatibility in case any generated/older code still calls it).
        public RenderUnitSnapshot(int id, int index, FixedVector3 position, FixedQuaternion rotation)
            : this(id, index, position, rotation,
                factionId: 0, ownerUserId: -1, colorRgb: FixedVector3.Zero,
                currentHp: 0, maxHp: 0,
                topActivity: null, activityStack: null, abilities: null, childCount: null, childAbilities: null, rootArchetypeId: null)
        {
        }

        public RenderUnitSnapshot(int id, int index, FixedVector3 position, FixedQuaternion rotation, int factionId, int ownerUserId, FixedVector3 colorRgb)
            : this(id, index, position, rotation,
                factionId, ownerUserId, colorRgb,
                currentHp: 0, maxHp: 0,
                topActivity: null, activityStack: null, abilities: null, childCount: null, childAbilities: null, rootArchetypeId: null)
        {
        }

        public RenderUnitSnapshot(int id, int index, FixedVector3 position, FixedQuaternion rotation, int factionId, int ownerUserId, FixedVector3 colorRgb, string topActivity)
            : this(id, index, position, rotation,
                factionId, ownerUserId, colorRgb,
                currentHp: 0, maxHp: 0,
                topActivity, activityStack: null, abilities: null, childCount: null, childAbilities: null, rootArchetypeId: null)
        {
        }

        public RenderUnitSnapshot(int id, int index, FixedVector3 position, FixedQuaternion rotation, int factionId, int ownerUserId, FixedVector3 colorRgb, string topActivity, string[] activityStack)
            : this(id, index, position, rotation,
                factionId, ownerUserId, colorRgb,
                currentHp: 0, maxHp: 0,
                topActivity, activityStack, abilities: null, childCount: null, childAbilities: null, rootArchetypeId: null)
        {
        }

        public RenderUnitSnapshot(int id, int index, FixedVector3 position, FixedQuaternion rotation, int factionId, int ownerUserId, FixedVector3 colorRgb, string topActivity, string[] activityStack, string[] abilities)
            : this(id, index, position, rotation,
                factionId, ownerUserId, colorRgb,
                currentHp: 0, maxHp: 0,
                topActivity, activityStack, abilities, childCount: null, childAbilities: null, rootArchetypeId: null)
        {
        }

        public RenderUnitSnapshot(int id, int index, FixedVector3 position, FixedQuaternion rotation,
            int factionId, int ownerUserId, FixedVector3 colorRgb,
            string topActivity, string[] activityStack, string[] abilities,
            int? childCount, string[][] childAbilities)
            : this(id, index, position, rotation,
                factionId, ownerUserId, colorRgb,
                currentHp: 0, maxHp: 0,
                topActivity, activityStack, abilities, childCount, childAbilities, rootArchetypeId: null)
        {
        }

        public RenderUnitSnapshot(int id, int index, FixedVector3 position, FixedQuaternion rotation,
            int factionId, int ownerUserId, FixedVector3 colorRgb,
            string topActivity, string[] activityStack, string[] abilities,
            int? childCount, string[][] childAbilities,
            string rootArchetypeId)
            : this(id, index, position, rotation,
                factionId, ownerUserId, colorRgb,
                currentHp: 0, maxHp: 0,
                topActivity, activityStack, abilities, childCount, childAbilities, rootArchetypeId)
        {
        }

        // New full constructor.
        public RenderUnitSnapshot(int id, int index, FixedVector3 position, FixedQuaternion rotation,
            int factionId, int ownerUserId, FixedVector3 colorRgb,
            int currentHp, int maxHp,
            string topActivity, string[] activityStack, string[] abilities,
            int? childCount, string[][] childAbilities,
            string rootArchetypeId)
        {
            Id = id;
            Index = index;
            Position = position;
            Rotation = rotation;
 
            FactionId = factionId;
            OwnerUserId = ownerUserId;
            ColorRgb = colorRgb;

            CurrentHp = currentHp;
            MaxHp = maxHp;
 
            TopActivity = topActivity;
            ActivityStack = activityStack;
            Abilities = abilities;
 
            ChildCount = childCount;
            ChildAbilities = childAbilities;
            RootArchetypeId = rootArchetypeId;
         }

        public override string ToString() => $"{Id}:{Index}@{Position} rot={Rotation} faction={FactionId} user={OwnerUserId}";
    }
}
