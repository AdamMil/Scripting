using System;

namespace Scripting.Runtime
{

public sealed class Rational : IConvertible, IComparable<Rational>, ICloneable
{
  public Rational(int i) : this(new Integer(i)) { }
  public Rational(long i) : this(new Integer(i)) { }
  public Rational(uint i) : this(new Integer(i)) { }
  public Rational(ulong i) : this(new Integer(i)) { }

  public Rational(Integer i)
  {
    numerator   = i;
    denominator = Integer.One;
  }

  public Rational(int num, int den) : this(new Integer(num), new Integer(den)) { }
  public Rational(long num, long den) : this(new Integer(num), new Integer(den)) { }

  public Rational(Integer num, Integer den)
  {
    if(den.Sign == 0) throw new ArgumentException("The denominator must not be negative.");
    numerator   = num;
    denominator = den;
    Normalize();
  }

  public unsafe Rational(double d)
  {
    int exponent;
    double fraction = Utilities.SplitFloat(d, out exponent);
    // we'll utilize the properties of the IEEE754 format to quickly calculate the numerator and denominator.
    // after splitting the float, 'fraction' will contain a value with an exponent of -1 and a mantissa of double
    // the actual fraction. to create the numerator, all we need to do is shift off the trailing zeros from the
    // mantissa and stick a leading one on the front. to create the denominator, ...

    ulong mantissa = *(ulong*)&fraction & 0xFFFFFFFFFFFFF;
    int shift;
    if(mantissa == 0)
    {
      shift = 0;
      numerator = Integer.One;
    }
    else
    {
      shift = 52;
      while((mantissa & 0xFF) == 0) { mantissa >>= 8; shift -= 8; }
      while((mantissa & 1) == 0) { mantissa >>= 1; shift--; }
      numerator = new Integer(mantissa | ((ulong)1<<shift));
    }

    exponent = shift-exponent+1;
    if(exponent < 0)
    {
      numerator  *= Integer.Pow(2, (uint)-exponent);
      denominator = Integer.One;
    }
    else
    {
      denominator = Integer.Pow(2, (uint)exponent);
    }

    if(d < 0) numerator = -numerator;
    Normalize();
  }

  Rational(Integer num, Integer den, bool dummy)
  {
    numerator   = num;
    denominator = den;
  }

  public Integer Denominator
  {
    get { return denominator; }
  }

  public Integer Numerator
  {
    get { return numerator; }
  }

  public override bool Equals(object obj)
  {
    return obj is Rational ? this == (Rational)obj : false;
  }

  public override int GetHashCode()
  {
    return numerator.GetHashCode() ^ denominator.GetHashCode();
  }

  public override string ToString()
  {
    return denominator == Integer.One ? numerator.ToString() : numerator.ToString()+"/"+denominator.ToString();
  }

  #region ICloneable Members
  public object Clone() { return new Rational(numerator, denominator, false); }
  #endregion

  #region IComparable Members
  public int CompareTo(object other)
  {
    if(other is Rational) return CompareTo((Rational)other);
    else throw new ArgumentException();
  }

  public int CompareTo(Rational other)
  {
    return Compare(this, other);
  }

  public int CompareTo(Integer other)
  {
    // if the signs are different, we know the relationship right away
    if(numerator.Sign != other.Sign) return numerator.Sign - other.Sign;

    // test for equality
    if(denominator == Integer.One && numerator == other) return 0;

    // take advantage of numerator/denominator truncating towards zero...
    if(numerator.Sign > 0) return Integer.Compare(numerator/denominator, other);
    else return Integer.Compare(-other, -numerator/denominator);
  }

  public static int Compare(Rational a, Rational b)
  {
    // if the signs are different, we know their relationship right away
    if(a.numerator.Sign != b.numerator.Sign) return a.numerator.Sign - b.numerator.Sign;
    else if(a == b) return 0;

    Integer numGcd = Integer.GreatestCommonFactor(a.numerator, b.numerator);
    Integer denGcd = Integer.GreatestCommonFactor(b.denominator, a.denominator);
    return Integer.Compare((a.numerator/numGcd)*(b.denominator/denGcd), (a.denominator/denGcd)*(b.numerator/numGcd));
  }
  #endregion

  #region IConvertible Members
  TypeCode IConvertible.GetTypeCode()
  {
    return TypeCode.Object;
  }

