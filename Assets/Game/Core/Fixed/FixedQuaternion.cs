using UnityEngine;

namespace Game.Scripts.Fixed
{
    public struct FixedQuaternion
    {
        public Fixed x;
        public Fixed y;
        public Fixed z;
        public Fixed w;

        public FixedQuaternion(Fixed x, Fixed y, Fixed z, Fixed w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        public static FixedQuaternion Identity => new FixedQuaternion(Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.One);

        public static FixedQuaternion FromAxisAngle(FixedVector3 axis, Fixed angleRadians)
        {
            var half = angleRadians / Fixed.Two;
            var s = FixedMath.Sin(half);
            var c = FixedMath.Cos(half);
            var naxis = axis.Normalized();
            return new FixedQuaternion(naxis.x * s, naxis.y * s, naxis.z * s, c);
        }

        public FixedQuaternion Normalized()
        {
            // compute magnitude
            var mag2 = x * x + y * y + z * z + w * w;
            var mag = FixedMath.Sqrt(mag2);
            if (mag == Fixed.Zero) return Identity;
            return new FixedQuaternion(x / mag, y / mag, z / mag, w / mag);
        }

        public static FixedQuaternion operator *(FixedQuaternion a, FixedQuaternion b)
        {
            // q = a * b
            var x = a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y;
            var y = a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x;
            var z = a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w;
            var w = a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z;
            return new FixedQuaternion(x, y, z, w);
        }

        public Vector4 ToUnityVector4() => new Vector4(x.ToFloat(), y.ToFloat(), z.ToFloat(), w.ToFloat());
        public Quaternion ToUnity() => new Quaternion(x.ToFloat(), y.ToFloat(), z.ToFloat(), w.ToFloat());

        public override string ToString() => $"({x.ToFloat():F3}, {y.ToFloat():F3}, {z.ToFloat():F3}, {w.ToFloat():F3})";

        public static Fixed Dot(FixedQuaternion a, FixedQuaternion b)
            => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

        public static FixedQuaternion Negate(FixedQuaternion q)
            => new FixedQuaternion(-q.x, -q.y, -q.z, -q.w);

        public static FixedQuaternion Lerp(FixedQuaternion a, FixedQuaternion b, Fixed t)
        {
            // clamp t [0,1]
            if (t.Raw <= 0) return a;
            if (t.Raw >= Fixed.One.Raw) return b;
            var x = a.x + (b.x - a.x) * t;
            var y = a.y + (b.y - a.y) * t;
            var z = a.z + (b.z - a.z) * t;
            var w = a.w + (b.w - a.w) * t;
            return new FixedQuaternion(x, y, z, w).Normalized();
        }

        /// <summary>
        /// Spherical linear interpolation (shortest arc).
        /// Uses lerp+normalize when quaternions are very close.
        /// </summary>
        public static FixedQuaternion Slerp(FixedQuaternion a, FixedQuaternion b, Fixed t)
        {
            if (t.Raw <= 0) return a;
            if (t.Raw >= Fixed.One.Raw) return b;

            var dot = Dot(a, b);
            // If dot < 0, negate b to take shortest path.
            if (dot.Raw < 0)
            {
                b = Negate(b);
                dot = -dot;
            }

            // Clamp dot to [0,1]
            if (dot.Raw > Fixed.One.Raw) dot = Fixed.One;
            if (dot.Raw < 0) dot = Fixed.Zero;

            // If very close, fallback to lerp to avoid division by tiny numbers.
            // threshold ~ 0.9995
            var close = Fixed.FromMilli(999); // 0.999
            if (dot >= close)
                return Lerp(a, b, t);

            // theta0 = acos(dot)
            // Use System.Math directly here to avoid dependency on FixedMath.Acos in analyzers.
            var theta0 = Fixed.FromDouble(System.Math.Acos(dot.ToDouble()));
            var sinTheta0 = FixedMath.Sin(theta0);
            if (sinTheta0.Raw == 0)
                return Lerp(a, b, t);

            var theta = theta0 * t;
            var sinTheta = FixedMath.Sin(theta);

            var s0 = FixedMath.Cos(theta) - dot * sinTheta / sinTheta0;
            var s1 = sinTheta / sinTheta0;

            var x = a.x * s0 + b.x * s1;
            var y = a.y * s0 + b.y * s1;
            var z = a.z * s0 + b.z * s1;
            var w = a.w * s0 + b.w * s1;
            return new FixedQuaternion(x, y, z, w).Normalized();
        }
    }
}
