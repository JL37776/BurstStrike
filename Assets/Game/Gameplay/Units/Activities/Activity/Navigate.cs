using System;
using System.Collections.Generic;
using Game.Pathing;
using Game.Scripts.Fixed;
using Game.Grid;
using Game.Unit.Ability.BaseAbilities;
using Game.World.Logic;

namespace Game.Unit.Activity
{
    /// <summary>
    /// Navigate activity: follows a PathResult step-by-step.
    /// Each step selects the next grid cell, checks it is walkable and not occupied, then pushes a Move activity to that cell center.
    /// </summary>
    public struct GridIndex
    {
        public int X;
        public int Y;
        public GridIndex(int x, int y) { X = x; Y = y; }
        public override string ToString() => $"({X},{Y})";
    }

    public class Navigate : IActivity
    {
        public Actor Self { get; set; }

        public GridIndex TargetCell { get; private set; }
        public FixedVector2 TargetWorld { get; private set; }

        private PathResult _path;
        private bool _isFinished;

        /// <summary>
        /// When next grid cell is blocked by a non-moving unit, and the goal is already close enough,
        /// we treat it as arrived. Manhattan distance on grid.
        /// </summary>
        public int ArriveManhattanThreshold { get; set; } = 5;

        /// <summary>
        /// When blocked and far from goal, try to replan toward an anchor further on the original path.
        /// How many cells (on the original path) we look ahead to find a reachable anchor.
        /// </summary>
        public int ReplanLookaheadCells { get; set; } = 20;

        /// <summary>
        /// Maximum number of replans per Navigate instance to avoid thrashing.
        /// </summary>
        public int MaxReplans { get; set; } = 3;

        /// <summary>
        /// How many path nodes ahead we try to aim for in world-space to keep motion continuous.
        /// 1 means per-cell (old behavior). Bigger means smoother.
        /// </summary>
        public int WaypointLookaheadCells { get; set; } = 1;

        /// <summary>
        /// When we're within this distance to a path node, we consider it consumed.
        /// Defaults to Movement.ArrivalThreshold.
        /// </summary>
        public Fixed WaypointArriveThreshold { get; set; } = Movement.ArrivalThreshold;

        /// <summary>
        /// How long (in logic ticks) we reserve the next (immediate) cell for ourselves.
        /// This prevents multiple units from committing to the same cell at the same time.
        /// </summary>
        public int CellReservationTtlTicks { get; set; } = 10;

        /// <summary>
        /// When close to the goal but the next cell is blocked, we will search for an alternative
        /// reachable free cell near the goal and re-path to it. This caps how many times it can trigger.
        /// </summary>
        public int MaxGoalFallbackRepaths { get; set; } = 2;

        public int GoalFallbackBfsMaxVisited { get; set; } = 512;

        private int _replanCount;
        private int _goalFallbackRepathCount;

        // NEW: reservation state
        private GridPosition? _reservedCell;

        private struct Reservation
        {
            public Actor Actor;
            public int ExpiresTick;
        }

        // Shared across all Navigate instances; key = world, value = cellKey->reservation
        private static readonly Dictionary<Game.World.IOccupancyView, Dictionary<int, Reservation>> _reservationsByWorld =
            new Dictionary<Game.World.IOccupancyView, Dictionary<int, Reservation>>();

        public Navigate(GridIndex targetCell, FixedVector2 targetWorld, Actor self = null, PathResult path = null)
        {
            TargetCell = targetCell;
            // NOTE: Navigate runs on the logic thread; don't call UnityEngine APIs here.
            TargetWorld = targetWorld;

            Self = self;
            _path = path;
            _isFinished = false;
            _replanCount = 0;
            _goalFallbackRepathCount = 0;
        }

        public void Start()
        {
        }

        public Fixed CornerInset { get; set; } = Fixed.FromCenti(20); // 0.20

        /// <summary>
        /// How many interpolated samples to use inside the next cell for corner smoothing.
        /// 0 disables curve sampling (falls back to single cut point).
        /// </summary>
        public int CornerSamples { get; set; } = 2;

