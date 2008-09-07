using System;
using System.Collections.Generic;

namespace NetLisp.Runtime
{

#region LispFunctionTemplate
public sealed class LispFunctionTemplate : Scripting.Runtime.FunctionTemplate
{
  public LispFunctionTemplate(IntPtr funcPtr, string name, string[] parameterNames, Type[] parameterTypes,
                              int numRequired, bool hasListParam, bool hasDictParam)
    : base(funcPtr, name, parameterNames, parameterTypes, numRequired, hasListParam, hasDictParam) { }

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

#region LispSymbol
public sealed class LispSymbol
{
  public LispSymbol(string name) { Name = name; }

  public readonly string Name;
  public override string ToString() { return Name; }

  public static LispSymbol Get(string name)
  {
    LispSymbol sym;
    lock(table)
    {
      if(!table.TryGetValue(name, out sym)) table[name] = sym = new LispSymbol(name);
    }
    return sym;
  }

  static readonly Dictionary<string,LispSymbol> table = new Dictionary<string,LispSymbol>();
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

} // namespace NetLisp.Runtime