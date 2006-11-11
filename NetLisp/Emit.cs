using System;
using System.Reflection.Emit;
using Scripting.Emit;
using NetLisp.Runtime;

namespace NetLisp.Emit
{

public class LispCodeGenerator : CodeGenerator
{
  public LispCodeGenerator(TypeGenerator typeGen, IMethodBase method, ILGenerator ilGen)
    : base(typeGen, method, ilGen) { }

  public override bool CanEmitConstant(Type type)
  {
    return type == typeof(Symbol) || base.CanEmitConstant(type);
  }

  public override void EmitConstant(object value)
  {
    Symbol symbol = value as Symbol;
    if(symbol != null)
    {
      EmitString(symbol.Name);
      EmitCall(typeof(Symbol), "Get");
    }
    else
    {
      base.EmitConstant(value);
    }
  }
}

} // namespace NetLisp.Emit