        public void Tick()
        {
            if (_isFinished) return;
            if (Self == null) { FinishAndClearMovement(); return; }

            if (_path == null || !_path.HasPath || _path.IsComplete)
            {
                FinishAndClearMovement();
                return;
            }

            // Need a location to know where we are.
            Location loc = null;
            foreach (var ab in Self.Abilities)
            {
                if (ab is Location l) { loc = l; break; }
            }
            if (loc == null) { FinishAndClearMovement(); return; }

            // Optional Movement ability (used for speed params); Navigate owns the activity stack.
            Movement movement = null;
            foreach (var ab in Self.Abilities)
            {
                if (ab is Movement mv) { movement = mv; break; }
            }

            // If we're currently moving (Move is on top), wait until it completes.
            if (Self.Activities != null && Self.Activities.Count > 0 && Self.Activities.Peek() is Move topMove)
            {
                // If the top Move already finished this tick (arrived & cell is free), consume it now
                // so we can immediately issue the next step without a 1-tick gap.
                if (topMove.IsFinished())
                    Self.Activities.Pop();
                else
                    return;
            }

            // Determine current cell.
            var world = Self.World;
            if (world == null) { _isFinished = true; return; }
            var occ = world.Occupancy;
            if (occ == null) { _isFinished = true; return; }

            if (!occ.TryGetCellOfActor(Self, out var currentCell))
            {
                // Occupancy not ready this tick.
                return;
            }

            // movement mask for layer checks.
            uint movementMask = 1u;
            foreach (var ab in Self.Abilities)
            {
                if (ab is Unit.Ability.Navigation nav)
                {
                    movementMask = nav.MovementMask;
                    if (movementMask == 0u) movementMask = 1u;
                    break;
                }
            }

            // Determine map from the PathResult if possible.
            Game.Map.IMap map = null;
            if (_path is GridPathResult gpr) map = gpr.Map;
            // Avoid assuming IOccupancyView exposes Map on all implementations; try a safe cast to LogicWorld which has Map.
            if (map == null && world is LogicWorld lw) map = lw.Map;

            // Decide next cell.
            GridPosition? nextCell = null;

            // Flow-field path: query next cell directly.
            if (_path is FlowFieldPathResult flowPath)
            {
                var nc = flowPath.NextCell(currentCell);
                if (nc.HasValue) nextCell = nc.Value;
            }
            else
            {
                // Grid A* path: decide next cell from raw path.
                var raw = _path.RawPath;
                if (raw != null && raw.Count > 0)
                {
                    // RawPath typically starts at the start cell. We need the cell AFTER currentCell.
                    // If currentCell isn't found (e.g., occupancy/path desync), fall back to a best-effort nearest index.
                    int curIdx = -1;
                    for (int i = 0; i < raw.Count; i++)
                    {
                        var p = raw[i];
                        if (p.X == currentCell.X && p.Y == currentCell.Y)
                        {
                            curIdx = i;
                            break;
                        }
                    }

                    if (curIdx >= 0)
                    {
                        int nxt = curIdx + 1;
                        if (nxt < raw.Count)
                            nextCell = raw[nxt];
                    }
                    else
                    {
                        // Nearest-by-manhattan fallback (avoid choosing some unrelated early node).
                        int bestIdx = -1;
                        int bestD = int.MaxValue;
                        for (int i = 0; i < raw.Count; i++)
                        {
                            var p = raw[i];
                            int d = Math.Abs(p.X - currentCell.X) + Math.Abs(p.Y - currentCell.Y);
                            if (d < bestD)
                            {
                                bestD = d;
                                bestIdx = i;
                            }
                        }

                        int nxt = bestIdx + 1;
                        if (bestIdx >= 0 && nxt < raw.Count)
                            nextCell = raw[nxt];
                    }
                }
            }

            if (nextCell == null)
            {
                FinishAndClearMovement();
                return;
            }

            // Walkability check (only if we have a map).
            if (map != null)
            {
                if (!map.IsWalkable(nextCell.Value, (Game.Map.MapLayer)movementMask))
                    return;
            }

            // Occupancy handling for the immediate next cell.
            if (!TryHandleBlockedNextCell(occ, map, currentCell, nextCell.Value, movementMask))
            {
                return;
            }

            // NEW: reserve the immediate next cell so other units won't commit to it this tick.
            if (!TryReserveNextCell(world, map, nextCell.Value, movementMask))
            {
                return;
            }

            // Choose a farther waypoint to avoid "one-cell-one-stop".
            var moveTargetCell = nextCell.Value;
            if (map != null && !(_path is FlowFieldPathResult))
            {
                var raw = _path.RawPath;
                if (raw != null && raw.Count > 0)
                {
                    int curIdx = -1;
                    for (int i = 0; i < raw.Count; i++)
                    {
                        var p = raw[i];
                        if (p.X == currentCell.X && p.Y == currentCell.Y) { curIdx = i; break; }
                    }
                    if (curIdx < 0) curIdx = 0;

                    // Consume nodes that we are already very close to (helps when tick/occupancy lags).
                    var arriveThresh = WaypointArriveThreshold.Raw != 0 ? WaypointArriveThreshold : Movement.ArrivalThreshold;
                    int idx = Math.Min(curIdx + 1, raw.Count - 1);
                    while (idx < raw.Count)
                    {
                        var c2 = map.Grid.GetCellCenterWorld(raw[idx]);
                        var wp = new FixedVector3(c2.x, loc.Position.y, c2.y);
                        if ((wp - loc.Position).SqrMagnitude() <= arriveThresh * arriveThresh)
                        {
                            idx++;
                            continue;
                        }
                        break;
                    }
                    if (idx >= raw.Count) idx = raw.Count - 1;

                    int maxIdx = Math.Min(raw.Count - 1, idx + Math.Max(1, WaypointLookaheadCells) - 1);
                    // Pick the farthest walkable cell in the lookahead window.
                    for (int i = maxIdx; i >= idx; i--)
                    {
                        var cand = raw[i];
                        if (map.IsWalkable(cand, (Game.Map.MapLayer)movementMask))
                        {
                            moveTargetCell = cand;
                            break;
                        }
                    }
                }
            }

            // Next waypoint in world.
            FixedVector3 dst;
            if (map != null)
            {
                // Default: center of selected waypoint cell
                var c = map.Grid.GetCellCenterWorld(moveTargetCell);
                dst = new FixedVector3(c.x, loc.Position.y, c.y);

                // Corner smoothing (only for grid A* paths where we have RawPath)
                if (!(_path is FlowFieldPathResult) && _path.RawPath != null)
                {
                    var raw = _path.RawPath;

                    // Find current index again (cheap; raw is short). We need next and next-next.
                    int curIdx = -1;
                    for (int i = 0; i < raw.Count; i++)
                    {
                        var p = raw[i];
                        if (p.X == currentCell.X && p.Y == currentCell.Y) { curIdx = i; break; }
                    }

                    if (curIdx >= 1 && curIdx + 2 < raw.Count)
                    {
                        var prev = raw[curIdx - 1];
                        var next = raw[curIdx + 1];
                        var next2 = raw[curIdx + 2];

                        // Only smooth when we are committing to the immediate next cell.
                        if (moveTargetCell.X == next.X && moveTargetCell.Y == next.Y)
                        {
                            var dirOut = DirToFixed2(currentCell, next);
                            var dirAfter = DirToFixed2(next, next2);

                            // If direction changes at "next" (i.e., an upcoming corner), aim inside next cell towards the outgoing direction.
                            if (!IsSameDir(dirOut, dirAfter))
                            {
                                // target point = center(next) + dirAfter * inset
                                var cn = map.Grid.GetCellCenterWorld(next);
                                var inset = CornerInset;

                                var p0 = new FixedVector2(cn.x, cn.y);
                                var p1 = new FixedVector2(cn.x + dirOut.x * inset, cn.y + dirOut.y * inset);
                                var p2 = new FixedVector2(cn.x + dirAfter.x * inset, cn.y + dirAfter.y * inset);

                                FixedVector2 sample = p2;

                                int samples = CornerSamples;
                                if (samples > 0)
                                {
                                    // Pick the farthest sample this tick (closer to p2) so it looks rounder.
                                    // t values: 1/(n+1), 2/(n+1), ... n/(n+1)
                                    var denom = Fixed.FromInt(samples + 1);
                                    var t = Fixed.FromInt(samples) / denom;
                                    sample = QuadraticBezier(p0, p1, p2, t);
                                }

                                var cellOfP = map.Grid.WorldToCell(sample);
                                if (cellOfP.X == next.X && cellOfP.Y == next.Y)
                                {
                                    dst = new FixedVector3(sample.x, loc.Position.y, sample.y);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                dst = new FixedVector3(TargetWorld.x, loc.Position.y, TargetWorld.y);
            }

            // Determine speed.
            Fixed speed = Fixed.FromMilli(30);
            if (movement != null)
            {
                // Always use MaxSpeed as desired speed limit for this segment.
                // Actual per-tick speed is integrated inside Movement (movement.Speed).
                speed = movement.MaxSpeed;
                if (speed.Raw == 0) speed = Fixed.FromMilli(100);
                movement.MoveTo(dst, speed);
            }

            if (Self.Activities != null)
                Self.Activities.Push(new Move(dst, speed, Self));
        }

        private static bool IsCellFreeOfOthers(Game.World.LayeredOccupancyIndex occ, GridPosition cell, uint movementMask, Actor self)
        {
            return OccupancyUtil.IsCellFreeOfOthers(occ, cell, movementMask, self);
        }

        private static List<Actor> GetOtherActorsAt(Game.World.LayeredOccupancyIndex occ, GridPosition cell, uint movementMask, Actor self)
        {
            var result = new List<Actor>(4);
            if (occ == null) return result;
            if (movementMask == 0u) movementMask = 1u;

            uint mask = movementMask;
            uint bit = 1u;
            while (mask != 0)
            {
                if ((mask & 1u) != 0)
                {
                    var layer = (Game.Map.MapLayer)bit;
                    var list = occ.GetActorsAt(layer, cell);
                    for (int i = 0; i < list.Count; i++)
                    {
                        var a = list[i];
                        if (a == null) continue;
                        if (!OccupancyUtil.IsSameUnitActor(a, self) && !result.Contains(a))
                            result.Add(a);
                    }
                }
                mask >>= 1;
                bit <<= 1;
            }

            return result;
        }

        /// <summary>
        /// Returns true when navigation can proceed to push Move this tick.
        /// Returns false if it decided to wait/finish/replan (meaning Tick should return).
        /// </summary>
        private bool TryHandleBlockedNextCell(Game.World.LayeredOccupancyIndex occ, Game.Map.IMap map, GridPosition currentCell, GridPosition nextCell, uint movementMask)
        {
            if (occ == null) return true;

            // Fast path: free.
            if (IsCellFreeOfOthers(occ, nextCell, movementMask, Self))
                return true;

            // Inspect blockers.
            var blockers = GetOtherActorsAt(occ, nextCell, movementMask, Self);
            if (blockers.Count == 0)
                return true;

            // 1) If any blocker is currently executing Move, yield this tick (避让等待)
            for (int i = 0; i < blockers.Count; i++)
            {
                var b = blockers[i];
                if (b?.Activities != null && b.Activities.Count > 0 && b.Activities.Peek() is Move)
                {
                    return false;
                }
            }

            // 2) Not moving blockers: if goal is close enough, try to find an alternative free cell near the goal.
            var goal = new GridPosition(TargetCell.X, TargetCell.Y);
            int manhattan = Math.Abs(goal.X - currentCell.X) + Math.Abs(goal.Y - currentCell.Y);
            if (manhattan <= ArriveManhattanThreshold)
            {
                // Old behavior: stop/arrived.
                // New behavior: pick nearest free & walkable cell near the goal via BFS, then re-path to it.
                if (map == null)
                    return false;

                if (_goalFallbackRepathCount >= MaxGoalFallbackRepaths)
                    return false; // give up: will wait in place

                if (TryRepathToNearestFreeGoalNeighbor(map, occ, currentCell, goal, (Game.Map.MapLayer)movementMask, out var newPath, out var newGoal))
                {
                    _goalFallbackRepathCount++;
                    TargetCell = new GridIndex(newGoal.X, newGoal.Y);
                    TargetWorld = map.Grid.GetCellCenterWorld(newGoal);
                    _path = new GridPathResult(map, newPath);
                    return false;
                }

                // If BFS couldn't find any suitable cell (fully blocked cluster), keep waiting.
                return false;
            }

            // 3) Far: replan around the blocked cell.
            // We do NOT replan to nextCell (it's occupied). Instead, temporarily treat nextCell as blocked
            // and try to reach an anchor further along the original path (prefer goal).
            if (map == null)
                return false;

            if (!(_path is GridPathResult) || _path.RawPath == null)
                return false;

            if (_replanCount >= MaxReplans)
                return false;

            if (!TryReplanAndSplice(map, currentCell, nextCell, goal, (Game.Map.MapLayer)movementMask, _path.RawPath, out var merged))
                return false;

            _replanCount++;
            _path = new GridPathResult(map, merged);
            return false; // replanned; next tick will re-evaluate
        }

        private bool TryReplanAndSplice(Game.Map.IMap map,
            GridPosition currentCell,
            GridPosition blockedCell,
            GridPosition goalCell,
            Game.Map.MapLayer movementMask,
            IReadOnlyList<GridPosition> originalPath,
            out List<GridPosition> merged)
        {
            merged = null;
            if (map == null || originalPath == null || originalPath.Count == 0) return false;

            // Find current index on original path (best-effort: first occurrence of currentCell).
            int curIdx = -1;
            for (int i = 0; i < originalPath.Count; i++)
            {
                var p = originalPath[i];
                if (p.X == currentCell.X && p.Y == currentCell.Y)
                {
                    curIdx = i;
                    break;
                }
            }
            if (curIdx < 0) curIdx = 0;

            // If goal is on the path, try anchors from far to near (goal-first).
            // Limit lookup to a lookahead window for cost control.
            int maxIdx = Math.Min(originalPath.Count - 1, curIdx + Math.Max(1, ReplanLookaheadCells));

            // Prefer reaching the real goal if it's in the lookahead window.
            int goalIdx = -1;
            for (int i = maxIdx; i >= curIdx + 1; i--)
            {
                var p = originalPath[i];
                if (p.X == goalCell.X && p.Y == goalCell.Y)
                {
                    goalIdx = i;
                    break;
                }
            }

            int bestAnchorIdx = -1;
            if (goalIdx >= 0 && goalCell.X != blockedCell.X && goalCell.Y != blockedCell.Y && map.IsWalkable(goalCell, movementMask))
            {
                bestAnchorIdx = goalIdx;
            }
            else
            {
                // Fallback: pick the farthest walkable anchor in window (skipping the blocked cell).
                for (int i = maxIdx; i >= curIdx + 1; i--)
                {
                    var anchor = originalPath[i];
                    if (anchor.X == blockedCell.X && anchor.Y == blockedCell.Y) continue;
                    if (!map.IsWalkable(anchor, movementMask)) continue;
                    bestAnchorIdx = i;
                    break;
                }
            }

            if (bestAnchorIdx < 0)
                return false;

            var anchorCell = originalPath[bestAnchorIdx];

            // Temporarily block the occupied cell to force detour, if it was walkable.
            bool prevWalkable = map.IsWalkable(blockedCell, movementMask);
            if (prevWalkable)
                map.SetWalkable(blockedCell, movementMask, false);

            PathResult local;
            try
            {
                local = PathService.FindPathPointToPoint(map, currentCell, anchorCell, movementMask);
            }
            finally
            {
                if (prevWalkable)
                    map.SetWalkable(blockedCell, movementMask, true);
            }

            if (!(local is GridPathResult localGrid) || localGrid.RawPath == null || localGrid.RawPath.Count == 0)
                return false;

            // Merge: localPath(current->...->anchor) + originalPath(from anchorIdx onward)
            merged = new List<GridPosition>(localGrid.RawPath.Count + (originalPath.Count - bestAnchorIdx));

            for (int i = 0; i < localGrid.RawPath.Count; i++)
            {
                var p = localGrid.RawPath[i];
                if (merged.Count == 0 || merged[merged.Count - 1].X != p.X || merged[merged.Count - 1].Y != p.Y)
                    merged.Add(p);
            }

            for (int i = bestAnchorIdx; i < originalPath.Count; i++)
            {
                var p = originalPath[i];
                if (merged.Count == 0 || merged[merged.Count - 1].X != p.X || merged[merged.Count - 1].Y != p.Y)
                    merged.Add(p);
            }

            return merged.Count > 0;
        }

        private bool TryReserveNextCell(Game.World.IOccupancyView world, Game.Map.IMap map, GridPosition nextCell, uint movementMask)
        {
            if (world == null || Self == null) return false;

            // If we already reserved this same cell, keep it fresh.
            if (_reservedCell.HasValue && _reservedCell.Value.X == nextCell.X && _reservedCell.Value.Y == nextCell.Y)
            {
                TouchReservation(world, map, nextCell);
                return true;
            }

            // Otherwise clear previous reservation before reserving a new cell.
            ClearReservation(world, map);

            // If cell is reserved by someone else, treat as blocked.
            if (IsReservedByOther(world, map, nextCell))
                return false;

            // Defensive: also ensure it's currently not occupied by others.
            if (world.Occupancy != null && !IsCellFreeOfOthers(world.Occupancy, nextCell, movementMask, Self))
                return false;

            _reservedCell = nextCell;
            TouchReservation(world, map, nextCell);
            return true;
        }

        private static int CellKey(Game.Map.IMap map, GridPosition cell)
        {
            // Use map width when available; fallback to stable stride.
            int w = map != null ? map.Width : 4096;
            return cell.Y * w + cell.X;
        }

        private void TouchReservation(Game.World.IOccupancyView world, Game.Map.IMap map, GridPosition cell)
        {
            if (world == null) return;
            if (!_reservationsByWorld.TryGetValue(world, out var table))
            {
                table = new Dictionary<int, Reservation>();
                _reservationsByWorld[world] = table;
            }

            int nowTick = 0;
            if (world is LogicWorld lw) nowTick = lw.Tick;

            int key = CellKey(map, cell);
            table[key] = new Reservation { Actor = Self, ExpiresTick = nowTick + Math.Max(1, CellReservationTtlTicks) };
        }

        private bool IsReservedByOther(Game.World.IOccupancyView world, Game.Map.IMap map, GridPosition cell)
        {
            if (world == null) return false;
            if (!_reservationsByWorld.TryGetValue(world, out var table)) return false;

            int nowTick = 0;
            if (world is LogicWorld lw) nowTick = lw.Tick;

            int key = CellKey(map, cell);
            if (!table.TryGetValue(key, out var r)) return false;

            // expire
            if (r.Actor == null || r.ExpiresTick <= nowTick)
            {
                table.Remove(key);
                return false;
            }

            return !OccupancyUtil.IsSameUnitActor(r.Actor, Self);
        }

        private void ClearReservation(Game.World.IOccupancyView world, Game.Map.IMap map)
        {
            if (world == null) return;
            if (!_reservedCell.HasValue) return;

            if (_reservationsByWorld.TryGetValue(world, out var table))
            {
                int nowTick = 0;
                if (world is LogicWorld lw) nowTick = lw.Tick;

                int key = CellKey(map, _reservedCell.Value);
                if (table.TryGetValue(key, out var r))
                {
                    if (OccupancyUtil.IsSameUnitActor(r.Actor, Self) || r.ExpiresTick <= nowTick)
                        table.Remove(key);
                }
            }

            _reservedCell = null;
        }

        public void Stop()
        {
            // clear reservation when interrupted
            var w = Self?.World;
            Game.Map.IMap map = null;
            if (w is LogicWorld lw) map = lw.Map;
            ClearReservation(w, map);

            // Stop kinematics
            if (Self != null)
            {
                foreach (var ab in Self.Abilities)
                {
                    if (ab is Movement mv)
                    {
                        mv.ClearTarget();
                        break;
                    }
                }
            }

            _isFinished = true;
        }

        // Placeholder to accept a Grid-based MoveTo request
        public void MoveTo(GridIndex newTargetCell)
        {
            TargetCell = newTargetCell;
        }

        /// <summary>
        /// New overload: accept destination and a ready PathResult.
        /// </summary>
        public void MoveTo(GridIndex newTargetCell, PathResult pathResult)
        {
            TargetCell = newTargetCell;
            _path = pathResult;
            _isFinished = false;
        }

        public bool IsFinished()
        {
            if (_isFinished)
            {
                var w = Self?.World;
                Game.Map.IMap map = null;
                if (w is LogicWorld lw) map = lw.Map;
                ClearReservation(w, map);

                // Ensure Movement doesn't keep a stale target after activity ends.
                if (Self != null)
                {
                    foreach (var ab in Self.Abilities)
                    {
                        if (ab is Movement mv)
                        {
                            mv.ClearTarget();
                            break;
                        }
                    }
                }
            }
            return _isFinished;
        }

        private bool TryRepathToNearestFreeGoalNeighbor(Game.Map.IMap map,
            Game.World.LayeredOccupancyIndex occ,
            GridPosition currentCell,
            GridPosition desiredGoal,
            Game.Map.MapLayer movementMask,
            out List<GridPosition> newPath,
            out GridPosition newGoal)
        {
            newPath = null;
            newGoal = desiredGoal;
            if (map == null || occ == null) return false;

            if (!TryFindNearestFreeWalkableCellBfs(map, occ, desiredGoal, movementMask, out var found))
                return false;

            newGoal = found;
            var local = PathService.FindPathPointToPoint(map, currentCell, found, movementMask);
            if (!(local is GridPathResult g) || g.RawPath == null || g.RawPath.Count == 0)
                return false;

            newPath = new List<GridPosition>(g.RawPath);
            return true;
        }

        private bool TryFindNearestFreeWalkableCellBfs(Game.Map.IMap map,
            Game.World.LayeredOccupancyIndex occ,
            GridPosition seed,
            Game.Map.MapLayer movementMask,
            out GridPosition found)
        {
            found = seed;
            if (map == null || occ == null) return false;
            if (!map.Grid.Contains(seed)) return false;

            // If seed itself is suitable, use it.
            if (map.IsWalkable(seed, movementMask) && OccupancyUtil.IsCellFreeOfOthers(occ, seed, (uint)movementMask, Self))
            {
                found = seed;
                return true;
            }

            // Determine diagonal rules from Navigation ability.
            bool allowDiagonals = true;
            bool allowCornerCutting = false;
            foreach (var ab in Self.Abilities)
            {
                if (ab is Unit.Ability.Navigation nav)
                {
                    allowDiagonals = nav.AllowDiagonals;
                    allowCornerCutting = nav.AllowCornerCutting;
                    break;
                }
            }

            var q = new Queue<GridPosition>();
            var visited = new HashSet<int>();
            q.Enqueue(seed);
            visited.Add(seed.Y * map.Width + seed.X);

            int maxVisited = GoalFallbackBfsMaxVisited <= 0 ? 512 : GoalFallbackBfsMaxVisited;

            while (q.Count > 0 && visited.Count <= maxVisited)
            {
                var p = q.Dequeue();

                foreach (var n in map.GetNeighbors(p, allowDiagonals, allowCornerCutting, movementMask))
                {
                    int key = n.Y * map.Width + n.X;
                    if (!visited.Add(key))
                        continue;

                    // n is already walkable by GetNeighbors; now check occupancy
                    if (OccupancyUtil.IsCellFreeOfOthers(occ, n, (uint)movementMask, Self))
                    {
                        found = n;
                        return true;
                    }

                    q.Enqueue(n);
                    if (visited.Count > maxVisited)
                        break;
                }
            }

            return false;
        }

        private void FinishAndClearMovement()
        {
            // Clear reservations and stop movement so we don't drift.
            var w = Self?.World;
            Game.Map.IMap map = null;
            if (w is LogicWorld lw) map = lw.Map;
            ClearReservation(w, map);

            if (Self != null)
            {
                foreach (var ab in Self.Abilities)
                {
                    if (ab is Movement mv)
                    {
                        mv.ClearTarget();
                        break;
                    }
                }
            }

            _isFinished = true;
        }

        private static FixedVector2 DirToFixed2(GridPosition from, GridPosition to)
        {
            int dx = to.X - from.X;
            int dy = to.Y - from.Y;
            // clamp to -1..1
            if (dx > 1) dx = 1; else if (dx < -1) dx = -1;
            if (dy > 1) dy = 1; else if (dy < -1) dy = -1;
            return new FixedVector2(Fixed.FromInt(dx), Fixed.FromInt(dy));
        }

        private static bool IsSameDir(in FixedVector2 a, in FixedVector2 b)
        {
            return a.x.Raw == b.x.Raw && a.y.Raw == b.y.Raw;
        }


        private static FixedVector2 QuadraticBezier(in FixedVector2 p0, in FixedVector2 p1, in FixedVector2 p2, Fixed t)
        {
            // (1-t)^2 p0 + 2(1-t)t p1 + t^2 p2
            var oneMinus = Fixed.One - t;
            var a = oneMinus * oneMinus;
            var b = Fixed.Two * oneMinus * t;
            var c = t * t;
            return new FixedVector2(p0.x * a + p1.x * b + p2.x * c, p0.y * a + p1.y * b + p2.y * c);
        }
    }
}
