using UnityEngine;

namespace Game.Command
{
    /// <summary>
    /// Abstract operator/controller that lives on a Unity GameObject (MonoBehaviour).
    /// Its responsibility is to observe inputs/state and emit a sequence of Commands.
    /// It does NOT execute commands directly.
    /// </summary>
    public abstract class Operator : MonoBehaviour
    {
        // Buffer owned by this operator. Consumer should read and Clear() it each tick.
        private CommandBuffer _buffer;

        protected virtual void Awake()
        {
            _buffer = new CommandBuffer();
        }

        /// <summary>
        /// Called by an external driver (e.g., a system or manager) once per tick.
        /// Implementations should Add() zero or more commands into the buffer.
        /// </summary>
        public abstract void ProduceCommands(CommandBuffer buffer);

        /// <summary>
        /// Convenience: have producers write into the operator's own buffer.
        /// </summary>
        public void ProduceCommands()
        {
            _buffer.Clear();
            ProduceCommands(_buffer);
        }

        public CommandBuffer GetBuffer() => _buffer;
    }
}
