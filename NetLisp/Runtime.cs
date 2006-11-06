using System;
using System.Collections.Generic;

namespace NetLisp.Runtime
{

#region LispFunctionTemplate
public sealed class LispFunctionTemplate : Scripting.Runtime.FunctionTemplate
{
  public LispFunctionTemplate(IntPtr funcPtr, string name, string[] parameterNames, Type[] parameterTypes,
                              int numRequired, bool hasListParam, bool hasDictParam, bool hasArgClosure)
    : base(funcPtr, name, parameterNames, parameterTypes, numRequired, hasListParam, hasDictParam, hasArgClosure) { }

  protected override object PackArguments(object[] arguments, int index, int length)
  {
    if(length > 1)
    {
      return LispOps.List(arguments, index, length);
    }
    else if(length == 0)
    {
      return null;
    }
    else
    {
      return new Pair(arguments[index], null);
    }
  }
}
#endregion

#region LispOps
public static class LispOps
{
  public static Pair List(object[] items, int start, int length)
  {
    Pair pair = null;
    for(int i=start+length-1; i >= start; i--) pair = new Pair(items[i], pair);
    return pair;
  }
}
#endregion

#region Pair
public sealed class Pair
{
  public Pair()
  {
    Car = Cdr = null;
  }

  public Pair(object car, object cdr)
  {
    Car = car;
    Cdr = cdr;
  }

  public object Car, Cdr;
}
#endregion

#region Symbol
public sealed class Symbol
{
  public Symbol(string name) { Name = name; }

  public readonly string Name;
  public override string ToString() { return Name; }

  public static Symbol Get(string name)
  {
    Symbol sym;
    lock(table)
    {
      if(!table.TryGetValue(name, out sym)) table[name] = sym = new Symbol(name);
    }
    return sym;
  }

  static readonly Dictionary<string,Symbol> table = new Dictionary<string,Symbol>();
}
#endregion

} // namespace NetLisp.Runtime