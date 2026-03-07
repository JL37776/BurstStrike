using System.Collections.Generic;

namespace Game.Command
{
    /// <summary>
    /// Simple command buffer for one producer (Operator) and one consumer (gameplay system).
    /// Backed by a List to avoid per-command allocations.
    /// </summary>
    public sealed class CommandBuffer
    {
        private readonly List<Command> _commands;

        public CommandBuffer(int initialCapacity = 16)
        {
            _commands = new List<Command>(initialCapacity);
        }

        public int Count => _commands.Count;

        public void Clear() => _commands.Clear();

        public void Add(in Command cmd) => _commands.Add(cmd);

        public List<Command> AsList() => _commands;
    }
}
