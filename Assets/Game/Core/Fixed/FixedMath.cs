using System;

namespace Game.Scripts.Fixed
{
    public static class FixedConfig
    {
        // If true, arithmetic operations saturate on overflow instead of throwing.
        public static bool UseSaturatingArithmetic = true;

        // Trig provider: can be replaced at runtime.
        public static ITrigProvider TrigProvider = new DefaultTrigProvider();
    }

    public interface ITrigProvider
    {
        Fixed Sin(Fixed angle);
        Fixed Cos(Fixed angle);
        Fixed Atan2(Fixed y, Fixed x);
        // optional fast combined Sin/Cos; default implementation may call Sin and Cos separately
    }

    // Default implementation using System.Math (double) for accuracy and simplicity.
    public class DefaultTrigProvider : ITrigProvider
    {
        public Fixed Sin(Fixed angle)
        {
            double r = Math.Sin(angle.ToDouble());
            return Fixed.FromDouble(r);
        }

        public Fixed Cos(Fixed angle)
        {
            double r = Math.Cos(angle.ToDouble());
            return Fixed.FromDouble(r);
        }

        public Fixed Atan2(Fixed y, Fixed x)
        {
            double r = Math.Atan2(y.ToDouble(), x.ToDouble());
            return Fixed.FromDouble(r);
        }
    }

    // Optional LUT-based provider for performance. Uses a sin table with linear interpolation.
    public class TableTrigProvider : ITrigProvider
    {
        readonly Fixed[] sinTable;
        readonly int mask;
        readonly int size;
        readonly double invSize; // mapping radians to index

        // size must be power of two
        public TableTrigProvider(int sizePowerOfTwo)
        {
            if (sizePowerOfTwo <= 0 || (sizePowerOfTwo & (sizePowerOfTwo - 1)) != 0)
                throw new ArgumentException("sizePowerOfTwo must be power of two and > 0");

            size = sizePowerOfTwo;
            mask = size - 1;
            sinTable = new Fixed[size];
            // fill table for [0, 2PI)
            for (int i = 0; i < size; i++)
            {
                double angle = (i / (double)size) * Math.PI * 2.0;
                sinTable[i] = Fixed.FromDouble(Math.Sin(angle));
            }

            invSize = size / (2.0 * Math.PI);
        }

        public Fixed Sin(Fixed angle)
        {
            double radians = angle.ToDouble();
            double idx = radians * invSize;
            double baseIdx = Math.Floor(idx);
            double frac = idx - baseIdx;
            int i0 = (int)baseIdx % size;
            if (i0 < 0) i0 += size;
            int i1 = (i0 + 1) & mask;
            double v0 = sinTable[i0].ToDouble();
            double v1 = sinTable[i1].ToDouble();
            double v = v0 + (v1 - v0) * frac;
            return Fixed.FromDouble(v);
        }

        public Fixed Cos(Fixed angle)
        {
            // cos(x) = sin(x + PI/2)
            double r = angle.ToDouble() + Math.PI * 0.5;
            return Sin(Fixed.FromDouble(r));
        }

        public Fixed Atan2(Fixed y, Fixed x)
        {
            // fallback to Math.Atan2 for simplicity
            return Fixed.FromDouble(Math.Atan2(y.ToDouble(), x.ToDouble()));
        }
    }

    public static class FixedMath
    {
        // common constants
        public static readonly Fixed Pi = Fixed.FromDouble(Math.PI);
        public static readonly Fixed TwoPi = Fixed.FromDouble(Math.PI * 2.0);
        public static readonly Fixed HalfPi = Fixed.FromDouble(Math.PI * 0.5);
        public static readonly Fixed Deg2Rad = Fixed.FromDouble(Math.PI / 180.0);
        public static readonly Fixed Rad2Deg = Fixed.FromDouble(180.0 / Math.PI);

        public static Fixed Sqrt(Fixed v)
        {
            if (v.Raw <= 0) return Fixed.Zero;
            // compute integer sqrt of (v.Raw << SHIFT) to get result in fixed raw
            long val = ((long)v.Raw) << Fixed.SHIFT; // up to ~2^47
            // using Math.Sqrt on double is OK because val fits in 53-bit mantissa
            long sqrt = (long)Math.Floor(Math.Sqrt(val) + 0.5);
            return Fixed.FromRaw((int)sqrt);
        }

        public static void SinCos(Fixed angle, out Fixed sin, out Fixed cos)
        {
            sin = FixedMath.Sin(angle);
            cos = FixedMath.Cos(angle);
        }

        public static Fixed Sin(Fixed angle) => FixedConfig.TrigProvider.Sin(angle);
        public static Fixed Cos(Fixed angle) => FixedConfig.TrigProvider.Cos(angle);
        public static Fixed Atan2(Fixed y, Fixed x) => FixedConfig.TrigProvider.Atan2(y, x);

        public static Fixed Abs(Fixed v) => Fixed.Abs(v);
        public static Fixed Min(Fixed a, Fixed b) => Fixed.Min(a, b);
        public static Fixed Max(Fixed a, Fixed b) => Fixed.Max(a, b);

        public static Fixed Clamp(Fixed v, Fixed lo, Fixed hi) => v < lo ? lo : (v > hi ? hi : v);

        public static Fixed Lerp(Fixed a, Fixed b, Fixed t)
        {
            // a + (b-a) * t
            return a + (b - a) * t;
        }

        public static Fixed FromDegrees(double deg) => Fixed.FromDouble(deg * Math.PI / 180.0);
        public static Fixed FromRadians(double rad) => Fixed.FromDouble(rad);

        public static Fixed Acos(Fixed v)
        {
            // clamp to [-1,1]
            if (v > Fixed.One) v = Fixed.One;
            var negOne = -Fixed.One;
            if (v < negOne) v = negOne;
            return Fixed.FromDouble(Math.Acos(v.ToDouble()));
        }
    }
}
