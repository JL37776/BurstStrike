namespace Game.Unit
{
    public interface IAbility
    {
        Actor Self { get; set; }

        void Init();
        void Tick();
    }

    /// <summary>
    /// Compatibility helper: provides BindActor() via extension method.
    /// (Default interface implementations may not be available depending on Unity/C# settings.)
    /// </summary>
    public static class AbilityExtensions
    {
        public static void BindActor(this IAbility ability, Actor actor)
        {
            if (ability == null) return;
            ability.Self = actor;
        }
    }
}
