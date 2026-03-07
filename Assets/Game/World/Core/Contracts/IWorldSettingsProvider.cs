namespace Game.World
{
    /// <summary>
    /// Optional settings provider that can override World startup settings.
    /// Implementations must be side-effect free: only mutate the settings struct.
    /// </summary>
    public interface IWorldSettingsProvider
    {
        /// <summary>
        /// Higher wins. Used to create deterministic override order when multiple providers exist.
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Override startup settings. Must not start threads, create Unity objects, or call Unity APIs
        /// other than reading fields.
        /// </summary>
        void Mutate(ref WorldSettings settings);
    }
}