  bool IConvertible.ToBoolean(IFormatProvider provider)
  {
    return this != Zero;
  }

  byte IConvertible.ToByte(IFormatProvider provider)
  {
    double d = Math.Round(ToDouble());
    if(d < byte.MinValue || d > byte.MaxValue) throw new OverflowException();
    return (byte)d;
  }

  char IConvertible.ToChar(IFormatProvider provider)
  {
    double d = Math.Round(ToDouble());
    if(d < 0 || d > (int)char.MaxValue) throw new OverflowException();
    return (char)(int)d;
  }

  DateTime IConvertible.ToDateTime(IFormatProvider provider)
  {
    throw new InvalidCastException();
  }

  decimal IConvertible.ToDecimal(IFormatProvider provider)
  {
    return new decimal(ToDouble());
  }

  double IConvertible.ToDouble(IFormatProvider provider)
  {
    return ToDouble();
  }

  short IConvertible.ToInt16(IFormatProvider provider)
  {
    double d = Math.Round(ToDouble());
    if(d < short.MinValue || d > short.MaxValue) throw new OverflowException();
    return (short)d;
  }

  int IConvertible.ToInt32(IFormatProvider provider)
  {
    double d = Math.Round(ToDouble());
    if(d < int.MinValue || d > int.MaxValue) throw new OverflowException();
    return (int)d;
  }

  long IConvertible.ToInt64(IFormatProvider provider)
  {
    double d = Math.Round(ToDouble());
    if(d < long.MinValue || d > long.MaxValue) throw new OverflowException();
    return (long)d;
  }

  sbyte IConvertible.ToSByte(IFormatProvider provider)
  {
    double d = Math.Round(ToDouble());
    if(d < sbyte.MinValue || d > sbyte.MaxValue) throw new OverflowException();
    return (sbyte)d;
  }

  float IConvertible.ToSingle(IFormatProvider provider)
  {
    return (float)ToDouble();
  }

  string IConvertible.ToString(IFormatProvider provider)
  {
    return ToString();
  }

  object IConvertible.ToType(Type conversionType, IFormatProvider provider)
  {
    return Utilities.ToType(this, conversionType, provider);
  }

  ushort IConvertible.ToUInt16(IFormatProvider provider)
  {
    double d = Math.Round(ToDouble());
    if(d < ushort.MinValue || d > ushort.MaxValue) throw new OverflowException();
    return (ushort)d;
  }

  uint IConvertible.ToUInt32(IFormatProvider provider)
  {
    double d = Math.Round(ToDouble());
    if(d < uint.MinValue || d > uint.MaxValue) throw new OverflowException();
    return (uint)d;
  }

  ulong IConvertible.ToUInt64(IFormatProvider provider)
  {
    double d = Math.Round(ToDouble());
    if(d < ulong.MinValue || d > ulong.MaxValue) throw new OverflowException();
    return (ulong)d;
  }

  public double ToDouble()
  {
    return numerator.ToDouble(false) / denominator.ToDouble(false);
  }

  public static byte ToByte(Rational r) { return ((IConvertible)r).ToByte(null); }
  public static decimal ToDecimal(Rational r) { return ((IConvertible)r).ToDecimal(null); }
  public static double ToDouble(Rational r) { return r.ToDouble(); }
  public static float ToSingle(Rational r) { return ((IConvertible)r).ToSingle(null); }
  public static short ToInt16(Rational r) { return ((IConvertible)r).ToInt16(null); }
  public static int ToInt32(Rational r) { return ((IConvertible)r).ToInt32(null); }
  public static long ToInt64(Rational r) { return ((IConvertible)r).ToInt64(null); }
  public static ushort ToUInt16(Rational r) { return ((IConvertible)r).ToUInt16(null); }
  public static uint ToUInt32(Rational r) { return ((IConvertible)r).ToUInt32(null); }
  public static ulong ToUInt64(Rational r) { return ((IConvertible)r).ToUInt64(null); }
  public static sbyte ToSByte(Rational r) { return ((IConvertible)r).ToSByte(null); }
  #endregion

  #region Operators
  #region Addition
  public static Rational operator+(Rational a, Rational b)
  {
    Integer gcd = Integer.GreatestCommonFactor(a.denominator, b.denominator);
    Integer den = a.denominator / gcd;
    Integer num = a.numerator * (b.denominator / gcd) + (b.numerator * den);
    gcd = Integer.GreatestCommonFactor(num, gcd);
    num /= gcd;
    den *= b.denominator / gcd;
    return new Rational(num, den, false);
  }

