using System;
using System.Collections.Generic;
using Game.Scripts.Fixed;
using Game.Unit;

namespace Game.World
{
    /// <summary>
    /// Global, deterministic enemy search service for the logic simulation.
    /// 
    /// Goals:
    /// - Pure logic (no Unity APIs)
    /// - Deterministic ordering / tie-break
    /// - Can use Occupancy + Map for efficient spatial queries
    /// 
    /// Typical use:
    /// Guard (ability) provides alert range; IdleActivity/Guard queries this service to find hostile actors in range.
    /// </summary>
    public interface IEnemySearchService
    {
        /// <summary>
        /// Finds the nearest hostile candidate around <paramref name="request"/> origin.
        /// Returns true if found.
        /// </summary>
        bool TryFindNearest(in EnemySearchRequest request, out EnemyCandidate enemy);

        /// <summary>
        /// Enumerate hostile candidates in range into a destination list.
        /// Returns number of candidates written.
        /// 
        /// Note: ordering must be deterministic.
        /// </summary>
        int FindInRange(in EnemySearchRequest request, List<EnemyCandidate> results);

        /// <summary>
        /// Fast check: does any hostile exist in range?
        /// Must be deterministic w.r.t. world state (no randomness).
        /// </summary>
        bool AnyInRange(in EnemySearchRequest request);
    }

    /// <summary>
    /// Query parameters for enemy search.
    /// Keep it pure-data so it can be created by abilities/activities.
    /// </summary>
    public readonly struct EnemySearchRequest
    {
        public readonly IOccupancyView World;
        public readonly Actor Self;
        public readonly FixedVector3 Origin;
        public readonly Fixed Range;

        /// <summary>
        /// Optional filter mask: only consider targets whose UnitAlertLayer matches this mask.
        /// Example: a ground unit's Guard mask can be Ground|LowAir.
        /// None (0) => treat as "any" (no alert-layer filtering).
        /// </summary>
        public readonly UnitAlertLayer AlertMask;

        /// <summary>
        /// If set, only actors on these movement layers are considered.
        /// This matches Navigation.MovementMask semantics.
        /// 0 => treat as "all" (implementation-defined).
        /// </summary>
        public readonly uint MovementMask;

        /// <summary>
        /// Max number of results to return for FindInRange.
        /// 0 or negative => implementation default.
        /// </summary>
        public readonly int MaxResults;

        /// <summary>
        /// Optional tick context for future rules (e.g., visibility, fog of war, reservation).
        /// </summary>
        public readonly int Tick;

        public EnemySearchRequest(
            IOccupancyView world,
            Actor self,
            FixedVector3 origin,
            Fixed range,
            UnitAlertLayer alertMask = UnitAlertLayer.None,
            uint movementMask = 0u,
            int maxResults = 0,
            int tick = 0)
        {
            World = world;
            Self = self;
            Origin = origin;
            Range = range;
            AlertMask = alertMask;
            MovementMask = movementMask;
            MaxResults = maxResults;
            Tick = tick;
        }
    }

    /// <summary>
    /// Pure-data enemy candidate.
    /// </summary>
    public readonly struct EnemyCandidate
    {
        public readonly int ActorId;
        public readonly int FactionId;
        public readonly int OwnerPlayerId;
        public readonly FixedVector3 Position;
        public readonly Fixed DistanceSq;

        public EnemyCandidate(int actorId, int factionId, int ownerPlayerId, FixedVector3 position, Fixed distanceSq)
        {
            ActorId = actorId;
            FactionId = factionId;
            OwnerPlayerId = ownerPlayerId;
            Position = position;
            DistanceSq = distanceSq;
        }
    }

    /// <summary>
    /// Targeting rule set. For now we only define the contract.
    /// Implementation can be as simple as (self.Faction != other.Faction).
    /// </summary>
    public interface ITargetingRules
    {
        bool IsHostile(Actor self, Actor other);
        bool CanTarget(Actor self, Actor other);
    }
}
