using System;
using Scripting.AST;

namespace Scripting.Runtime
{

public static class Ops
{
  #region Numeric operations
  public static object Add(int a, int b)
  {
    try { return checked(a+b); }
    catch(OverflowException) { return (long)a + (long)b; }
  }

  public static object Add(long a, long b)
  {
    try { return checked(a+b); }
    catch(OverflowException) { return new Integer(a) + b; }
  }
  
  public static object Add(uint a, uint b)
  {
    try { return checked(a+b); }
    catch(OverflowException) { return (ulong)a + (ulong)b; }
  }

  public static object Multiply(int a, int b)
  {
    try { return checked(a*b); }
    catch(OverflowException) { return (long)a * (long)b; }
  }

  public static object Multiply(long a, long b)
  {
    try { return checked(a*b); }
    catch(OverflowException) { return new Integer(a) * b; }
  }

  public static object Multiply(uint a, uint b)
  {
    try { return checked(a*b); }
    catch(OverflowException) { return (ulong)a * (ulong)b; }
  }

  public static object Multiply(ulong a, ulong b)
  {
    try { return checked(a*b); }
    catch(OverflowException) { return new Integer(a) * b; }
  }

  public static object Add(ulong a, ulong b)
  {
    try { return checked(a+b); }
    catch(OverflowException) { return new Integer(a) + b; }
  }

  public static object Subtract(int a, int b)
  {
    try { return checked(a-b); }
    catch(OverflowException) { return (long)a - (long)b; }
  }

  public static object Subtract(long a, long b)
  {
    try { return checked(a-b); }
    catch(OverflowException) { return new Integer(a) - b; }
  }

  public static object Subtract(uint a, uint b)
  {
    try { return checked(a-b); }
    catch(OverflowException) { return (ulong)a - (ulong)b; }
  }

  public static object Subtract(ulong a, ulong b)
  {
    try { return checked(a-b); }
    catch(OverflowException) { return new Integer(a) - b; }
  }
  #endregion
  
  #region Type conversion
  public static object ConvertTo(object obj, Type type)
  {
    if(obj == null && !type.IsValueType) return null;
    if(obj != null && type.IsAssignableFrom(obj.GetType())) return obj;
    return Convert.ChangeType(obj, type);
  }
  #endregion
  
  public static void Swap<T>(ref T a, ref T b)
  {
    T temp = a;
    a = b;
    b = a;
  }

  public readonly static object[] EmptyArray = new object[0];
}

} // namespace Scripting.Runtime