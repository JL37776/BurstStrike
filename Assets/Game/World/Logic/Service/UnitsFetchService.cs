using System;
using System.Collections.Generic;
using Game.Grid;
using Game.Map;
using Game.Scripts.Fixed;
using Game.Unit;

namespace Game.World.Logic.Service
{
    /// <summary>
    /// Helper for fast unit queries on the logic thread.
    ///
    /// Contract:
    /// - This service does NOT do any gameplay validation (faction checks, visibility, hit rules, etc.).
    /// - It's intended to be used by projectiles/weapons/aoe logic to fetch candidate units.
    /// - Determinism: results are returned in a stable order (by Actor.Id then reference fallback).
    /// </summary>
    public sealed class UnitsFetchService
    {
        private readonly LogicWorld _world;

        public UnitsFetchService(LogicWorld world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public Actor GetUnitById(int id)
        {
            if (id == 0) return null;
            return _world.TryGetActorById(id, out var a) ? a : null;
        }

        public List<Actor> GetUnitsByIds(int[] ids, List<Actor> results = null)
        {
            results ??= new List<Actor>(ids != null ? ids.Length : 0);
            results.Clear();
            if (ids == null || ids.Length == 0) return results;

            for (int i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (id == 0) continue;
                if (_world.TryGetActorById(id, out var a) && a != null)
                    results.Add(a);
            }

            SortStable(results);
            return results;
        }

        public List<Actor> GetRangeUnits(FixedVector3 center, Fixed range, List<Actor> results = null)
        {
            results ??= new List<Actor>(32);
            results.Clear();

            if (_world.Map == null || _world.Occupancy == null) return results;
            if (range.Raw <= 0) return results;

            // Convert world range to grid radius (ceil).
            var map = _world.Map;
            var grid = map.Grid;
            var originCell = grid.WorldToCell(new FixedVector2(center.x, center.z));

            // Estimate cell radius from cell size.
            int cellRadius = WorldToCellRadius(grid, range);
            return GetRangeCellsUnits(originCell, cellRadius, results);
        }

        public List<Actor> GetRangeCellsUnits(GridPosition cell, int range, List<Actor> results = null)
        {
            results ??= new List<Actor>(64);
            results.Clear();

            if (_world.Map == null || _world.Occupancy == null) return results;
            if (range < 0) return results;

            var map = _world.Map;
            var occ = _world.Occupancy;

            // NOTE: Occupancy index is layer-based. For a generic fetch we search across all layers.
            // This may include duplicates if a unit occupies multiple layers; we dedupe by Actor reference.
            var seen = new HashSet<Actor>();

            int x0 = cell.X - range;
            int x1 = cell.X + range;
            int y0 = cell.Y - range;
            int y1 = cell.Y + range;

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = new GridPosition(x, y);
                if (!map.Grid.Contains(p)) continue;

                // Query per layer.
                foreach (MapLayer layer in Enum.GetValues(typeof(MapLayer)))
                {
                    if (layer == MapLayer.None || layer == MapLayer.All) continue;
                    var list = occ.GetActorsAt(layer, p);
                    if (list == null || list.Count == 0) continue;

                    for (int i = 0; i < list.Count; i++)
                    {
                        var a = list[i];
                        if (a == null) continue;
                        if (seen.Add(a))
                            results.Add(a);
                    }
                }
            }

            SortStable(results);
            return results;
        }

        private static int WorldToCellRadius(Grid2D grid, Fixed worldRange)
        {
            // grid.CellSize is FixedVector2; take X as base.
            var cs = grid.CellSize;
            var size = cs.x.Raw != 0 ? cs.x : Fixed.FromInt(1);

            // ceil(worldRange / cellSize)
            var div = worldRange / size;
            var f = div.ToFloat();
            return (int)Math.Ceiling(f);
        }

        private static void SortStable(List<Actor> actors)
        {
            if (actors == null || actors.Count <= 1) return;
            actors.Sort(static (a, b) =>
            {
                if (ReferenceEquals(a, b)) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                // Primary key: stable id if present.
                int ia = a.Id;
                int ib = b.Id;
                if (ia != 0 && ib != 0) return ia.CompareTo(ib);
                if (ia != 0) return -1;
                if (ib != 0) return 1;
                // Fallback: hashcode for deterministic-ish ordering within same run.
                return a.GetHashCode().CompareTo(b.GetHashCode());
            });
        }
    }
}
