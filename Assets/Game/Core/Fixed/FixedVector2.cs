using UnityEngine;

namespace Game.Scripts.Fixed
{
    public struct FixedVector2
    {
        public Fixed x;
        public Fixed y;

        public FixedVector2(Fixed x, Fixed y) { this.x = x; this.y = y; }

        public static FixedVector2 Zero => new FixedVector2(Fixed.Zero, Fixed.Zero);

        public static FixedVector2 operator +(FixedVector2 a, FixedVector2 b) => new FixedVector2(a.x + b.x, a.y + b.y);
        public static FixedVector2 operator -(FixedVector2 a, FixedVector2 b) => new FixedVector2(a.x - b.x, a.y - b.y);
        public static FixedVector2 operator *(FixedVector2 v, Fixed s) => new FixedVector2(v.x * s, v.y * s);
        public static FixedVector2 operator *(Fixed s, FixedVector2 v) => v * s;
        public static FixedVector2 operator /(FixedVector2 v, Fixed s) => new FixedVector2(v.x / s, v.y / s);
        public static FixedVector2 operator -(FixedVector2 v) => new FixedVector2(-v.x, -v.y);

        public Fixed Dot(FixedVector2 other) => x * other.x + y * other.y;
        public Fixed SqrMagnitude() => Dot(this);
        public Fixed Magnitude() => FixedMath.Sqrt(SqrMagnitude());

        public FixedVector2 Normalized()
        {
            var mag = Magnitude();
            if (mag == Fixed.Zero) return Zero;
            return this / mag;
        }

        public Vector2 ToUnity() => new Vector2(x.ToFloat(), y.ToFloat());
        public static FixedVector2 FromUnity(Vector2 v) => new FixedVector2(Fixed.FromFloat(v.x), Fixed.FromFloat(v.y));

        public override string ToString() => $"({x.ToFloat():F3}, {y.ToFloat():F3})";
    }
}
