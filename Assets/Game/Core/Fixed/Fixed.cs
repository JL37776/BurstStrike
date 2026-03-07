using System;

namespace Game.Scripts.Fixed
{
    // Q16.16 fixed-point number stored in a 32-bit int (16 fractional bits).
    // Value = Raw / (1 << SHIFT)
    public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
    {
        public readonly int Raw;

        public const int SHIFT = 16;
        public const int ONE = 1 << SHIFT;
        public const long LONG_ONE = 1L << SHIFT;

        public static readonly Fixed Zero = new Fixed(0);
        public static readonly Fixed One = FromRaw(ONE);
        public static readonly Fixed Two = FromRaw(ONE * 2);
        public static readonly Fixed MinusOne = FromRaw(-ONE);

        // Representable extremes (raw int range)
        public static readonly Fixed MaxValue = FromRaw(int.MaxValue);
        public static readonly Fixed MinValue = FromRaw(int.MinValue);

        public Fixed(int raw) { Raw = raw; }

        public static Fixed FromRaw(int raw) => new Fixed(raw);
        public static Fixed FromInt(int v) => new Fixed(v << SHIFT);

        public static Fixed FromFloat(float f)
        {
            double d = Math.Round((double)f * ONE);
            if (d >= int.MaxValue) return MaxValue;
            if (d <= int.MinValue) return MinValue;
            return new Fixed((int)d);
        }

        public static Fixed FromDouble(double d)
        {
            double r = Math.Round(d * ONE);
            if (r >= int.MaxValue) return MaxValue;
            if (r <= int.MinValue) return MinValue;
            return new Fixed((int)r);
        }

        /// <summary>
        /// Create a fixed value from a rational number (numerator/denominator) without using float literals.
        /// Rounds to nearest representable Q16.16 value.
        /// </summary>
        public static Fixed FromRatio(int numerator, int denominator)
        {
            if (denominator == 0) throw new DivideByZeroException("Fixed.FromRatio denominator is zero");
            // Compute (numerator * ONE) / denominator with rounding.
            long num = (long)numerator * ONE;
            long den = denominator;
            if (den < 0) { den = -den; num = -num; }

            long adj = den / 2;
            long raw = num >= 0 ? (num + adj) / den : (num - adj) / den;
            if (raw >= int.MaxValue) return MaxValue;
            if (raw <= int.MinValue) return MinValue;
            return new Fixed((int)raw);
        }

        /// <summary>Convenience: v/1000 (e.g. 50 -> 0.050).</summary>
        public static Fixed FromMilli(int milli) => FromRatio(milli, 1000);

        /// <summary>Convenience: v/100 (e.g. 5 -> 0.05).</summary>
        public static Fixed FromCenti(int centi) => FromRatio(centi, 100);

        public float ToFloat() => Raw / (float)ONE;
        public double ToDouble() => Raw / (double)ONE;

        public override string ToString() => ToFloat().ToString("G6");

        // arithmetic
        public static Fixed operator +(Fixed a, Fixed b)
        {
            long sum = (long)a.Raw + b.Raw;
            if (global::Game.Scripts.Fixed.FixedConfig.UseSaturatingArithmetic)
            {
                if (sum > int.MaxValue) return MaxValue;
                if (sum < int.MinValue) return MinValue;
            }
            return new Fixed((int)sum);
        }

        public static Fixed operator -(Fixed a, Fixed b)
        {
            long diff = (long)a.Raw - b.Raw;
            if (global::Game.Scripts.Fixed.FixedConfig.UseSaturatingArithmetic)
            {
                if (diff > int.MaxValue) return MaxValue;
                if (diff < int.MinValue) return MinValue;
            }
            return new Fixed((int)diff);
        }

        public static Fixed operator -(Fixed a) => new Fixed(-a.Raw);

        public static Fixed operator *(Fixed a, Fixed b)
        {
            long prod = (long)a.Raw * b.Raw;
            // rounding: add 0.5 in fixed domain
            long rounded = (prod + (1L << (SHIFT - 1))) >> SHIFT;
            if (global::Game.Scripts.Fixed.FixedConfig.UseSaturatingArithmetic)
            {
                if (rounded > int.MaxValue) return MaxValue;
                if (rounded < int.MinValue) return MinValue;
            }
            return new Fixed((int)rounded);
        }

        public static Fixed operator /(Fixed a, Fixed b)
        {
            if (b.Raw == 0) throw new DivideByZeroException("Fixed division by zero");
            long num = ((long)a.Raw << SHIFT);
            // rounding: adjust numerator by half of divisor (sign-aware)
            long adj = Math.Abs((long)b.Raw) / 2;
            if ((num >= 0 && b.Raw > 0) || (num < 0 && b.Raw < 0))
                num += adj;
            else
                num -= adj;

            long res = num / b.Raw;
            if (global::Game.Scripts.Fixed.FixedConfig.UseSaturatingArithmetic)
            {
                if (res > int.MaxValue) return MaxValue;
                if (res < int.MinValue) return MinValue;
            }
            return new Fixed((int)res);
        }

        // comparisons
        public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;
        public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;
        public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;
        public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;
        public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;
        public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;

        public bool Equals(Fixed other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fixed f && Equals(f);
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);

        // helpers
        public static Fixed Abs(Fixed v) => v.Raw >= 0 ? v : (v.Raw == int.MinValue ? MaxValue : new Fixed(-v.Raw));
        public static Fixed Min(Fixed a, Fixed b) => a.Raw <= b.Raw ? a : b;
        public static Fixed Max(Fixed a, Fixed b) => a.Raw >= b.Raw ? a : b;

        // conversions
        public static implicit operator Fixed(int v) => FromInt(v);
        public static explicit operator int(Fixed f) => f.Raw >> SHIFT;
        public static explicit operator float(Fixed f) => f.ToFloat();
        public static explicit operator double(Fixed f) => f.ToDouble();
    }
}
