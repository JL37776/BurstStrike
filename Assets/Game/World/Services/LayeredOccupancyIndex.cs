using System;
using System.Collections.Generic;
using Game.Grid;
using Game.Map;
using Game.Scripts.Fixed;
using Game.Unit;
using Game.Unit.Ability.BaseAbilities;

namespace Game.World
{
    /// <summary>
    /// Layered occupancy index: maintains maps from (layer, cellIndex) -> list of Actors and from Actor -> last known cell.
    /// Call Update(...) once per tick with the current actor list and a reference map to rebuild the index.
    /// Designed for fast lookups: GetActorsAt(layer, gridPos) and TryGetCellOfActor(actor).
    /// </summary>
    public class LayeredOccupancyIndex
    {
        // layer -> (cellIndex -> list of actors)
        private readonly Dictionary<global::Game.Map.MapLayer, Dictionary<int, List<Actor>>> _byLayer = new Dictionary<global::Game.Map.MapLayer, Dictionary<int, List<Actor>>>();

        // actor -> last cell mapping
        private readonly Dictionary<Actor, global::Game.Grid.GridPosition> _actorCell = new Dictionary<Actor, global::Game.Grid.GridPosition>();

        // actor -> last movement layers mask (as uint bits, same semantics as Navigation.MovementMask)
        private readonly Dictionary<Actor, uint> _actorLayers = new Dictionary<Actor, uint>();

        private int _mapWidth = 0;
        private global::Game.Map.IMap _map;

        public LayeredOccupancyIndex()
        {
        }

        /// <summary>
        /// Initialize index with a map reference for incremental updates.
        /// You can still call Update(...) to rebuild from scratch.
        /// </summary>
        public void SetMap(global::Game.Map.IMap map)
        {
            _map = map;
            _mapWidth = map != null ? map.Width : 0;
        }

        /// <summary>
        /// Rebuild the occupancy index from the given actor list and map.
        /// This is an O(n) pass where n = number of actors with Location ability.
        /// </summary>
        public void Update(IList<Actor> actors, global::Game.Map.IMap map)
        {
            if (actors == null) throw new ArgumentNullException(nameof(actors));
            if (map == null) throw new ArgumentNullException(nameof(map));

            SetMap(map);

            _byLayer.Clear();
            _actorCell.Clear();
            _actorLayers.Clear();

            for (int i = 0; i < actors.Count; i++)
            {
                var a = actors[i];
                if (a == null) continue;

                if (!TryGetActorState(a, map, out var gp, out var movementMask))
                    continue;

                _actorCell[a] = gp;
                _actorLayers[a] = movementMask;

                int cellIndex = gp.Y * _mapWidth + gp.X;
                AddToLayers(a, movementMask, cellIndex);
            }
        }

        /// <summary>
        /// Incrementally update occupancy for a single actor (after it ticks).
        /// This is O(k) where k = number of movement layers set (usually small).
        /// </summary>
        public void UpdateActor(Actor actor)
        {
            if (actor == null) return;
            if (_map == null || _mapWidth <= 0) return;

            if (!TryGetActorState(actor, _map, out var newCell, out var newMask))
            {
                // Actor has no location anymore => remove any previous occupancy.
                RemoveActor(actor);
                return;
            }

            bool hadPrev = _actorCell.TryGetValue(actor, out var prevCell);
            _actorLayers.TryGetValue(actor, out var prevMask);

            // Fast path: nothing changed
            if (hadPrev && prevCell.X == newCell.X && prevCell.Y == newCell.Y && prevMask == newMask)
                return;

            if (hadPrev)
            {
                int prevIndex = prevCell.Y * _mapWidth + prevCell.X;
                RemoveFromLayers(actor, prevMask, prevIndex);
            }

            int newIndex = newCell.Y * _mapWidth + newCell.X;
            AddToLayers(actor, newMask, newIndex);

            _actorCell[actor] = newCell;
            _actorLayers[actor] = newMask;
        }

        /// <summary>
        /// Remove an actor from the occupancy index.
        /// </summary>
        public void RemoveActor(Actor actor)
        {
            if (actor == null) return;
            if (_mapWidth <= 0) return;

            if (_actorCell.TryGetValue(actor, out var cell))
            {
                _actorLayers.TryGetValue(actor, out var mask);
                int cellIndex = cell.Y * _mapWidth + cell.X;
                RemoveFromLayers(actor, mask, cellIndex);
            }

            _actorCell.Remove(actor);
            _actorLayers.Remove(actor);
        }