  public static Rational operator+(Rational a, Integer b)
  {
    return new Rational(a.numerator + (b * a.denominator), a.denominator, false);
  }

  public static Rational operator+(Rational a, int b) { return a + new Integer(b); }
  public static Rational operator+(Rational a, uint b) { return a + new Integer(b); }
  public static Rational operator+(Rational a, long b) { return a + new Integer(b); }
  public static Rational operator+(Rational a, ulong b) { return a + new Integer(b); }
  public static Rational operator+(Rational a, double b) { return a + new Rational(b); }
  public static Rational operator+(int a, Rational b) { return b + new Integer(a); }
  public static Rational operator+(uint a, Rational b) { return b + new Integer(a); }
  public static Rational operator+(long a, Rational b) { return b + new Integer(a); }
  public static Rational operator+(ulong a, Rational b) { return b + new Integer(a); }
  public static Rational operator+(Integer a, Rational b) { return b + a; }
  public static Rational operator+(double a, Rational b) { return new Rational(a) + b; }
  #endregion

  #region Subtraction
  public static Rational operator-(Rational a, Rational b)
  {
    Integer gcd = Integer.GreatestCommonFactor(a.denominator, b.denominator);
    Integer den = a.denominator / gcd;
    Integer num = a.numerator * (b.denominator / gcd) - (b.numerator * den);
    gcd = Integer.GreatestCommonFactor(num, gcd);
    num /= gcd;
    den *= b.denominator / gcd;
    return new Rational(num, den, false);
  }

  public static Rational operator-(Rational a, Integer b)
  {
    return new Rational(a.numerator - b*a.denominator, a.denominator, false);
  }
  public static Rational operator-(Integer a, Rational b)
  {
    return new Rational(a*b.denominator - b.numerator, b.denominator, false);
  }

  public static Rational operator-(Rational a, int b) { return a - new Integer(b); }
  public static Rational operator-(Rational a, uint b) { return a - new Integer(b); }
  public static Rational operator-(Rational a, long b) { return a - new Integer(b); }
  public static Rational operator-(Rational a, ulong b) { return a - new Integer(b); }
  public static Rational operator-(Rational a, double b) { return a - new Rational(b); }
  public static Rational operator-(int a, Rational b) { return new Integer(a) - b; }
  public static Rational operator-(uint a, Rational b) { return new Integer(a) - b; }
  public static Rational operator-(long a, Rational b) { return new Integer(a) - b; }
  public static Rational operator-(ulong a, Rational b) { return new Integer(a) - b; }
  public static Rational operator-(double a, Rational b) { return new Rational(a) - b; }
  #endregion

  #region Multiplication
  public static Rational operator*(Rational a, Rational b)
  {
    Integer gcd1 = Integer.GreatestCommonFactor(a.numerator, b.denominator);
    Integer gcd2 = Integer.GreatestCommonFactor(b.numerator, a.denominator);
    return new Rational((a.numerator/gcd1) * (b.numerator/gcd2), (a.denominator/gcd2) * (b.denominator/gcd1), false);
  }

  public static Rational operator*(Rational a, Integer b)
  {
    return new Rational(a.numerator*b, a.denominator);
  }

  public static Rational operator*(Rational a, int b) { return a * new Integer(b); }
  public static Rational operator*(Rational a, uint b) { return a * new Integer(b); }
  public static Rational operator*(Rational a, long b) { return a * new Integer(b); }
  public static Rational operator*(Rational a, ulong b) { return a * new Integer(b); }
  public static Rational operator*(Rational a, double b) { return a * new Rational(b); }
  public static Rational operator*(int a, Rational b) { return b * new Integer(a); }
  public static Rational operator*(uint a, Rational b) { return b * new Integer(a); }
  public static Rational operator*(long a, Rational b) { return b * new Integer(a); }
  public static Rational operator*(ulong a, Rational b) { return b * new Integer(a); }
  public static Rational operator*(Integer a, Rational b) { return b * a; }
  public static Rational operator*(double a, Rational b) { return new Rational(a) * b; }
  #endregion

