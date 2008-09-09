using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Scripting.Runtime
{

public struct Integer : IConvertible, IComparable<Integer>, ICloneable
{
  #region Constructors
  public Integer(int i)
  {
    if(i > 0)
    {
      sign   = 1;
      data   = i == 1 ? One.data : new uint[1] { (uint)i };
      length = 1;
    }
    else if(i < 0)
    {
      sign   = -1;
      data   = i == -1 ? MinusOne.data : new uint[1] { (uint)-i };
      length = 1;
    }
    else
    {
      this = Zero;
    }
  }

  public Integer(uint i)
  {
    if(i == 0)
    {
      this = Zero;
    }
    else
    {
      sign = 1;
      data = i == 1 ? One.data : new uint[1] { i };
      length = 1;
    }
  }

  public Integer(long i)
  {
    if(i == 0)
    {
      this = Zero;
    }
    else
    {
      ulong v;
      if(i > 0)
      {
        sign = 1;
        v    = (ulong)i;
      }
      else
      {
        sign = -1;
        v    = (ulong)-i;
      }

      data = new uint[2] { (uint)v, (uint)(v>>32) };
      length = (ushort)CalculateLength(data);
    }
  }

  public Integer(ulong i)
  {
    if(i == 0)
    {
      this = Zero;
    }
    else
    {
      sign   = 1;
      data   = new uint[2] { (uint)i, (uint)(i>>32) };
      length = (ushort)CalculateLength(data);
    }
  }

  public Integer(double d)
  {
    if(double.IsInfinity(d)) throw new OverflowException("Cannot convert float infinity to Integer");
    if(double.IsNaN(d)) throw new InvalidCastException("Cannot convert NaN to Integer");

    double fraction;
    int exponent;

    fraction = Math.Abs(Utilities.SplitFloat(d, out exponent));
    if(exponent == 0)
    {
      this = Zero;
      return;
    }

    length = (ushort)((exponent+31)/32);
    data   = new uint[length];

    fraction = Utilities.MakeFloat(fraction, ((exponent-1)&31)+1);
    data[length-1] = (uint)fraction;

    for(int i=length-2; i>=0 && fraction != 0; i--)
    {
      fraction = Utilities.MakeFloat(fraction - (uint)fraction, 32);
      data[i]  = (uint)fraction;
    }

    sign = (short)(d<0 ? -1 : 1);
  }

  public Integer(short sign, uint[] data)
  {
    int length = CalculateLength(data);
    if(length > ushort.MaxValue) throw new NotImplementedException("Integer values larger than 2097120 bits.");

    this.sign   = length == 0 ? (short)0 : sign < 0 ? (short)-1 : (short)1;
    this.data   = data;
    this.length = (ushort)length;
  }
  #endregion

  public int Length
  {
    get { return length; }
  }

  /// <summary>Gets a value which will be negative if the integer is negative, positive if the integer is positive, and
  /// zero if the integer is zero.
  /// </summary>
  public int Sign
  {
    get { return sign; }
  }

  public override bool Equals(object obj)
  {
    return obj is Integer ? CompareTo((Integer)obj) == 0 : false;
  }

  public uint GetData(int index)
  {
    return data[index];
  }

  public override int GetHashCode()
  {
    uint hash = 0;
    for(int i=0; i<length; i++) hash ^= data[i];
    return (int)hash;
  }

  #region ToString
  public override string ToString()
  {
    return ToString(10);
  }

  public string ToString(int radix)
  {
    if(radix<2 || radix>36) throw new ArgumentOutOfRangeException("radix", radix, "radix must be from 2 to 36");
    if(length == 0) return "0";

    const string charSet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    System.Text.StringBuilder sb = new System.Text.StringBuilder();
    uint[] data = (uint[])this.data.Clone();
    int msi = length - 1; // index of the most significant dword in 'data'

    while(true) // build the string backwards
    {
      sb.Append(charSet[DivideInPlace(data, msi+1, (uint)radix)]);
      if(data[msi] == 0)
      {
        if(msi-- == 0) break;
      }
    }

    if(sign == -1) sb.Append('-'); // add the minus sign if it needs one

    char[] chars = new char[sb.Length]; // and then reverse and return the string
    for(int i=0,len=sb.Length-1; i<=len; i++)
    {
      chars[i] = sb[len-i];
    }
    return new string(chars);
  }
  #endregion

  public static Integer Abs(Integer i)
  {
    return i.sign == -1 ? -i : i;
  }

  /// <summary>Returns the greatest common factor of <paramref name="a"/> and <paramref name="b" /> as a
  /// non-negative value.
  /// </summary>
  public static Integer GreatestCommonFactor(Integer a, Integer b)
  {
    if(a.Sign < 0) a = -a;
    if(b.Sign < 0) b = -b;

    while(true)
    {
      if(b.Length == 0) return a;
      a %= b;
      if(a.Length == 0) return b;
      b %= a;
    }
  }

  /// <summary>Returns the least common multiple of <paramref name="a"/> and <paramref name="b"/> as a
  /// non-negative value.
  /// </summary>
  public static Integer LeastCommonMultiple(Integer a, Integer b)
  {
    if(a.Length == 0 || b.Length == 0) return Zero;
    return Abs(a / GreatestCommonFactor(a, b) * b);
  }

  public static Integer Parse(string str)
  {
    return Parse(str, 10);
  }

  public static Integer Parse(string str, int radix)
  {
    if(str == null) throw new ArgumentNullException();
    if(radix < 2 || radix > 36) throw new ArgumentOutOfRangeException("radix must be from 2 to 36");
    
    int i = 0;
    bool negative = false;

    while(i<str.Length && char.IsWhiteSpace(str[i])) i++; // skip whitespace

    if(i < str.Length) // check for sign markers
    {
      if(str[i]=='-')
      {
        negative = true;
        i++;
      }
      else if(str[i]=='+')
      {
        i++;
      }
    }

    while(i<str.Length && char.IsWhiteSpace(str[i])) i++; // skip more whitespace

    int startIndex = i;
    Integer value = Zero;
    for(char c; i != str.Length && IsValidDigit(c=char.ToUpperInvariant(str[i]), radix); i++)
    {
      value = value*radix + (c - (c <= '9' ? '0' : 'A'-10));
    }

    if(i == startIndex) throw new FormatException("String does not contain a valid integer");

    return negative ? -value : value;
  }

  public static Integer Pow(Integer i, uint power) // TODO: this can be optimized better
  {
    if(power == 2) return i.Squared();
    if(power < 0) throw new ArgumentOutOfRangeException("power", power, "power must be >= 0");

    Integer factor = i;
    Integer result = One;
    while(power != 0)
    {
      if((power&1) != 0) result *= factor;
      factor = factor.Squared();
      power >>= 1;
    }
    return result;
  }

  public static Integer Pow(Integer i, Integer power) // TODO: this can be optimized better
  {
    if(power.Sign < 0) throw new ArgumentOutOfRangeException("power", power, "power must be >= 0");
    if(power <= uint.MaxValue) return Pow(i, Integer.ToUInt32(power));

    Integer factor = i;
    Integer result = One;
    while(power != 0)
    {
      if((power&1) != 0) result *= factor;
      factor = factor.Squared();
      power >>= 1;
    }
    return result;
  }

  public static readonly Integer MinusOne = new Integer(-1, new uint[1] { 1 });
  public static readonly Integer One  = new Integer(1, new uint[1] { 1 });
  public static readonly Integer Zero = new Integer(0, null);

  #region Comparison operators
  public static bool operator==(Integer a, Integer b) { return a.CompareTo(b) == 0; }
  public static bool operator==(Integer a, int b) { return a.CompareTo(b) == 0; }
  public static bool operator==(Integer a, long b) { return a.CompareTo(b) == 0; }
  public static bool operator==(Integer a, uint b) { return a.CompareTo(b) == 0; }
  public static bool operator==(Integer a, ulong b) { return a.CompareTo(b) == 0; }
  public static bool operator==(int a, Integer b) { return b.CompareTo(a) == 0; }
  public static bool operator==(long a, Integer b) { return b.CompareTo(a) == 0; }
  public static bool operator==(uint a, Integer b) { return b.CompareTo(a) == 0; }
  public static bool operator==(ulong a, Integer b) { return b.CompareTo(a) == 0; }

  public static bool operator!=(Integer a, Integer b) { return a.CompareTo(b)!=0; }
  public static bool operator!=(Integer a, int b) { return a.CompareTo(b)!=0; }
  public static bool operator!=(Integer a, long b) { return a.CompareTo(b)!=0; }
  public static bool operator!=(Integer a, uint b) { return a.CompareTo(b)!=0; }
  public static bool operator!=(Integer a, ulong b) { return a.CompareTo(b)!=0; }
  public static bool operator!=(int a, Integer b) { return b.CompareTo(a)!=0; }
  public static bool operator!=(long a, Integer b) { return b.CompareTo(a)!=0; }
  public static bool operator!=(uint a, Integer b) { return b.CompareTo(a)!=0; }
  public static bool operator!=(ulong a, Integer b) { return b.CompareTo(a)!=0; }

  public static bool operator<(Integer a, Integer b) { return a.CompareTo(b) < 0; }
  public static bool operator<(Integer a, int b) { return a.CompareTo(b) < 0; }
  public static bool operator<(Integer a, long b) { return a.CompareTo(b) < 0; }
  public static bool operator<(Integer a, uint b) { return a.CompareTo(b) < 0; }
  public static bool operator<(Integer a, ulong b) { return a.CompareTo(b) < 0; }
  public static bool operator<(int a, Integer b) { return b.CompareTo(a) > 0; }
  public static bool operator<(long a, Integer b) { return b.CompareTo(a) > 0; }
  public static bool operator<(uint a, Integer b) { return b.CompareTo(a) > 0; }
  public static bool operator<(ulong a, Integer b) { return b.CompareTo(a) > 0; }

  public static bool operator<=(Integer a, Integer b) { return a.CompareTo(b) <= 0; }
  public static bool operator<=(Integer a, int b) { return a.CompareTo(b) <= 0; }
  public static bool operator<=(Integer a, long b) { return a.CompareTo(b) <= 0; }
  public static bool operator<=(Integer a, uint b) { return a.CompareTo(b) <= 0; }
  public static bool operator<=(Integer a, ulong b) { return a.CompareTo(b) <= 0; }
  public static bool operator<=(int a, Integer b) { return b.CompareTo(a) >= 0; }
  public static bool operator<=(long a, Integer b) { return b.CompareTo(a) >= 0; }
  public static bool operator<=(uint a, Integer b) { return b.CompareTo(a) >= 0; }
  public static bool operator<=(ulong a, Integer b) { return b.CompareTo(a) >= 0; }

  public static bool operator>(Integer a, Integer b) { return a.CompareTo(b) > 0; }
  public static bool operator>(Integer a, int b) { return a.CompareTo(b) > 0; }
  public static bool operator>(Integer a, long b) { return a.CompareTo(b) > 0; }
  public static bool operator>(Integer a, uint b) { return a.CompareTo(b) > 0; }
  public static bool operator>(Integer a, ulong b) { return a.CompareTo(b) > 0; }
  public static bool operator>(int a, Integer b) { return b.CompareTo(a) < 0; }
  public static bool operator>(long a, Integer b) { return b.CompareTo(a) < 0; }
  public static bool operator>(uint a, Integer b) { return b.CompareTo(a) < 0; }
  public static bool operator>(ulong a, Integer b) { return b.CompareTo(a) < 0; }

  public static bool operator>=(Integer a, Integer b) { return a.CompareTo(b) >= 0; }
  public static bool operator>=(Integer a, int b) { return a.CompareTo(b) >= 0; }
  public static bool operator>=(Integer a, long b) { return a.CompareTo(b) >= 0; }
  public static bool operator>=(Integer a, uint b) { return a.CompareTo(b) >= 0; }
  public static bool operator>=(Integer a, ulong b) { return a.CompareTo(b) >= 0; }
  public static bool operator>=(int a, Integer b) { return b.CompareTo(a) <= 0; }
  public static bool operator>=(long a, Integer b) { return b.CompareTo(a) <= 0; }
  public static bool operator>=(uint a, Integer b) { return b.CompareTo(a) <= 0; }
  public static bool operator>=(ulong a, Integer b) { return b.CompareTo(a) <= 0; }
  #endregion

  #region Arithmetic and bitwise operators
  #region Addition
  public static Integer operator+(Integer a, Integer b)
  {
    if(a.sign == 0) return b; // quickly handle addition to zero
    if(b.sign == 0) return a;

    int c = a.AbsCompareTo(b);
    if(a.sign == b.sign) // if the signs are the same, we'll perform addition on the data
    {
      if(c >= 0) return new Integer(a.sign, Add(a.data, a.length, b.data, b.length));
      else return new Integer(b.sign, Add(b.data, b.length, a.data, a.length));
    }
    else // otherwise, it's a subtraction operation
    {
      if(c > 0) return new Integer(a.sign, Subtract(a.data, a.length, b.data, b.length));
      else if(c == 0) return Zero;
      else return new Integer(b.sign, Subtract(b.data, b.length, a.data, a.length));
    }
  }

  public static Integer operator+(Integer a, int b)
  {
    if(a.sign == 0) return new Integer(b); // quickly handle addition to zero
    if(b == 0) return a;

    short bsign = (short)Math.Sign(b);
    uint ub = IntToUint(b);
    if(a.sign == bsign) // if the signs are the same, we'll perform addition on the data
    {
      return new Integer(bsign, Add(a.data, a.length, ub));
    }
    else // otherwise, it's a subtraction operation
    {
      int c = a.AbsCompareTo(ub);
      if(c > 0) return new Integer(a.sign, Subtract(a.data, a.length, ub));
      else if(c == 0) return Zero;
      else return new Integer(bsign, Subtract(ub, a.data, a.length));
    }
  }

  public static Integer operator+(Integer a, uint b)
  {
    if(a.sign == -1) // if a is negative, it's a subtraction operation
    {
      int c = a.AbsCompareTo(b);
      if(c > 0) return new Integer(a.sign, Subtract(a.data, a.length, b));
      else if(c == 0) return Zero;
      else return new Integer(1, Subtract(b, a.data, a.length));
    }
    else // otherwise it's addition
    {
      return new Integer(1, Add(a.data, a.length, b));
    }
  }

  public static Integer operator+(int a, Integer b) { return b + a; }
  public static Integer operator+(uint a, Integer b) { return b + a; }
  #endregion

  #region Subtraction
  public static Integer operator-(Integer a, Integer b)
  {
    if(b.sign == 0) return a;

    int c = a.AbsCompareTo(b);
    if(a.sign == b.sign) // if the signs are the same (eg, 5-2 or -5 - -2) it's a subtraction of the data
    {
      if(c > 0) return new Integer(a.sign, Subtract(a.data, a.length, b.data, b.length));
      else if(c == 0) return Zero;
      else return new Integer((short)-b.sign, Subtract(b.data, b.length, a.data, a.length));
    }
    else // otherwise, it's addition
    {
      if(c > 0) return new Integer(a.sign, Add(a.data, a.length, b.data, b.length));
      else return new Integer((short)-b.sign, Add(b.data, b.length, a.data, a.length));
    }
  }

  public static Integer operator-(Integer a, int b)
  {
    uint ub = IntToUint(b);
    int c = a.AbsCompareTo(ub);
    short bsign = (short)Math.Sign(b);
    if(a.sign == bsign) // if the signs are the same (eg, 5-2 or -5 - -2) it's a subtraction of the data
    {
      if(c > 0) return new Integer(a.sign, Subtract(a.data, a.length, ub));
      else if(c == 0) return Zero;
      else return new Integer((short)-bsign, Subtract(ub, a.data, a.length));
    }
    else // otherwise, it's addition
    {
      return new Integer(c > 0 ? a.sign : (short)-bsign, Add(a.data, a.length, ub));
    }
  }

  public static Integer operator-(Integer a, uint b)
  {
    int c = a.AbsCompareTo(b);
    if(a.sign == -1)
    {
      return new Integer(c>0 ? a.sign : (short)-1, Add(a.data, a.length, b));
    }
    else
    {
      if(c > 0) return new Integer(a.sign, Subtract(a.data, a.length, b));
      else if(c == 0) return Zero;
      else return new Integer(-1, Subtract(b, a.data, a.length));
    }
  }

  public static Integer operator-(int a, Integer b)
  {
    uint ua = IntToUint(a);
    int c = -b.AbsCompareTo(ua); // c == ua.AbsCompareTo(b)
    short asign = (short)Math.Sign(a);
    if(asign == b.sign) // subtraction
    {
      if(c > 0) return new Integer(asign, Subtract(ua, b.data, b.length));
      else if(c == 0) return Zero;
      else return new Integer((short)-b.sign, Subtract(b.data, b.length, ua));
    }
    else
    {
      return new Integer((short)(c>0 ? asign : -b.sign), Add(b.data, b.length, ua)); // addition
    }
  }

  public static Integer operator-(uint a, Integer b)
  {
    if(b.sign == -1)
    {
      return new Integer(1, Add(b.data, b.length, a)); // addition
    }
    else
    {
      int c = -b.AbsCompareTo(a); // c == a.CompareTo(b)
      if(c > 0) return new Integer(1, Subtract(a, b.data, b.length));
      else if(c == 0) return Zero;
      else return new Integer((short)-b.sign, Subtract(b.data, b.length, a));
    }
  }
  #endregion

  #region Multiplication
  public static Integer operator*(Integer a, Integer b)
  {
    int newSign = a.sign * b.sign;
    return newSign == 0 ? Zero : new Integer((short)newSign, Multiply(a.data, a.length, b.data, b.length));
  }

  public static Integer operator*(Integer a, int b)
  {
    int newSign = a.sign * Math.Sign(b);
    return newSign == 0 ? Zero : new Integer((short)newSign, Multiply(a.data, a.length, IntToUint(b)));
  }

  public static Integer operator*(Integer a, uint b)
  {
    return b == 0 || a.sign == 0 ? Zero : new Integer(a.sign, Multiply(a.data, a.length, b));
  }

  public static Integer operator*(int a, Integer b)
  {
    int newSign = Math.Sign(a) * b.sign;
    return newSign == 0 ? Zero : new Integer((short)newSign, Multiply(b.data, b.length, IntToUint(a)));
  }

  public static Integer operator*(uint a, Integer b)
  {
    return a == 0 || b.sign == 0 ? Zero : new Integer(b.sign, Multiply(b.data, b.length, a));
  }
  #endregion

  #region Division
  public static Integer operator/(Integer a, Integer b)
  {
    if(b.sign == 0) throw new DivideByZeroException("Integer division by zero");
    if(a.sign == 0) return Zero;

    int c = a.AbsCompareTo(b);
    if(c > 0)
    {
      uint[] dummy;
      return new Integer((short)(a.sign*b.sign), Divide(a.data, a.length, b.data, b.length, out dummy));
    }
    else if(c == 0)
    {
      return a.sign == b.sign ? One : MinusOne;
    }
    else
    {
      return Zero;
    }
  }

  public static Integer operator/(Integer a, int b)
  {
    if(b == 0) throw new DivideByZeroException("Integer division by zero");
    if(a.sign == 0) return Zero;
    uint dummy;
    return new Integer((short)(a.sign*Math.Sign(b)), Divide(a.data, a.length, IntToUint(b), out dummy));
  }

  public static Integer operator/(Integer a, uint b)
  {
    if(b == 0) throw new DivideByZeroException("Integer division by zero");
    if(a.sign == 0) return Zero;
    uint dummy;
    return new Integer(a.sign, Divide(a.data, a.length, b, out dummy));
  }
  #endregion

  #region Modulus
  public static Integer operator%(Integer a, Integer b)
  {
    if(b.sign == 0) throw new DivideByZeroException("Integer modulus by zero");
    if(a.sign == 0) return Zero;

    int c = a.AbsCompareTo(b);
    if(c < 0) return a;
    else if(c == 0) return Zero;

    uint[] remainder;
    Divide(a.data, a.length, b.data, b.length, out remainder);
    return new Integer(a.sign, remainder);
  }

  public static Integer operator%(Integer a, int b)
  {
    if(b == 0) throw new DivideByZeroException("Integer modulus by zero");
    if(a.sign == 0) return Zero;

    int c = a.AbsCompareTo(IntToUint(b));
    if(c < 0) return a;
    else if(c == 0) return Zero;

    uint remainder;
    Divide(a.data, a.length, IntToUint(b), out remainder);
    Integer ret = new Integer(remainder);
    ret.sign = a.sign;
    return ret;
  }

  public static Integer operator%(Integer a, uint b)
  {
    if(b == 0) throw new DivideByZeroException("Integer modulus by zero");
    if(a.sign == 0) return Zero;

    int c = a.AbsCompareTo(b);
    if(c < 0) return a;
    else if(c == 0) return Zero;

    uint remainder;
    Divide(a.data, a.length, b, out remainder);
    Integer ret = new Integer(remainder);
    ret.sign = a.sign;
    return ret;
  }
  #endregion

  #region Unary operators
  public static Integer operator-(Integer i) { return new Integer((short)-i.sign, i.data); }
  public static Integer operator~(Integer i)
  {
    return i.length == 0 ? new Integer(0xffffffff) : new Integer(1, BitNegate(i.data, i.length));
  }
  public static Integer operator++(Integer i) { return i+1; }
  public static Integer operator--(Integer i) { return i-1; }
  #endregion

  #region Bitwise And
  public static Integer operator&(Integer a, Integer b)
  {
    bool aIsNeg = a.sign==-1, bIsNeg = b.sign==-1;
    if(!aIsNeg && !bIsNeg)
    {
      return new Integer(1, BitAnd(a.data, a.length, b.data, b.length));
    }
    uint[] data = BitAnd(a.data, a.length, aIsNeg, b.data, b.length, bIsNeg);
    return aIsNeg && bIsNeg ? new Integer(-1, TwosComplement(data)) : new Integer(1, data);
  }
  #endregion

  #region Bitwise Or
  public static Integer operator|(Integer a, Integer b)
  {
    bool aIsNeg = a.sign==-1, bIsNeg = b.sign==-1;
    if(!aIsNeg && !bIsNeg) return new Integer(1, BitOr(a.data, a.length, b.data, b.length));
    return new Integer(-1, TwosComplement(BitOr(a.data, a.length, aIsNeg, b.data, b.length, bIsNeg)));
  }
  #endregion

  #region Bitwise Xor
  public static Integer operator^(Integer a, Integer b)
  {
    bool aIsNeg = a.sign==-1, bIsNeg = b.sign==-1;
    if(!aIsNeg && !bIsNeg) return new Integer(1, BitXor(a.data, a.length, b.data, b.length));
    uint[] data = BitXor(a.data, a.length, aIsNeg, b.data, b.length, bIsNeg);
    if(aIsNeg && bIsNeg) return new Integer(1, data);
    short sign = (data[data.Length-1]&0x80000000) == 0 ? (short)1 : (short)-1;
    return new Integer(sign, TwosComplement(data));
  }

  public static Integer operator^(Integer a, int b)
  {
    return a.sign == -1 || b<0 ? a ^ new Integer(b) : new Integer(1, BitXor(a.data, a.length, (uint)b));
  }

  public static Integer operator^(Integer a, uint b)
  {
    return a.sign == -1 ? a ^ new Integer(b) : new Integer(1, BitXor(a.data, a.length, b));
  }

  public static Integer operator^(Integer a, long b)
  {
    return a.sign == -1 || b < 0 ? a ^ new Integer(b) : new Integer(1, BitXor(a.data, a.length, (ulong)b));
  }

  public static Integer operator^(Integer a, ulong b)
  {
    return a.sign == -1 ? a ^ new Integer(b) : new Integer(1, BitXor(a.data, a.length, b));
  }

  public static Integer operator^(int a, Integer b)
  {
    return a < 0 || b.sign == -1 ? new Integer(a) ^ b : new Integer(1, BitXor(b.data, b.length, (uint)a));
  }

  public static Integer operator^(uint a, Integer b)
  {
    return b.sign == -1 ? new Integer(a) ^ b : new Integer(1, BitXor(b.data, b.length, a));
  }

  public static Integer operator^(long a, Integer b)
  {
    return a < 0 || b.sign == -1 ? new Integer(a) ^ b : new Integer(1, BitXor(b.data, b.length, (ulong)a));
  }

  public static Integer operator^(ulong a, Integer b)
  {
    return b.sign == -1 ? new Integer(a) ^ b : new Integer(1, BitXor(b.data, b.length, a));
  }
  #endregion

  #region Shifting
  public static Integer operator<<(Integer a, int shift)
  {
    if(a.sign == 0 || shift == 0) return a;
    return new Integer(a.sign, shift<0 ? RightShift(a.data, a.length, -shift) : LeftShift(a.data, a.length, shift));
  }
  public static Integer operator>>(Integer a, int shift)
  {
    if(a.sign == 0 || shift == 0) return a;
    return new Integer(a.sign, shift<0 ? LeftShift(a.data, a.length, -shift) : RightShift(a.data, a.length, shift));
  }
  #endregion

  #region Implicit conversion
  public static implicit operator Integer(int i) { return new Integer(i); }
  public static implicit operator Integer(uint i) { return new Integer(i); }
  public static implicit operator Integer(long i) { return new Integer(i); }
  public static implicit operator Integer(ulong i) { return new Integer(i); }
  #endregion
  #endregion

  #region IConvertible
  ulong IConvertible.ToUInt64(IFormatProvider provider)
  {
    if(sign == -1 || length > 2) throw new OverflowException();
    if(sign == 0) return 0;
    return data.Length == 1 ? data[0] : (ulong)data[1]<<32 | data[0];
  }

  sbyte IConvertible.ToSByte(IFormatProvider provider)
  {
    if(length > 1 || (sign == 1 && this > sbyte.MaxValue) || (sign == -1 && this < sbyte.MinValue))
    {
      throw new OverflowException();
    }
    return length == 0 ? (sbyte)0 : (sbyte)((int)data[0] * sign);
  }

  double IConvertible.ToDouble(IFormatProvider provider)
  {
    return ToDouble(true);
  }

  DateTime IConvertible.ToDateTime(IFormatProvider provider) { throw new InvalidCastException(); }

  float IConvertible.ToSingle(IFormatProvider provider)
  {
    float value = (float)ToDouble(false);
    if(float.IsInfinity(value)) throw new OverflowException();
    return value;
  }

  bool IConvertible.ToBoolean(IFormatProvider provider) { return sign != 0; }

  int IConvertible.ToInt32(IFormatProvider provider)
  {
    if(length == 1)
    {
      uint v = data[0];
      if(sign == 1)
      {
        if(v < 0x80000000) return (int)v;
      }
      else
      {
        if(v <= 0x80000000) return -(int)v;
      }
    }
    else if(length == 0) return 0;

    throw new OverflowException();
  }

  ushort IConvertible.ToUInt16(IFormatProvider provider)
  {
    if(length == 1)
    {
      if(data[0] <= ushort.MaxValue) return (ushort)data[0];
    }
    else if(length == 0) return 0;
    
    throw new OverflowException();
  }

  short IConvertible.ToInt16(IFormatProvider provider)
  {
    if(length == 1)
    {
      uint v = data[0];
      if(sign == 1)
      {
        if(v < 0x8000) return (short)v;
      }
      else
      {
        if(v <= 0x8000) return (short)-(int)v;
      }
    }
    else if(length == 0) return 0;

    throw new OverflowException();
  }

  string IConvertible.ToString(IFormatProvider provider) { return ToString(); }

  byte IConvertible.ToByte(IFormatProvider provider)
  {
    if(length == 1)
    {
      if(sign == 1 && data[0] <= byte.MaxValue) return (byte)data[0];
    }
    else if(length == 0) return 0;

    throw new OverflowException();
  }

  char IConvertible.ToChar(IFormatProvider provider)
  {
    if(length == 1)
    {
      if(sign == 1 && data[0] <= (uint)char.MaxValue) return (char)data[0];
    }
    else if(length == 0) return '\0';
    
    throw new OverflowException();
  }

  long IConvertible.ToInt64(IFormatProvider provider)
  {
    if(length == 2)
    {
      uint v = data[1];
      if(sign == 1)
      {
        if(v < 0x80000000) return (long)((ulong)v<<32 | data[0]);
      }
      else
      {
        if(v < 0x80000000 || v == 0x80000000 && data[0] == 0) return -(long)((ulong)v<<32 | data[0]);
      }
    }
    else if(length == 1) return sign == 1 ? (long)data[0] : -(long)data[0];
    else if(length == 0) return 0;

    throw new OverflowException();
  }

  TypeCode IConvertible.GetTypeCode()
  {
    return TypeCode.Object;
  }

  decimal IConvertible.ToDecimal(IFormatProvider provider)
  {
    return new decimal(ToDouble(true));
  }

  object IConvertible.ToType(Type conversionType, IFormatProvider provider)
  {
    return Utilities.ToType(this, conversionType, provider);
  }

  uint IConvertible.ToUInt32(IFormatProvider provider)
  {
    if(length == 1) return data[0];
    else if(length == 0) return 0;
    else throw new OverflowException();
  }

  internal double ToDouble(bool throwOnInfinity)
  {
    if(length == 0) return 0.0;

    double value = 0;
    for(int i=length-1; i>=0; i--)
    {
      value = value*4294967296.0 + data[i];
    }
    if(throwOnInfinity && double.IsInfinity(value)) throw new OverflowException();
    if(sign == -1) value = -value;
    return value;
  }

  public static byte ToByte(Integer i) { return ((IConvertible)i).ToByte(null); }
  public static decimal ToDecimal(Integer i) { return ((IConvertible)i).ToDecimal(null); }
  public static double ToDouble(Integer i) { return i.ToDouble(true); }
  public static double ToDouble(Integer i, bool throwOnInfinity) { return i.ToDouble(throwOnInfinity); }
  public static float ToSingle(Integer i) { return ((IConvertible)i).ToSingle(null); }
  public static float ToSingle(Integer i, bool throwOnInfinity)
  {
    return throwOnInfinity ? ToSingle(i) : (float)i.ToDouble(false);
  }
  public static short ToInt16(Integer i) { return ((IConvertible)i).ToInt16(null); }
  public static int ToInt32(Integer i) { return ((IConvertible)i).ToInt32(null); }
  public static long ToInt64(Integer i) { return ((IConvertible)i).ToInt64(null); }
  public static ushort ToUInt16(Integer i) { return ((IConvertible)i).ToUInt16(null); }
  public static uint ToUInt32(Integer i) { return ((IConvertible)i).ToUInt32(null); }
  public static ulong ToUInt64(Integer i) { return ((IConvertible)i).ToUInt64(null); }
  public static sbyte ToSByte(Integer i) { return ((IConvertible)i).ToSByte(null); }
  #endregion

  #region IComparable Members
  public int CompareTo(object obj)
  {
    if(obj is Integer) return CompareTo((Integer)obj);
    throw new ArgumentException();
  }

  public int CompareTo(Integer o)
  {
    if(sign != o.sign) return sign - o.sign;
    int len = length, olen = o.length;
    if(len != olen) return len - olen;

    for(int i=len-1; i>=0; i--)
    {
      if(data[i] != o.data[i])
      {
        return (int)(data[i]-o.data[i]) * sign;
      }
    }

    return 0;
  }

  public int CompareTo(int i)
  {
    int osign = Math.Sign(i);
    if(sign != osign) return sign - osign;

    switch(length)
    {
      case 1: return UintCompare(data[0], IntToUint(i)) * sign;
      case 0: return 0; // 'i' can't be nonzero here because the 'sign != osign' check above would have caught it
      default: return sign;
    }
  }

  public int CompareTo(uint i)
  {
    int osign = i == 0 ? 0 : 1;
    if(sign != osign) return sign - osign;
    return length==1 ? UintCompare(data[0], i) : length;
  }

  public int CompareTo(long i)
  {
    int osign = Math.Sign(i);
    if(sign != osign) return sign-osign;

    switch(length)
    {
      case 2: return UlongCompare(((ulong)data[1]<<32) | data[0], LongToUlong(i)) * sign;
      case 1:
      {
        ulong ulvalue = LongToUlong(i);
        return (uint)(ulvalue>>32) == 0 ? UintCompare(data[0], (uint)ulvalue)*sign : -sign;
      }
      case 0: return 0; // 'i' can't be nonzero here because the 'sign != osign' check above would have caught it
      default: return sign;
    }
  }

  public int CompareTo(ulong i)
  {
    int osign = i == 0 ? 0 : 1;
    if(sign != osign) return sign-osign;

    switch(length)
    {
      case 2: return UlongCompare(((ulong)data[1]<<32) | data[0], i);
      case 1: return (i>>32)==0 ? UintCompare(data[0], (uint)i) : -1;
      case 0: return 0; // 'i' can't be nonzero here because the 'sign != osign' check above would have caught it
      default: return 1;
    }
  }

  public static int Compare(Integer a, Integer b)
  {
    return a.CompareTo(b);
  }
  #endregion

  #region ICloneable Members
  public object Clone() { return new Integer(sign, (uint[])data.Clone()); }
  #endregion

  internal uint[] GetInternalData()
  {
    if(data == null || data.Length == length)
    {
      return data;
    }
    else
    {
      uint[] trimmedData = new uint[length];
      Array.Copy(data, trimmedData, length);
      return trimmedData;
    }
  }

  int AbsCompareTo(Integer o)
  {
    return AbsCompare(data, length, o.data, o.length);
  }

  int AbsCompareTo(uint i)
  {
    switch(length)
    {
      case 1: return UintCompare(data[0], i);
      case 0: return i == 0 ? 0 : -1;
      default: return 1;
    }
  }

  static int AbsCompare(uint[] a, int alength, uint[] b, int blength)
  {
    if(alength != blength) return alength - blength;

    for(int i=alength-1; i>=0; i--)
    {
      if(a[i] != b[i]) return (int)(a[i]-b[i]);
    }
    return 0;
  }

  Integer Squared() { return this*this; } // TODO: this can be optimized much better

  uint[] data;
  ushort length;
  short sign;

  static unsafe uint[] Add(uint[] a, int alen, uint b)
  {
    uint[] newData = new uint[alen+1];
    fixed(uint* aptr=a, dptr=newData)
    {
      ulong sum = b;
      int i;
      for(i=0; i<alen && (uint)sum!=0; i++)
      {
        sum += aptr[i];
        dptr[i] = (uint)sum;
        sum >>= 32;
      }
      if((uint)sum != 0)
      {
        dptr[alen] = (uint)sum;
      }
      else
      {
        for(; i<alen; i++) dptr[i] = aptr[i];
      }
    }
    return newData;
  }

  static unsafe uint[] Add(uint[] a, int alen, uint[] b, int blen)
  {
    Debug.Assert(alen >= blen);

    uint[] newData = new uint[alen+1];
    fixed(uint* aptr=a, bptr=b, dptr=newData)
    {
      ulong sum = 0;
      int i;
      for(i=0; i<blen; i++)
      {
        sum += (ulong)aptr[i] + (ulong)bptr[i];
        dptr[i] = (uint)sum;
        sum >>= 32;
      }
      for(; i<alen && (uint)sum!=0; i++)
      {
        sum += aptr[i];
        dptr[i] = (uint)sum;
        sum >>= 32;
      }
      if((uint)sum != 0)
      {
        dptr[alen] = (uint)sum;
      }
      else
      {
        for(; i<alen; i++) dptr[i] = aptr[i];
      }
    }
    return newData;
  }

  static uint[] BitAnd(uint[] a, int alen, uint b)
  {
    return alen == 0 ? Zero.data : new uint[1] { a[0] & b };
  }

  static uint[] BitAnd(uint[] a, int alen, ulong b)
  {
    uint[] ret;
    if(alen > 1)
    {
      ret = new uint[2];
      ret[0] = a[0] & (uint)b;
      ret[1] = a[1] & (uint)(b>>32);
    }
    else if(alen != 0) // alen == 1
    {
      ret    = new uint[1];
      ret[0] = a[0] & (uint)b;
    }
    else
    {
      ret = Zero.data;
    }
    return ret;
  }

  static unsafe uint[] BitAnd(uint[] a, int alen, uint[] b, int blen)
  {
    int newLen = Math.Min(alen, blen);
    uint[] newData = new uint[newLen];
    fixed(uint* aptr=a, bptr=b, dptr=newData)
    {
      for(int i=0; i<newLen; i++) dptr[i] = aptr[i] & bptr[i];
    }
    return newData;
  }

  static unsafe uint[] BitAnd(uint[] a, int alen, bool aneg, uint[] b, int blen, bool bneg)
  {
    if(alen < blen) // swap if necessary so that alen >= blen
    {
      Ops.Swap(ref a, ref b);
      Ops.Swap(ref alen, ref blen);
      Ops.Swap(ref aneg, ref bneg);
    }

    uint[] newData = new uint[blen];
    fixed(uint* aptr=a, bptr=b, dptr=newData)
    {
      int i;
      bool aWasNonZero = false, bWasNonZero = false;
      for(i=0; i<blen; i++)
      {
        dptr[i] = GetBitwise(aneg, aptr[i], ref aWasNonZero) & GetBitwise(bneg, bptr[i], ref bWasNonZero);
      }
      if(bneg) for(; i<alen; i++) dptr[i] = GetBitwise(aneg, aptr[i], ref aWasNonZero);
    }
    return newData;
  }

  static uint[] BitOr(uint[] a, int alen, uint b)
  {
    uint[] newData = (uint[])a.Clone();
    if(alen != 0) newData[0] |= b;
    return newData;
  }

  static uint[] BitOr(uint[] a, int alen, ulong b)
  {
    uint[] newData = (uint[])a.Clone();
    if(alen > 1)
    {
      newData[0] |= (uint)b;
      newData[1] |= (uint)(b>>32);
    }
    else if(alen != 0)
    {
      newData[0] |= (uint)b;
    }
    return newData;
  }

  static unsafe uint[] BitOr(uint[] a, int alen, uint[] b, int blen)
  {
    if(alen < blen)
    {
      Ops.Swap(ref a, ref b);
      Ops.Swap(ref alen, ref blen);
    }

    uint[] newData = (uint[])a.Clone();
    fixed(uint* bptr=b, dptr=newData)
    {
      for(int i=0; i<blen; i++) dptr[i] |= bptr[i];
    }
    return newData;
  }

  static unsafe uint[] BitOr(uint[] a, int alen, bool aneg, uint[] b, int blen, bool bneg)
  {
    if(alen<blen)
    {
      Ops.Swap(ref a, ref b);
      Ops.Swap(ref alen, ref blen);
      Ops.Swap(ref aneg, ref bneg);
    }

    uint[] newData = new uint[alen];
    fixed(uint* aptr=a, bptr=b, dptr=newData)
    {
      bool aWasNonZero=false, bWazNonZero=false;
      int i;
      for(i=0; i<blen; i++)
      {
        dptr[i] = GetBitwise(aneg, aptr[i], ref aWasNonZero) | GetBitwise(bneg, bptr[i], ref bWazNonZero);
      }

      if(bneg)
      {
        for(; i<alen; i++) dptr[i] = uint.MaxValue;
      }
      else
      {
        for(; i<alen; i++) dptr[i] = GetBitwise(aneg, aptr[i], ref aWasNonZero);
      }
    }
    return newData;
  }

  static unsafe uint[] BitNegate(uint[] data, int length)
  {
    uint[] newData = (uint[])data.Clone();
    fixed(uint* dptr=newData)
    {
      for(int i=0; i<length; i++)
      {
        dptr[i] = ~dptr[i];
      }
    }
    return newData;
  }

  static uint[] BitXor(uint[] a, int alen, uint b)
  {
    uint[] newData = (uint[])a.Clone();
    if(alen != 0) newData[0] ^= b;
    return newData;
  }

  static uint[] BitXor(uint[] a, int alen, ulong b)
  {
    uint[] newData = (uint[])a.Clone();
    if(alen > 1)
    {
      newData[0] ^= (uint)b;
      newData[1] ^= (uint)(b>>32);
    }
    else if(alen != 0)
    {
      newData[0] ^= (uint)b;
    }
    return newData;
  }

  static unsafe uint[] BitXor(uint[] a, int alen, uint[] b, int blen)
  {
    if(alen < blen)
    {
      Ops.Swap(ref a, ref b);
      Ops.Swap(ref alen, ref blen);
    }

    uint[] newData = (uint[])a.Clone();
    fixed(uint* bptr=b, dptr=newData)
    {
      for(int i=0; i<blen; i++) dptr[i] ^= bptr[i];
    }
    return newData;
  }

  static unsafe uint[] BitXor(uint[] a, int alen, bool aneg, uint[] b, int blen, bool bneg)
  {
    if(alen < blen)
    {
      Ops.Swap(ref a, ref b);
      Ops.Swap(ref alen, ref blen);
      Ops.Swap(ref aneg, ref bneg);
    }

    uint[] newData = new uint[alen];
    fixed(uint* aptr=a, bptr=b, dptr=newData)
    {
      bool aWasNonZero=false, bWasNonZero=false;
      int i;

      for(i=0; i<blen; i++)
      {
        dptr[i] = GetBitwise(aneg, aptr[i], ref aWasNonZero) ^ GetBitwise(bneg, bptr[i], ref bWasNonZero);
      }
      if(bneg)
      {
        for(; i<alen; i++) dptr[i] = ~GetBitwise(aneg, aptr[i], ref aWasNonZero);
      }
      else
      {
        for(; i<alen; i++) dptr[i] = GetBitwise(aneg, aptr[i], ref aWasNonZero);
      }
    }
    return newData;
  }

  static int CalculateLength(uint[] data)
  {
    if(data == null) return 0;
    int index = data.Length-1;
    while(index >= 0 && data[index] == 0) index--;
    return index+1;
  }

  static unsafe uint[] Divide(uint[] a, int alen, uint b, out uint remainder) // assumes b>0
  {
    uint[] d = new uint[alen];

    fixed(uint* ab=a, db=d)
    {
      ulong rem=0;
      while(alen-- != 0)
      {
        rem = (rem<<32) | ab[alen];
        d[alen] = (uint)(rem/b); // it'd be nice to combine rem/b and rem%b into one operation,
        rem %= b;                // but Math.DivRem() doesn't support unsigned longs
      }
      remainder = (uint)rem;
    }
    return d;
  }

  // this algorithm was shamelessly adapted from Mono.Math
  static unsafe uint[] Divide(uint[] a, int alen, uint[] b, int blen, out uint[] remainder)
  {
    Debug.Assert(blen != 0 && AbsCompare(a, alen, b, blen) > 0);

    if(blen == 1) // the algorithm below assumes blen >= 2 and is slower anyway
    {
      remainder = new uint[1];
      return Divide(a, alen, b[0], out remainder[0]);
    }

    int remLen = alen+1, dataLen = blen+1;

    fixed(uint* aptr=a)
    {
      uint[] newData, remain;
      
      int shift = 0, rpos = alen-blen;
      {
        uint mask = 0x80000000, val = b[blen-1];
        while(mask != 0 && (val&mask)==0) { shift++; mask>>=1; }
        Debug.Assert(shift < 32);

        newData = new uint[rpos+1];
        if(shift == 0)
        {
          remain = (uint[])a.Clone();
        }
        else
        {
          remain = LeftShift(a, alen, shift);
          b      = LeftShift(b, blen, shift);
          blen   = CalculateLength(b);
        }
      }

      fixed(uint* bptr=b, dptr=newData, rptr=remain)
      {
        int j = remLen-blen, pos = remLen-1;
        uint   firstdb = bptr[blen-1];
        ulong seconddb = bptr[blen-2];

        for(; j != 0; j--)
        {
          ulong dividend = ((ulong)rptr[pos]<<32) + rptr[pos-1], qhat=dividend/firstdb, rhat=dividend%firstdb;

          while(qhat==0x100000000 || qhat*seconddb > (rhat<<32)+rptr[pos-2])
          {
            qhat--;
            rhat += firstdb;
            if(rhat >= 0x100000000) break;
          }

          uint uiqhat = (uint)qhat;
          int dpos = 0, npos = pos-dataLen+1;

          rhat = 0; // commandeering this variable
          do
          {
            rhat += (ulong)bptr[dpos] * uiqhat;
            uint t = rptr[npos];
            rptr[npos] -= (uint)rhat;
            rhat >>= 32;
            if(rptr[npos] > t) rhat++;
            npos++;
          } while(++dpos < dataLen);

          npos = pos-dataLen+1;
          dpos = 0;

          if(rhat != 0)
          {
            uiqhat--; rhat=0;
            do
            {
              rhat += (ulong)rptr[npos] + bptr[dpos];
              rptr[npos] = (uint)rhat;
              rhat >>= 32;
              npos++;
            } while(++dpos < dataLen);
          }

          dptr[rpos--] = uiqhat;
          pos--;
        }
      }

      remainder = shift == 0 ? remain : RightShift(remain, remLen, shift);
      return newData;
    }
  }

  static unsafe int DivideInPlace(uint[] data, int length, uint n) // only used for conversion to string
  {
    ulong rem = 0;
    fixed(uint* dptr=data)
    {
      while(length-- != 0)
      {
        rem = (rem<<32) | dptr[length];
        dptr[length] = (uint)(rem/n);
        rem %= n;
      }
    }
    return (int)(uint)rem;
  }

  static uint SignExtend(uint value, ref bool valueWasNonZero)
  {
    if(valueWasNonZero) // if we've seen a non-zero value from this array already, just bitnot the value
    {
      return ~value;
    }
    else if(value == 0)
    {
      return 0;
    }
    else // this is the first nonzero value from the array. set the marker to true and return the two's complement
    {
      valueWasNonZero = true;
      return ~value + 1;
    }
  }

  /// <summary>Gets a data value, possibly sign-extending it if the source was negative.</summary>
  /// <remarks>This is used to simulate a finite-sized, two's complement number for bitwise operations.</remarks>
  static uint GetBitwise(bool negative, uint value, ref bool sourceWasNonZero)
  {
    return negative ? SignExtend(value, ref sourceWasNonZero) : value;
  }

  static bool IsValidDigit(char c, int radix)
  {
    if(c >= '0' && c <= '9')
    {
      return c-'0' < radix;
    }
    else
    {
      return c-'A'+10 < radix;
    }
  }

  static uint IntToUint(int i)
  {
    return (uint)(i < 0 ? -i : i);
  }

  static ulong LongToUlong(long i)
  {
    return (ulong)(i < 0 ? -i : i);
  }

  static unsafe uint[] LeftShift(uint[] a, int alen, int shift)
  {
    Debug.Assert(shift > 0);

    int whole = shift>>5, newLen = alen+whole+1;
    uint[] newData = new uint[newLen];
    shift &= 31;

    fixed(uint* aptr=a, dptr=newData)
    {
      if(shift == 0)
      {
        for(int i=0; i<newLen; i++) dptr[i+whole] = aptr[i];
      }
      else
      {
        uint carry = 0;
        int inverseShift = 32-shift;
        for(int i=0; i<newLen; i++)
        {
          uint v = aptr[i];
          dptr[i+whole] = (v<<shift) | carry;
          carry = v>>inverseShift;
        }
      }
    }

    return newData;
  }

  static unsafe uint[] RightShift(uint[] a, int alen, int shift)
  {
    Debug.Assert(shift > 0);

    int whole = shift>>5, newLen = alen-whole;
    uint[] newData = new uint[newLen+1];
    shift &= 31;

    fixed(uint* aptr=a, dptr=newData)
    {
      if(shift == 0)
      {
        while(newLen-- != 0) dptr[newLen] = aptr[newLen+whole];
      }
      else
      {
        uint carry=0;
        int inverseShift = 32-shift;
        while(newLen-- != 0)
        {
          uint v = aptr[newLen+whole];
          dptr[newLen] = (v>>shift) | carry;
          carry = v << inverseShift;
        }
      }
    }

    return newData;
  }

  static unsafe uint[] Multiply(uint[] a, int alen, uint b)
  {
    uint[] newData = new uint[alen+1];
    fixed(uint* aptr=a, dptr=newData)
    {
      ulong carry=0;
      int i = 0;
      for(; i<alen; i++)
      {
        carry += (ulong)aptr[i] * (ulong)b;
        dptr[i] = (uint)carry;
        carry >>= 32;
      }
      dptr[i] = (uint)carry;
    }
    return newData;
  }

  // TODO: this is a rather naive algorithm. optimize it.
  static unsafe uint[] Multiply(uint[] a, int alen, uint[] b, int blen)
  {
    uint[] newData = new uint[alen+blen];
    fixed(uint* origAptr=a, origBptr=b, origDptr=newData)
    {
      uint* aptr=origAptr, aend=aptr+alen, bend=origBptr+blen, dptr=origDptr;
      for(; aptr<aend; dptr++,aptr++)
      {
        if(*aptr == 0) continue;

        ulong carry = 0;
        uint* dp = dptr;
        for(uint* bp=origBptr; bp<bend; dp++,bp++)
        {
          carry += (ulong)*aptr * (ulong)*bp + *dp;
          *dp = (uint)carry;
          carry >>= 32;
        }
        if(carry != 0) *dp=(uint)carry;
      }
    }
    return newData;
  }

  static uint[] TryResize(uint[] array, int lengthNeeded)
  {
    if(array.Length >= lengthNeeded) return array;
    uint[] newData = new uint[lengthNeeded];
    Array.Copy(array, newData, array.Length);
    return newData;
  }

  static unsafe uint[] Subtract(uint a, uint[] b, int blen)
  {
    return new uint[1] { blen == 0 ? a : a-b[0] }; // assumes avalue >= bvalue
  }

  static unsafe uint[] Subtract(uint[] a, int alen, uint b) // assumes avalue >= bvalue
  {
    uint[] newData = (uint[])a.Clone();
    fixed(uint* dptr=newData)
    {
      bool borrow = b > dptr[0];
      dptr[0] -= b;
      if(borrow)
      {
        for(int i=1; i<alen; i++) if(dptr[i]-- != 0) break;
      }
    }
    return newData;
  }

  static unsafe uint[] Subtract(uint[] a, int alen, uint[] b, int blen) // assumes avalue >= bvalue
  {
    Debug.Assert(AbsCompare(a, alen, b, blen) >= 0);

    uint[] newData = new uint[alen];
    fixed(uint* aptr=a, bptr=b, dptr=newData)
    {
      int i;
      uint ai, bi;
      bool borrow = false;

      for(i=0; i<blen; i++)
      {
        ai = aptr[i];
        bi = bptr[i];
        if(borrow)
        {
          if(ai == 0) ai=0xffffffff;
          else borrow = bi > --ai;
        }
        else if(bi > ai)
        {
          borrow = true;
        }
        dptr[i] = ai-bi;
      }

      if(borrow)
      {
        for(; i<alen; i++)
        {
          ai = aptr[i];
          dptr[i] = ai-1;
          if(ai != 0) { i++; break; }
        }
      }

      for(; i<alen; i++) dptr[i] = aptr[i];
    }

    return newData;
  }

  static unsafe uint[] TwosComplement(uint[] data)
  {
    fixed(uint* dptr=data)
    {
      int i;
      for(i=data.Length-1; i>=0 && dptr[i] == 0; i--)
      {
        dptr[i] = 0xffffffff;
      }

      if(i >= 0) // then negate until the end
      {
        for(; i >= 0; i--) dptr[i] = ~dptr[i]+1;
      }
      else // otherwise, all bits were set to one, so the two's complement is going to be one larger
      {
        data = TryResize(data, data.Length+1);
        data[data.Length] = 1;
      }
    }
    return data;
  }

  static int UintCompare(uint a, uint b) { return a>b ? 1 : a<b ? -1 : 0; }
  static int UlongCompare(ulong a, ulong b) { return a>b ? 1 : a<b ? -1 : 0; }
}

} // namespace Scripting.Runtime