using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Game.World.Logic
{
    // Minimal, dependency-free test harness (used by edit-mode or custom runners).
    // If your project already uses NUnit/UnityTestFramework, we can convert this into proper tests.
    internal static class LogicWorldTests
    {
        private sealed class Record : ILogicCommand
        {
            private readonly List<int> _log;
            private readonly int _value;
            public Record(List<int> log, int value) { _log = log; _value = value; }
            public void Execute(LogicWorld world) => _log.Add(_value);
        }

        private readonly struct EnqueueCmdInput : ILogicInput
        {
            private readonly ILogicCommand _cmd;
            public EnqueueCmdInput(ILogicCommand cmd) { _cmd = cmd; }
            public void Apply(LogicWorld world) => world.EnqueueCommand(_cmd);
        }

        /// <summary>
        /// Verifies: inputs can enqueue commands, and commands execute before actor tick in the same tick.
        /// (We can't easily assert actor tick order without altering Actor; for now we assert command FIFO.)
        /// </summary>
        public static void RunSmoke()
        {
            var inQ = new ConcurrentQueue<ILogicInput>();
            var outQ = new ConcurrentQueue<ILogicOutput>();
            var w = new LogicWorld(30, inQ, outQ);

            var log = new List<int>();
            inQ.Enqueue(new EnqueueCmdInput(new Record(log, 1)));
            inQ.Enqueue(new EnqueueCmdInput(new Record(log, 2)));
            inQ.Enqueue(new EnqueueCmdInput(new Record(log, 3)));

            w.TickOnce();

            if (log.Count != 3 || log[0] != 1 || log[1] != 2 || log[2] != 3)
                throw new System.Exception("LogicWorld command FIFO execution failed.");
        }
    }
}
