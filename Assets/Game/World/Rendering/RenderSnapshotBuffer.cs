using System;

namespace Game.World
{
    /// <summary>
    /// Small ring-buffer for RenderSnapshots on the main thread.
    /// Stores the last N snapshots so the renderer can interpolate between ticks.
    /// </summary>
    internal sealed class RenderSnapshotBuffer
    {
        private readonly RenderSnapshot[] _snapshots;
        private readonly float[] _arrivalTimes;
        private int _count;
        private int _head; // next write index

        public int Capacity => _snapshots.Length;
        public int Count => _count;

        public RenderSnapshotBuffer(int capacity)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be >= 2");
            _snapshots = new RenderSnapshot[capacity];
            _arrivalTimes = new float[capacity];
            _count = 0;
            _head = 0;
        }

        public void Clear()
        {
            _count = 0;
            _head = 0;
        }

        public void Add(in RenderSnapshot snapshot, float arrivalTime)
        {
            _snapshots[_head] = snapshot;
            _arrivalTimes[_head] = arrivalTime;
            _head = (_head + 1) % _snapshots.Length;
            if (_count < _snapshots.Length) _count++;
        }

        // Back-compat: if caller doesn't provide time, default to 0.
        public void Add(in RenderSnapshot snapshot)
        {
            Add(snapshot, 0f);
        }

        public bool TryGetLatest(out RenderSnapshot snapshot, out float arrivalTime)
        {
            if (_count == 0) { snapshot = default; arrivalTime = 0f; return false; }
            var idx = (_head - 1 + _snapshots.Length) % _snapshots.Length;
            snapshot = _snapshots[idx];
            arrivalTime = _arrivalTimes[idx];
            return true;
        }

        public bool TryGetLatest(out RenderSnapshot snapshot)
        {
            return TryGetLatest(out snapshot, out _);
        }

        private int IndexFromOldest(int offset)
        {
            // oldest is at head-count
            var oldest = (_head - _count + _snapshots.Length) % _snapshots.Length;
            return (oldest + offset) % _snapshots.Length;
        }

        /// <summary>
        /// Find two snapshots A and B such that A.Tick &lt;= targetTick &lt;= B.Tick.
        /// Assumes ticks are (mostly) increasing.
        /// Returns false if not enough data.
        /// </summary>
        public bool TryGetBracketingTicks(int targetTick, out RenderSnapshot a, out RenderSnapshot b)
        {
            a = default;
            b = default;
            if (_count < 2) return false;

            // Linear scan from oldest -> newest (count is small, O(N) is fine).
            RenderSnapshot prev = default;
            bool hasPrev = false;
            for (int i = 0; i < _count; i++)
            {
                var s = _snapshots[IndexFromOldest(i)];
                if (!hasPrev)
                {
                    prev = s;
                    hasPrev = true;
                    continue;
                }

                if (prev.Tick <= targetTick && targetTick <= s.Tick)
                {
                    a = prev;
                    b = s;
                    return true;
                }

                prev = s;
            }

            return false;
        }

        /// <summary>
        /// Find two snapshots A and B such that A.time &lt;= renderTime &lt;= B.time.
        /// Returns false if not found.
        /// </summary>
        public bool TryGetBracketingTimes(float renderTime, out RenderSnapshot a, out float aTime, out RenderSnapshot b, out float bTime)
        {
            a = default;
            b = default;
            aTime = 0f;
            bTime = 0f;
            if (_count < 2) return false;

            int prevIdx = -1;
            for (int i = 0; i < _count; i++)
            {
                var idx = IndexFromOldest(i);
                if (prevIdx < 0)
                {
                    prevIdx = idx;
                    continue;
                }

                var prevT = _arrivalTimes[prevIdx];
                var curT = _arrivalTimes[idx];

                if (prevT <= renderTime && renderTime <= curT)
                {
                    a = _snapshots[prevIdx];
                    b = _snapshots[idx];
                    aTime = prevT;
                    bTime = curT;
                    return true;
                }

                prevIdx = idx;
            }

            return false;
        }
    }
}