  #region Division
  public static Rational operator/(Rational a, Rational b)
  {
    if(b.numerator.Length == 0) throw new DivideByZeroException();
    if(a.numerator.Length == 0) return a;

    Integer numGcd = Integer.GreatestCommonFactor(a.numerator, b.numerator);
    Integer denGcd = Integer.GreatestCommonFactor(b.denominator, a.denominator);
    Integer num = (a.numerator/numGcd) * (b.denominator/denGcd);
    Integer den = (a.denominator/denGcd) * (b.numerator/numGcd);
    if(den.Sign < 0)
    {
      num = -num;
      den = -den;
    }
    return new Rational(num, den, false);
  }

  public static Rational operator/(Rational a, Integer b)
  {
    if(a.numerator.Length == 0) return a;

    Integer numGcd = Integer.GreatestCommonFactor(a.numerator, b);
    Integer num = a.numerator / numGcd;
    Integer den = a.denominator * (b/numGcd);
    if(den.Sign < 0)
    {
      num = -num;
      den = -den;
    }
    return new Rational(num, den, false);
  }

  public static Rational operator/(Integer a, Rational b)
  {
    if(b.numerator.Length == 0) throw new DivideByZeroException();
    if(a.Length == 0) return new Rational(a);

    Integer numGcd = Integer.GreatestCommonFactor(a, b.numerator);
    Integer num = (a/numGcd) * b.denominator;
    Integer den = b.numerator / numGcd;
    if(den.Sign < 0)
    {
      num = -num;
      den = -den;
    }
    return new Rational(num, den, false);
  }

  public static Rational operator/(Rational a, int b) { return a / new Integer(b); }
  public static Rational operator/(Rational a, uint b) { return a / new Integer(b); }
  public static Rational operator/(Rational a, long b) { return a / new Integer(b); }
  public static Rational operator/(Rational a, ulong b) { return a / new Integer(b); }
  public static Rational operator/(Rational a, double b) { return a / new Rational(b); }
  public static Rational operator/(int a, Rational b) { return new Integer(a) / b; }
  public static Rational operator/(uint a, Rational b) { return new Integer(a) / b; }
  public static Rational operator/(long a, Rational b) { return new Integer(a) / b; }
  public static Rational operator/(ulong a, Rational b) { return new Integer(a) / b; }
  public static Rational operator/(double a, Rational b) { return new Rational(a) / b; }
  #endregion

  #region Unary operators
  public static Rational operator-(Rational r)
  {
    return new Rational(-r.numerator, r.denominator, false);
  }

  public static Rational operator++(Rational r)
  {
    return new Rational(r.numerator+r.denominator, r.denominator, false);
  }

  public static Rational operator--(Rational r)
  {
    return new Rational(r.numerator-r.denominator, r.denominator, false);
  }
  #endregion

  #region Comparison operators
  public static bool operator<(Rational a, Rational b) { return a.CompareTo(b) < 0; }
  public static bool operator<(Rational a, Integer b) { return a.CompareTo(b) < 0; }
  public static bool operator<(Rational a, int b) { return a.CompareTo(new Integer(b)) < 0; }
  public static bool operator<(Rational a, uint b) { return a.CompareTo(new Integer(b)) < 0; }
  public static bool operator<(Rational a, long b) { return a.CompareTo(new Integer(b)) < 0; }
  public static bool operator<(Rational a, ulong b) { return a.CompareTo(new Integer(b)) < 0; }
  public static bool operator<(Rational a, double b) { return a.CompareTo(new Rational(b)) < 0; }
  public static bool operator<(Integer a, Rational b) { return b.CompareTo(a) >= 0; }
  public static bool operator<(int a, Rational b) { return b.CompareTo(new Integer(a)) >= 0; }
  public static bool operator<(uint a, Rational b) { return b.CompareTo(new Integer(a)) >= 0; }
  public static bool operator<(long a, Rational b) { return b.CompareTo(new Integer(a)) >= 0; }
  public static bool operator<(ulong a, Rational b) { return b.CompareTo(new Integer(a)) >= 0; }
  public static bool operator<(double a, Rational b) { return b.CompareTo(new Rational(a)) >= 0; }

