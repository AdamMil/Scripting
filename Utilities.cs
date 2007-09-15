using System;

namespace Scripting
{

static class Utilities
{
  /// <summary>Constructs a floating point value given a fraction and an exponent.</summary>
  /// <param name="fraction">A floating point value between -1 and 1 (exclusive).</param>
  /// <param name="exponent">An exponent from -1022 to 1024.</param>
  /// <returns>A floating point value equal to fraction * pow(2, exponent).</returns>
  public static unsafe double MakeFloat(double fraction, int exponent)
  {
    const int expShift = (64-11-1);
    const uint expMask = 0x7FF;

    if(fraction == 0) return 0;

    ulong ulvalue = *(ulong*)&fraction;
    exponent += (int)((uint)(ulvalue >> expShift) & expMask);

    if(exponent < -1022)
    {
      return 0;
    }
    else if(exponent >= expMask)
    {
      return fraction < 0 ? double.NegativeInfinity : double.PositiveInfinity;
    }

    ulvalue = ulvalue & ~((ulong)expMask << expShift) | ((ulong)exponent << expShift);
    return *(double*)&ulvalue;
  }

  /// <summary>Splits a floating point value into a fraction and an exponent.</summary>
  /// <param name="value">The floating point value to split.</param>
  /// <param name="exponent">The exponent, from -1022 to 1024.</param>
  /// <returns>A double value between -1 and 1 (exclusive).</returns>
  public static unsafe double SplitFloat(double value, out int exponent)
  {
    if(value == 0) // zero is represented in a way similar to denormalized numbers, so we have to handle it specially
    {
      exponent = 0;
      return 0;
    }

    const int expShift = (64-11-1), expBias = 1022; // the actual bias is 1023, but using 1022 avoids denormalized
    const uint expMask = 0x7FF;                     // numbers

    ulong ulvalue = *(ulong*)&value;
    exponent = (int)((uint)(ulvalue >> expShift) & expMask) - expBias;
    ulvalue = ulvalue & ~((ulong)expMask << expShift) | ((ulong)expBias << expShift);
    return *(double*)&ulvalue;
  }

  /// <summary>Helps implement <see cref="IConvertible.ToType"/>.</summary>
  public static object ToType(IConvertible convertible, Type conversionType, IFormatProvider provider)
  {
    switch(Type.GetTypeCode(conversionType))
    {
      case TypeCode.Boolean: return convertible.ToBoolean(provider);
      case TypeCode.Byte:    return convertible.ToByte(provider);
      case TypeCode.Char:    return convertible.ToChar(provider);
      case TypeCode.Decimal: return convertible.ToDecimal(provider);
      case TypeCode.Double:  return convertible.ToDouble(provider);
      case TypeCode.Int16:   return convertible.ToInt16(provider);
      case TypeCode.Int32:   return convertible.ToInt32(provider);
      case TypeCode.Int64:   return convertible.ToInt64(provider);
      case TypeCode.SByte:   return convertible.ToSByte(provider);
      case TypeCode.Single:  return convertible.ToSingle(provider);
      case TypeCode.String:  return convertible.ToString(provider);
      case TypeCode.UInt16:  return convertible.ToUInt16(provider);
      case TypeCode.UInt32:  return convertible.ToUInt32(provider);
      case TypeCode.UInt64:  return convertible.ToUInt64(provider);
      default: throw new InvalidCastException();
    }
  }
}

} // namespace Scripting
