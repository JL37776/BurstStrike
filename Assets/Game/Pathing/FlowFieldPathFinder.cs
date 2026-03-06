using System;
using System.Collections.Generic;
using Game.Map;
using Game.Grid;
using Game.Scripts.Fixed;

namespace Game.Pathing
{
    /// <summary>
    /// Build a flow-field for a single goal on a Map. Produces a FlowFieldPathResult which can be shared among agents.
    /// </summary>
    public class FlowFieldPathFinder
    {
        public FlowFieldPathResult Build(IMap map, GridPosition goalCell, MapLayer movementMask)
        {
            int w = map.Width;
            int h = map.Height;
            int size = w * h;
            var dist = new int[size];
            var next = new GridPosition[size];
            var inf = int.MaxValue / 4;

            for (int i = 0; i < size; i++)
            {
                dist[i] = inf;
                next[i] = new GridPosition(int.MinValue, int.MinValue);
            }

            if (!map.Grid.Contains(goalCell) || !map.IsWalkable(goalCell, movementMask))
                return new FlowFieldPathResult(map, next, w, h); // empty/unreachable

            // Dijkstra (0-1 weights not necessary, use uniform cost). We'll use simple priority queue.
            var pq = new SimplePriorityQueue();
            int goalIdx = goalCell.Y * w + goalCell.X;
            dist[goalIdx] = 0;
            // Goal should point to itself so following next pointers can terminate.
            next[goalIdx] = goalCell;
            pq.Enqueue(goalIdx, 0);

            var neighborOffsets = new (int dx, int dy, int cost)[] {
                (-1,0,10),(1,0,10),(0,-1,10),(0,1,10),
                (-1,-1,14),(-1,1,14),(1,-1,14),(1,1,14)
            };

            while (pq.Count > 0)
            {
                var cur = pq.Dequeue();
                int cx = cur % w;
                int cy = cur / w;
                var cpos = new GridPosition(cx, cy);
                int cd = dist[cur];

                // explore neighbors
                foreach (var n in neighborOffsets)
                {
                    int nx = cx + n.dx;
                    int ny = cy + n.dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    var np = new GridPosition(nx, ny);
                    if (!map.IsWalkable(np, movementMask)) continue;

                    // prevent corner cutting: require adjacent orthogonals if diagonal
                    if (Math.Abs(n.dx) + Math.Abs(n.dy) == 2)
                    {
                        var n1 = new GridPosition(cx + n.dx, cy);
                        var n2 = new GridPosition(cx, cy + n.dy);
                        if (!(map.IsWalkable(n1, movementMask) && map.IsWalkable(n2, movementMask))) continue;
                    }

                    int ni = ny * w + nx;
                    int nd = cd + n.cost;
                    if (nd < dist[ni])
                    {
                        dist[ni] = nd;
                        // next for np should point to the neighbor on the path toward goal, which from np is cpos (we set reverse link)
                        next[ni] = cpos;
                        pq.EnqueueOrUpdate(ni, nd);
                    }
                }
            }

            return new FlowFieldPathResult(map, next, w, h);
        }

        // tiny binary heap for ints
        private class SimplePriorityQueue
        {
            private List<(int idx, int pr)> _data = new List<(int, int)>();
            private Dictionary<int, int> _positions = new Dictionary<int, int>();
            public int Count => _data.Count;

            public void Enqueue(int idx, int pr)
            {
                _data.Add((idx, pr));
                int ci = _data.Count - 1;
                _positions[idx] = ci;
                HeapUp(ci);
            }

            public void EnqueueOrUpdate(int idx, int pr)
            {
                if (_positions.TryGetValue(idx, out int pos))
                {
                    var old = _data[pos];
                    _data[pos] = (idx, pr);
                    if (pr < old.pr) HeapUp(pos); else HeapDown(pos);
                }
                else Enqueue(idx, pr);
            }

            public int Dequeue()
            {
                var ret = _data[0].idx;
                var last = _data[_data.Count - 1];
                _data[0] = last;
                _positions[last.idx] = 0;
                _data.RemoveAt(_data.Count - 1);
                _positions.Remove(ret);
                if (_data.Count > 0) HeapDown(0);
                return ret;
            }

            private void HeapUp(int i)
            {
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (_data[p].pr <= _data[i].pr) break;
                    Swap(i, p);
                    i = p;
                }
            }

            private void HeapDown(int i)
            {
                int n = _data.Count;
                while (true)
                {
                    int l = i * 2 + 1;
                    int r = l + 1;
                    int smallest = i;
                    if (l < n && _data[l].pr < _data[smallest].pr) smallest = l;
                    if (r < n && _data[r].pr < _data[smallest].pr) smallest = r;
                    if (smallest == i) break;
                    Swap(i, smallest);
                    i = smallest;
                }
            }

            private void Swap(int a, int b)
            {
                var ta = _data[a];
                var tb = _data[b];
                _data[a] = tb;
                _data[b] = ta;
                _positions[ta.idx] = b;
                _positions[tb.idx] = a;
            }
        }
    }
}
