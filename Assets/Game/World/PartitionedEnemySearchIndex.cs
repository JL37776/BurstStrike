using System;
using System.Collections.Generic;
using Game.Grid;
using Game.Unit;

namespace Game.World
{
    /// <summary>
    /// Coarse spatial index for enemy/target searches.
    /// Partitions the map grid into fixed-size blocks (e.g., 5x5 cells).
    /// 
    /// Determinism:
    /// - Buckets store actors without assuming iteration order.
    /// - Consumer (EnemySearchService) must apply deterministic tie-break (distance, then ActorId).
    /// </summary>
    internal sealed class PartitionedEnemySearchIndex
    {
        private readonly int _partitionCellSize;
        private readonly int _mapWidth;
        private readonly int _mapHeight;
        private readonly int _partitionsX;
        private readonly int _partitionsY;

        // partitionIndex -> list of actors currently in this partition
        private readonly List<Actor>[] _buckets;

        // actor -> previous partitionIndex
        private readonly Dictionary<Actor, int> _actorPartition = new Dictionary<Actor, int>(256);

        public int PartitionCellSize => _partitionCellSize;
        public int PartitionsX => _partitionsX;
        public int PartitionsY => _partitionsY;

        public PartitionedEnemySearchIndex(Game.Map.IMap map, int partitionCellSize)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            _mapWidth = map.Width;
            _mapHeight = map.Height;
            _partitionCellSize = Math.Max(5, partitionCellSize);

            _partitionsX = Math.Max(1, (_mapWidth + _partitionCellSize - 1) / _partitionCellSize);
            _partitionsY = Math.Max(1, (_mapHeight + _partitionCellSize - 1) / _partitionCellSize);

            _buckets = new List<Actor>[_partitionsX * _partitionsY];
            for (int i = 0; i < _buckets.Length; i++)
                _buckets[i] = new List<Actor>(8);
        }

        public void Clear()
        {
            for (int i = 0; i < _buckets.Length; i++)
                _buckets[i].Clear();

            _actorPartition.Clear();
        }

        public void RemoveActor(Actor actor)
        {
            if (actor == null) return;
            if (!_actorPartition.TryGetValue(actor, out var idx)) return;

            var bucket = _buckets[idx];
            for (int i = 0; i < bucket.Count; i++)
            {
                if (ReferenceEquals(bucket[i], actor))
                {
                    int last = bucket.Count - 1;
                    bucket[i] = bucket[last];
                    bucket.RemoveAt(last);
                    break;
                }
            }

            _actorPartition.Remove(actor);
        }

        public void UpdateActorPartition(Actor actor, GridPosition cell)
        {
            if (actor == null) return;

            int newIdx = ToPartitionIndex(cell);

            if (_actorPartition.TryGetValue(actor, out var prevIdx))
            {
                if (prevIdx == newIdx) return;

                // remove from previous bucket
                var prevBucket = _buckets[prevIdx];
                for (int i = 0; i < prevBucket.Count; i++)
                {
                    if (ReferenceEquals(prevBucket[i], actor))
                    {
                        int last = prevBucket.Count - 1;
                        prevBucket[i] = prevBucket[last];
                        prevBucket.RemoveAt(last);
                        break;
                    }
                }
            }

            _buckets[newIdx].Add(actor);
            _actorPartition[actor] = newIdx;
        }

        public IReadOnlyList<Actor> GetBucket(int px, int py)
        {
            if (px < 0 || py < 0 || px >= _partitionsX || py >= _partitionsY)
                return Array.Empty<Actor>();
            return _buckets[py * _partitionsX + px];
        }

        public void GetPartitionBounds(GridPosition minCell, GridPosition maxCell, out int pxMin, out int pxMax, out int pyMin, out int pyMax)
        {
            // clamp to map bounds
            if (minCell.X < 0) minCell = new GridPosition(0, minCell.Y);
            if (minCell.Y < 0) minCell = new GridPosition(minCell.X, 0);
            if (maxCell.X >= _mapWidth) maxCell = new GridPosition(_mapWidth - 1, maxCell.Y);
            if (maxCell.Y >= _mapHeight) maxCell = new GridPosition(maxCell.X, _mapHeight - 1);

            pxMin = minCell.X / _partitionCellSize;
            pyMin = minCell.Y / _partitionCellSize;
            pxMax = maxCell.X / _partitionCellSize;
            pyMax = maxCell.Y / _partitionCellSize;

            if (pxMin < 0) pxMin = 0;
            if (pyMin < 0) pyMin = 0;
            if (pxMax >= _partitionsX) pxMax = _partitionsX - 1;
            if (pyMax >= _partitionsY) pyMax = _partitionsY - 1;
        }

        private int ToPartitionIndex(GridPosition cell)
        {
            int px = cell.X / _partitionCellSize;
            int py = cell.Y / _partitionCellSize;

            if (px < 0) px = 0;
            else if (px >= _partitionsX) px = _partitionsX - 1;

            if (py < 0) py = 0;
            else if (py >= _partitionsY) py = _partitionsY - 1;

            return py * _partitionsX + px;
        }
    }
}