  public static bool operator>(Rational a, Rational b) { return a.CompareTo(b) > 0; }
  public static bool operator>(Rational a, Integer b) { return a.CompareTo(b) > 0; }
  public static bool operator>(Rational a, int b) { return a.CompareTo(new Integer(b)) > 0; }
  public static bool operator>(Rational a, uint b) { return a.CompareTo(new Integer(b)) > 0; }
  public static bool operator>(Rational a, long b) { return a.CompareTo(new Integer(b)) > 0; }
  public static bool operator>(Rational a, ulong b) { return a.CompareTo(new Integer(b)) > 0; }
  public static bool operator>(Rational a, double b) { return a.CompareTo(new Rational(b)) > 0; }
  public static bool operator>(Integer a, Rational b) { return b.CompareTo(a) <= 0; }
  public static bool operator>(int a, Rational b) { return b.CompareTo(new Integer(a)) <= 0; }
  public static bool operator>(uint a, Rational b) { return b.CompareTo(new Integer(a)) <= 0; }
  public static bool operator>(long a, Rational b) { return b.CompareTo(new Integer(a)) <= 0; }
  public static bool operator>(ulong a, Rational b) { return b.CompareTo(new Integer(a)) <= 0; }
  public static bool operator>(double a, Rational b) { return b.CompareTo(new Rational(a)) < 0; }

  public static bool operator<=(Rational a, Rational b) { return a.CompareTo(b) <= 0; }
  public static bool operator<=(Rational a, Integer b) { return a.CompareTo(b) <= 0; }
  public static bool operator<=(Rational a, int b) { return a.CompareTo(new Integer(b)) <= 0; }
  public static bool operator<=(Rational a, uint b) { return a.CompareTo(new Integer(b)) <= 0; }
  public static bool operator<=(Rational a, long b) { return a.CompareTo(new Integer(b)) <= 0; }
  public static bool operator<=(Rational a, ulong b) { return a.CompareTo(new Integer(b)) <= 0; }
  public static bool operator<=(Rational a, double b) { return a.CompareTo(new Rational(b)) <= 0; }
  public static bool operator<=(Integer a, Rational b) { return b.CompareTo(a) > 0; }
  public static bool operator<=(int a, Rational b) { return b.CompareTo(new Integer(a)) > 0; }
  public static bool operator<=(uint a, Rational b) { return b.CompareTo(new Integer(a)) > 0; }
  public static bool operator<=(long a, Rational b) { return b.CompareTo(new Integer(a)) > 0; }
  public static bool operator<=(ulong a, Rational b) { return b.CompareTo(new Integer(a)) > 0; }
  public static bool operator<=(double a, Rational b) { return b.CompareTo(new Rational(a)) > 0; }

  public static bool operator>=(Rational a, Rational b) { return a.CompareTo(b) >= 0; }
  public static bool operator>=(Rational a, Integer b) { return a.CompareTo(b) >= 0; }
  public static bool operator>=(Rational a, int b) { return a.CompareTo(new Integer(b)) >= 0; }
  public static bool operator>=(Rational a, uint b) { return a.CompareTo(new Integer(b)) >= 0; }
  public static bool operator>=(Rational a, long b) { return a.CompareTo(new Integer(b)) >= 0; }
  public static bool operator>=(Rational a, ulong b) { return a.CompareTo(new Integer(b)) >= 0; }
  public static bool operator>=(Rational a, double b) { return a.CompareTo(new Rational(b)) >= 0; }
  public static bool operator>=(Integer a, Rational b) { return b.CompareTo(a) < 0; }
  public static bool operator>=(int a, Rational b) { return b.CompareTo(new Integer(a)) < 0; }
  public static bool operator>=(uint a, Rational b) { return b.CompareTo(new Integer(a)) < 0; }
  public static bool operator>=(long a, Rational b) { return b.CompareTo(new Integer(a)) < 0; }
  public static bool operator>=(ulong a, Rational b) { return b.CompareTo(new Integer(a)) < 0; }
  public static bool operator>=(double a, Rational b) { return b.CompareTo(new Rational(a)) < 0; }

