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
  
  public static Binding CheckBinding(Binding binding)
  {
    if(binding.Value == Binding.Unbound) throw new UndefinedVariableException(binding.Name);
    return binding;
  }

  public static ICallable ConvertToCallable(object obj)
  {
    ICallable call = obj as ICallable;
    if(call != null) return call;
    
    Delegate del = obj as Delegate;
    if(del != null) return MakeCallable(del);

    throw CantConvert(obj, typeof(ICallable));
  }

  public static ICallable MakeCallable(Delegate del)
  {
    throw new NotImplementedException();
  }

  public static void Swap<T>(ref T a, ref T b)
  {
    T temp = a;
    a = b;
    b = a;
  }

  public static string TypeName(object obj)
  {
    return obj == null ? "[NULL]" : TypeName(obj.GetType());
  }
  
  public static string TypeName(Type type)
  {
    return type.FullName;
  }

  static InvalidCastException CantConvert(object obj, Type type)
  {
    return new InvalidCastException("Can't convert "+TypeName(obj)+" to "+TypeName(type));
  }

  public readonly static object[] EmptyArray = new object[0];
}

} // namespace Scripting.Runtime