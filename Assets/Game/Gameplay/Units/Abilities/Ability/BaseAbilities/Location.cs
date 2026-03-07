using Game.Scripts.Fixed;

namespace Game.Unit.Ability.BaseAbilities
{
    public class Location : IAbility
    {
        // Use fixed-point position for logic/physics. Converted to Unity Vector3 only for rendering.
        public FixedVector3 Position { get; set; }

        // Fixed-point rotation (3D). Rendering/steering can read/write this.
        public FixedQuaternion Rotation { get; set; }

        public Actor Self { get; set; }

        public void Init()
        {
            // Default to origin; can be overridden by spawn data.
            if (Position.Equals(default(FixedVector3)))
                Position = FixedVector3.Zero;

            // Default facing.
            if (Rotation.w.Raw == 0 && Rotation.x.Raw == 0 && Rotation.y.Raw == 0 && Rotation.z.Raw == 0)
                Rotation = FixedQuaternion.Identity;
        }

        public void Tick()
        {
            // Location is passive; nothing to do per tick by default.
        }
    }
}