  public static bool operator==(Rational a, Rational b)
  {
    return a.numerator == b.numerator && a.denominator == b.denominator; 
  }
  public static bool operator==(Rational a, Integer b) { return a.numerator == b && a.denominator == 1; }
  public static bool operator==(Rational a, int b) { return a.numerator == b && a.denominator == 1; }
  public static bool operator==(Rational a, uint b) { return a.numerator == b && a.denominator == 1; }
  public static bool operator==(Rational a, long b) { return a.numerator == b && a.denominator == 1; }
  public static bool operator==(Rational a, ulong b) { return a.numerator == b && a.denominator == 1; }
  public static bool operator==(Rational a, double b) { return a == new Rational(b); }
  public static bool operator==(Integer a, Rational b) { return b.numerator == a && b.denominator == 1; }
  public static bool operator==(int a, Rational b) { return b.numerator == a && b.denominator == 1; }
  public static bool operator==(uint a, Rational b) { return b.numerator == a && b.denominator == 1; }
  public static bool operator==(long a, Rational b) { return b.numerator == a && b.denominator == 1; }
  public static bool operator==(ulong a, Rational b) { return b.numerator == a && b.denominator == 1; }
  public static bool operator==(double a, Rational b) { return new Rational(a) == b; }

  public static bool operator!=(Rational a, Rational b)
  {
    return a.numerator != b.numerator || a.denominator != b.denominator;
  }
  public static bool operator!=(Rational a, Integer b) { return a.numerator != b || a.denominator != 1; }
  public static bool operator!=(Rational a, int b) { return a.numerator != b || a.denominator != 1; }
  public static bool operator!=(Rational a, uint b) { return a.numerator != b || a.denominator != 1; }
  public static bool operator!=(Rational a, long b) { return a.numerator != b || a.denominator != 1; }
  public static bool operator!=(Rational a, ulong b) { return a.numerator != b || a.denominator != 1; }
  public static bool operator!=(Rational a, double b) { return a != new Rational(b); }
  public static bool operator!=(Integer a, Rational b) { return b.numerator != a || b.denominator != 1; }
  public static bool operator!=(int a, Rational b) { return b.numerator != a || b.denominator != 1; }
  public static bool operator!=(uint a, Rational b) { return b.numerator != a || b.denominator != 1; }
  public static bool operator!=(long a, Rational b) { return b.numerator != a || b.denominator != 1; }
  public static bool operator!=(ulong a, Rational b) { return b.numerator != a || b.denominator != 1; }
  public static bool operator!=(double a, Rational b) { return new Rational(a) != b; }
  #endregion

  #region Implicit conversions
  public static implicit operator Rational(int i) { return new Rational(i); }
  public static implicit operator Rational(long i) { return new Rational(i); }
  public static implicit operator Rational(uint i) { return new Rational(i); }
  public static implicit operator Rational(ulong i) { return new Rational(i); }
  public static implicit operator Rational(Integer i) { return new Rational(i); }
  public static implicit operator Rational(float f) { return new Rational(f); }
  public static implicit operator Rational(double d) { return new Rational(d); }
  #endregion
  #endregion

  public static Rational Abs(Rational r)
  {
    return r.numerator.Sign < 0 ? -r : r;
  }

  public static Rational Parse(string str)
  {
    int slashIndex = str.IndexOf('/');
    return slashIndex == -1 ? new Rational(Integer.Parse(str))
      : new Rational(Integer.Parse(str.Substring(0, slashIndex)), Integer.Parse(str.Substring(slashIndex+1)));
  }

  /// <summary>Creates and returns a <see cref="Rational"/> without normalizing it. The only valid usage of this method
  /// is to pass the numerator and denominator retrieved from a valid <see cref="Rational"/>. This method exists to
  /// support deserializing rationals without the unnecessary performance penalty of normalization.
  /// </summary>
  public static Rational Recreate(Integer numerator, Integer denominator)
  {
    return new Rational(numerator, denominator, false);
  }

  public static readonly Rational Zero = new Rational(0);
  public static readonly Rational One  = new Rational(1);
  public static readonly Rational MinusOne = new Rational(-1);

  void Normalize()
  {
    if(numerator.Sign == 0)
    {
      denominator = Integer.One;
      return;
    }

    Integer gcd = Integer.GreatestCommonFactor(numerator, denominator);
    numerator /= gcd;
    denominator /= gcd;

    if(denominator.Sign < 0) // ensure that the denominator is positive
    {
      numerator   = -numerator;
      denominator = -denominator;
    }
  }

  /// <summary>The numerator of the fraction. Can be positive, negative, or zero.</summary>
  Integer numerator;
  /// <summary>The denominator of the fraction. This should always be positive.</summary>
  Integer denominator;
}

} // namespace Scripting.Runtime