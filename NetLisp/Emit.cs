using System;
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
    topSlot = new TopLevelSlot(name);
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
    return type == typeof(LispSymbol) || base.CanEmitConstant(type);
  }

  public override void EmitConstant(object value)
  {
    LispSymbol symbol = value as LispSymbol;
    if(symbol != null)
    {
      EmitString(symbol.Name);
      EmitCall(typeof(LispSymbol), "Get");
    }
    else
    {
      base.EmitConstant(value);
    }
  }
}
#endregion

} // namespace NetLisp.Emit