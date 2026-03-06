using UnityEngine;

namespace Game.Scripts.Fixed
{
    public struct FixedVector3
    {
        public Fixed x;
        public Fixed y;
        public Fixed z;

        public FixedVector3(Fixed x, Fixed y, Fixed z) { this.x = x; this.y = y; this.z = z; }

        public static FixedVector3 Zero => new FixedVector3(Fixed.Zero, Fixed.Zero, Fixed.Zero);

        public static FixedVector3 operator +(FixedVector3 a, FixedVector3 b) => new FixedVector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static FixedVector3 operator -(FixedVector3 a, FixedVector3 b) => new FixedVector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static FixedVector3 operator -(FixedVector3 a) => new FixedVector3(-a.x, -a.y, -a.z);
        public static FixedVector3 operator *(FixedVector3 v, Fixed s) => new FixedVector3(v.x * s, v.y * s, v.z * s);
        public static FixedVector3 operator *(Fixed s, FixedVector3 v) => v * s;
        public static FixedVector3 operator /(FixedVector3 v, Fixed s) => new FixedVector3(v.x / s, v.y / s, v.z / s);

        public Fixed Dot(FixedVector3 o) => x * o.x + y * o.y + z * o.z;
        public FixedVector3 Cross(FixedVector3 o)
            => new FixedVector3(y * o.z - z * o.y, z * o.x - x * o.z, x * o.y - y * o.x);

        public Fixed SqrMagnitude() => Dot(this);
        public Fixed Magnitude() => FixedMath.Sqrt(SqrMagnitude());

        public FixedVector3 Normalized()
        {
            var m = Magnitude();
            if (m == Fixed.Zero) return Zero;
            return this / m;
        }

        public Vector3 ToUnity() => new Vector3(x.ToFloat(), y.ToFloat(), z.ToFloat());
        public static FixedVector3 FromUnity(Vector3 v) => new FixedVector3(Fixed.FromFloat(v.x), Fixed.FromFloat(v.y), Fixed.FromFloat(v.z));

        public override string ToString() => $"({x.ToFloat():F3}, {y.ToFloat():F3}, {z.ToFloat():F3})";
    }
}
