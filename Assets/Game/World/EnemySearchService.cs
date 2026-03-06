using System;
using System.Collections.Generic;
using Game.Grid;
using Game.Scripts.Fixed;
using Game.Unit;
using Game.Unit.Ability.BaseAbilities;

namespace Game.World
{
    /// <summary>
    /// Default implementation of IEnemySearchService.
    /// 
    /// Currently focuses on providing the structure + partition index maintenance.
    /// Query implementations will be filled in next iteration.
    /// </summary>
    internal sealed class EnemySearchService : IEnemySearchService
    {
        private readonly Game.Map.IMap _map;
        private readonly PartitionedEnemySearchIndex _index;
        private readonly ITargetingRules _rules;
        private readonly IOccupancyView _worldView;

        public int PartitionCellSize => _index.PartitionCellSize;

        public EnemySearchService(IOccupancyView worldView, Game.Map.IMap map, int partitionCellSize, ITargetingRules rules)
        {
            _worldView = worldView ?? throw new ArgumentNullException(nameof(worldView));
            _map = map; // can be null, but then index can't be built
            _rules = rules;

            if (_map != null)
                _index = new PartitionedEnemySearchIndex(_map, partitionCellSize);
        }

        public bool TryGetPartitionForWorldPos(FixedVector3 worldPos, out int px, out int py)
        {
            px = 0;
            py = 0;
            if (_index == null || _map == null) return false;

            var cell = _map.Grid.WorldToCell(new FixedVector2(worldPos.x, worldPos.z));
            px = cell.X / _index.PartitionCellSize;
            py = cell.Y / _index.PartitionCellSize;

            if (px < 0) px = 0;
            if (py < 0) py = 0;
            if (px >= _index.PartitionsX) px = _index.PartitionsX - 1;
            if (py >= _index.PartitionsY) py = _index.PartitionsY - 1;
            return true;
        }

        public void UpdateActorPartition(Actor actor)
        {
            if (actor == null) return;
            if (_index == null) return;

            // Prefer Occupancy's actor->cell mapping for determinism.
            if (_worldView?.Occupancy == null) return;
            if (!_worldView.Occupancy.TryGetCellOfActor(actor, out var cell))
                return;

            _index.UpdateActorPartition(actor, cell);
        }

        public void RemoveActor(Actor actor)
        {
            _index?.RemoveActor(actor);
        }

        public void Clear()
        {
            _index?.Clear();
        }

        // ---- Query interface (to be fully implemented next) ----

        public bool TryFindNearest(in EnemySearchRequest request, out EnemyCandidate enemy)
        {
            enemy = default;
            if (_index == null || _map == null) return false;
            if (request.World == null) return false;
            if (request.Self == null) return false;

            // Compute which partitions intersect the search square.
            ComputePartitionBounds(request, out var pxMin, out var pxMax, out var pyMin, out var pyMax);

            var maxDistSq = request.Range * request.Range;
            bool found = false;
            EnemyCandidate best = default;

            // Deterministic iteration: partition order (py,px) then within-bucket we still apply deterministic tie-break.
            for (int py = pyMin; py <= pyMax; py++)
            {
                for (int px = pxMin; px <= pxMax; px++)
                {
                    var bucket = _index.GetBucket(px, py);
                    if (bucket == null || bucket.Count == 0) continue;

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var other = bucket[i];
                        if (other == null) continue;
                        if (ReferenceEquals(other, request.Self)) continue;

                        // Hostility + general targeting rules
                        if (!IsHostile(request.Self, other)) continue;

                        // Alert layer filtering: guard mask vs target's layer.
                        if (request.AlertMask != UnitAlertLayer.None)
                        {
                            if (!MatchesAlertLayers(request.AlertMask, other.UnitAlertLayer))
                                continue;
                        }

                        // Resolve other position from Location ability (logic state, deterministic)
                        FixedVector3 otherPos = default;
                        bool hasPos = false;
                        foreach (var ab in other.Abilities)
                        {
                            if (ab is Location ol)
                            {
                                otherPos = ol.Position;
                                hasPos = true;
                                break;
                            }
                        }
                        if (!hasPos) continue;

                        var distSq = DistanceSqXZ(request.Origin, otherPos);
                        if (distSq.Raw > maxDistSq.Raw) continue;

                        var cand = new EnemyCandidate(other.Id, other.Faction, other.OwnerPlayerId, otherPos, distSq);
                        if (!found)
                        {
                            best = cand;
                            found = true;
                        }
                        else
                        {
                            // Deterministic compare: distance then ActorId
                            int cmp = CompareCandidate(cand, best);
                            if (cmp < 0) best = cand;
                        }
                    }
                }
            }

            if (!found) return false;
            enemy = best;
            return true;
        }

        public int FindInRange(in EnemySearchRequest request, List<EnemyCandidate> results)
        {
            if (results == null) return 0;
            // TODO: fill results deterministically
            return 0;
        }

        public bool AnyInRange(in EnemySearchRequest request)
        {
            // TODO: fast any
            return false;
        }

        // Utility: compute squared distance on XZ plane
        internal static Fixed DistanceSqXZ(FixedVector3 a, FixedVector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        internal bool IsHostile(Actor self, Actor other)
        {
            if (self == null || other == null) return false;
            if (_rules != null)
                return _rules.IsHostile(self, other) && _rules.CanTarget(self, other);
            return self.Faction != other.Faction;
        }

        internal static bool MatchesAlertLayers(UnitAlertLayer guardMask, UnitAlertLayer targetLayer)
        {
            return (guardMask & targetLayer) != 0;
        }

        internal static int CompareCandidate(in EnemyCandidate a, in EnemyCandidate b)
        {
            // distance first, then actor id for determinism
            var d = a.DistanceSq.Raw.CompareTo(b.DistanceSq.Raw);
            if (d != 0) return d;
            return a.ActorId.CompareTo(b.ActorId);
        }

        internal void ComputePartitionBounds(in EnemySearchRequest request, out int pxMin, out int pxMax, out int pyMin, out int pyMax)
        {
            pxMin = pxMax = pyMin = pyMax = 0;
            if (_index == null || _map == null) return;

            var originCell = _map.Grid.WorldToCell(new FixedVector2(request.Origin.x, request.Origin.z));

            // conservative range in cells based on map cell size
            var cellSize = _map.Grid.CellSize;
            var maxCell = cellSize.x.Raw > cellSize.y.Raw ? cellSize.x : cellSize.y;
            if (maxCell.Raw <= 0) maxCell = Fixed.FromInt(1);

            // rangeCells ~= range / cellSize
            int rangeCells = (int)(request.Range.Raw / maxCell.Raw);
            if (rangeCells < 0) rangeCells = 0;

            var minCell = new GridPosition(originCell.X - rangeCells, originCell.Y - rangeCells);
            var maxCell2 = new GridPosition(originCell.X + rangeCells, originCell.Y + rangeCells);

            _index.GetPartitionBounds(minCell, maxCell2, out pxMin, out pxMax, out pyMin, out pyMax);
        }

        internal IReadOnlyList<Actor> GetBucket(int px, int py) => _index != null ? _index.GetBucket(px, py) : Array.Empty<Actor>();
    }
}
