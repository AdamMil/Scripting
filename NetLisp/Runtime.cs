/*
NetLisp is the reference implementation for a language similar to
Scheme, also called NetLisp. This implementation is both interpreted
and compiled, targetting the Microsoft .NET Framework.

http://www.adammil.net/
Copyright (C) 2007-2008 Adam Milazzo

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.
This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
*/

using System;
using System.Collections.Generic;
using Scripting;
using Scripting.Runtime;

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
      return LispOps.List(arguments, null, index, length);
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
  public static readonly Singleton EOF = new Singleton("#<eof>"), Void = new Singleton("#<void>");

  public static object GetSingleValue(object obj)
  {
    MultipleValues mv = obj as MultipleValues;
    return mv != null ? mv.Values[0] : obj;
  }

  public static MultipleValues ExpectValues(object obj, int count)
  {
    MultipleValues mv = obj as MultipleValues;
    if(mv == null) throw new MultipleValuesException(count, 1);
    else return ExpectValues(mv, count);
  }

  public static MultipleValues ExpectValues(MultipleValues values, int count)
  {
    if(values.Values.Length < count) throw new MultipleValuesException(count, values.Values.Length);
    return values;
  }

  public static Pair List(params object[] items)
  {
    return List(items, null, 0, items.Length);
  }

  public static Pair List(IList<object> items)
  {
    return List(items, null, 0, items.Count);
  }

  public static Pair List(IList<object> items, object dottedItem)
  {
    return List(items, dottedItem, 0, items.Count);
  }

  public static Pair List(IList<object> items, object dottedItem, int start, int length)
  {
    int i = start+length-1;
    Pair pair = new Pair(items[i], dottedItem);
    for(i--; i >= start; i--) pair = new Pair(items[i], pair);
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

#region MultipleValues
public sealed class MultipleValues
{
  public MultipleValues(object[] values)
  {
    Values = values;
  }

  public readonly object[] Values;
}
#endregion

#region MultipleValuesException
/// <summary>Thrown when an attempt is made to change a read-only variable.</summary>
public class MultipleValuesException : RuntimeScriptException
{
  public MultipleValuesException() : base("An expression produced the wrong number of values.") { }
  public MultipleValuesException(int expected, int received) :
    base("Expected " + expected.ToString() + " values, but received " + received.ToString() + ".") { }
}
#endregion

#region Pair
public sealed class Pair
{
  public Pair(object car, object cdr)
  {
    Car = car;
    Cdr = cdr;
  }

  public readonly object Car, Cdr;
}
#endregion

} // namespace NetLisp.Runtime