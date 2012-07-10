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
using System.Reflection.Emit;
using Scripting.Emit;
using NetLisp.Runtime;

namespace NetLisp.Emit
{

#region StaticTopLevelSlot
public sealed class StaticTopLevelSlot : ProxySlot
{
  public StaticTopLevelSlot(string name, ITypeInfo type) : base(name, type) { }

  public override bool CanGetAddr
  {
    get { return true; }
  }

  public override bool CanRead
  {
    get { return true; }
  }

  public override bool CanWrite
  {
    get { return true; }
  }

  public override void EmitSet(CodeGenerator cg, Scripting.AST.ASTNode valueNode, bool initialize)
  {
    // we still need to update the real toplevel environment on set, so set both the static field and the binding
    ITypeInfo typeOnStack = valueNode.Emit(cg);
    cg.EmitDup();
    base.EmitSet(cg, typeOnStack, initialize);
    topSlot.EmitSet(cg, typeOnStack, initialize);
  }

  public override void EmitSet(CodeGenerator cg, ITypeInfo typeOnStack, bool initialize)
  {
    cg.EmitDup();
    base.EmitSet(cg, typeOnStack, initialize);
    topSlot.EmitSet(cg, typeOnStack, initialize);
  }

  public override void EmitSet(CodeGenerator cg, Slot valueSlot, bool initialize)
  {
    valueSlot.EmitGet(cg);
    cg.EmitDup();
    base.EmitSet(cg, valueSlot.Type, initialize);
    topSlot.EmitSet(cg, valueSlot.Type, initialize);
  }

  public override bool IsSameAs(Slot other)
  {
    StaticTopLevelSlot otherSlot = other as StaticTopLevelSlot;
    return otherSlot != null && string.Equals(Name, otherSlot.Name, StringComparison.Ordinal);
  }

  protected override Slot CreateSlot(CodeGenerator cg, string name, ITypeInfo type)
  {
    topSlot = new TopLevelSlot(name, true);
    return cg.Assembly.GetPrivateClass().DefineStaticField("top$"+name, type);
  }

  TopLevelSlot topSlot;
}
#endregion

#region LispCodeGenerator
public class LispCodeGenerator : CodeGenerator
{
  public LispCodeGenerator(TypeGenerator typeGen, IMethodBase method, ILGenerator ilGen)
    : base(typeGen, method, ilGen) { }

  public override bool CanEmitConstant(Type type)
  {
    return type == typeof(LispSymbol) || type == typeof(Pair) || base.CanEmitConstant(type);
  }

  public override void EmitConstant(object value)
  {
    LispSymbol symbol = value as LispSymbol;
    if(symbol != null)
    {
      EmitString(symbol.Name);
      EmitCall(typeof(LispSymbol), "Get");
    }
    else if(value is Pair)
    {
      Pair pair = (Pair)value;
      List<object> objects = new List<object>();
      do
      {
        objects.Add(pair.Car);
        Pair next = pair.Cdr as Pair;
        if(next == null) objects.Add(pair.Cdr);
        pair = next;
      } while(pair != null);

      System.Reflection.ConstructorInfo pairCons =
        typeof(Pair).GetConstructor(new Type[] { typeof(object), typeof(object) });

      Slot temp = AllocLocalTemp(TypeWrapper.Object);
      EmitConstant(objects[objects.Count-1]);
      temp.EmitSet(this);
      for(int i=objects.Count-2; i >= 0; i--)
      {
        EmitConstant(objects[i]);
        temp.EmitGet(this);
        EmitNew(pairCons);
        if(i != 0) temp.EmitSet(this);
      }
      FreeLocalTemp(temp);
    }
    else
    {
      base.EmitConstant(value);
    }
  }
}
#endregion

#region MultipleValuesType
public class MultipleValuesType : TypeWrapperBase
{
  public MultipleValuesType(params ITypeInfo[] valueTypes) : base(typeof(MultipleValues))
  {
    if(valueTypes == null) throw new ArgumentNullException();
    if(valueTypes.Length < 2) throw new ArgumentException("Less than two types were given.");
    ValueTypes = valueTypes;
  }

  public readonly ITypeInfo[] ValueTypes;
}
#endregion

} // namespace NetLisp.Emit