        private static bool TryGetActorState(Actor a, global::Game.Map.IMap map, out global::Game.Grid.GridPosition cell, out uint movementMask)
        {
            cell = default;
            movementMask = 0u;

            if (a == null || map == null) return false;

            global::Game.Scripts.Fixed.FixedVector3? pos = null;
            foreach (var ab in a.Abilities)
            {
                if (pos == null && ab is Location l) pos = l.Position;
                if (movementMask == 0u && ab is Game.Unit.Ability.Navigation nav) movementMask = nav.MovementMask;
                if (pos != null && movementMask != 0u) break;
            }

            if (pos == null) return false; // no location -> not indexable

            // default movement mask: foot (bit 0)
            if (movementMask == 0u) movementMask = 1u;

            // Ground plane is X/Z (Y is height).
            var world2 = new global::Game.Scripts.Fixed.FixedVector2(pos.Value.x, pos.Value.z);
            cell = map.Grid.WorldToCell(world2);
            return true;
        }

        private void AddToLayers(Actor actor, uint movementMask, int cellIndex)
        {
            uint mask = movementMask;
            uint bit = 1u;
            while (mask != 0)
            {
                if ((mask & 1u) != 0)
                {
                    global::Game.Map.MapLayer layer = (global::Game.Map.MapLayer)bit;
                    if (!_byLayer.TryGetValue(layer, out var cellMap))
                    {
                        cellMap = new Dictionary<int, List<Actor>>();
                        _byLayer[layer] = cellMap;
                    }

                    if (!cellMap.TryGetValue(cellIndex, out var list))
                    {
                        list = new List<Actor>(4);
                        cellMap[cellIndex] = list;
                    }

                    list.Add(actor);
                }

                mask >>= 1;
                bit <<= 1;
            }
        }

        private void RemoveFromLayers(Actor actor, uint movementMask, int cellIndex)
        {
            if (movementMask == 0u) movementMask = 1u;

            uint mask = movementMask;
            uint bit = 1u;
            while (mask != 0)
            {
                if ((mask & 1u) != 0)
                {
                    global::Game.Map.MapLayer layer = (global::Game.Map.MapLayer)bit;
                    if (_byLayer.TryGetValue(layer, out var cellMap) && cellMap.TryGetValue(cellIndex, out var list))
                    {
                        // Remove the actor from the bucket.
                        // List is expected small; linear remove is fine and avoids allocations.
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (ReferenceEquals(list[i], actor))
                            {
                                int last = list.Count - 1;
                                list[i] = list[last];
                                list.RemoveAt(last);
                                break;
                            }
                        }

                        // Optional cleanup to keep dictionaries compact
                        if (list.Count == 0)
                            cellMap.Remove(cellIndex);
                    }
                }

                mask >>= 1;
                bit <<= 1;
            }
        }

        /// <summary>
        /// Get actors at the given grid position for the specified layer. Returns empty list if none.
        /// </summary>
        public IReadOnlyList<Actor> GetActorsAt(global::Game.Map.MapLayer layer, global::Game.Grid.GridPosition gridPos)
        {
            if (!_byLayer.TryGetValue(layer, out var cellMap)) return Array.Empty<Actor>();
            if (_mapWidth <= 0) return Array.Empty<Actor>();
            int cellIndex = gridPos.Y * _mapWidth + gridPos.X;
            if (!cellMap.TryGetValue(cellIndex, out var list)) return Array.Empty<Actor>();
            return list.AsReadOnly();
        }

        /// <summary>
        /// Get actors at the given cellIndex for the specified layer. Use this when you already computed the cell index.
        /// </summary>
        public IReadOnlyList<Actor> GetActorsAtByIndex(global::Game.Map.MapLayer layer, int cellIndex)
        {
            if (!_byLayer.TryGetValue(layer, out var cellMap)) return Array.Empty<Actor>();
            if (!cellMap.TryGetValue(cellIndex, out var list)) return Array.Empty<Actor>();
            return list.AsReadOnly();
        }

        /// <summary>
        /// Try get the last known cell for an actor.
        /// </summary>
        public bool TryGetCellOfActor(Actor actor, out Game.Grid.GridPosition cell)
        {
            return _actorCell.TryGetValue(actor, out cell);
        }
    }
}
