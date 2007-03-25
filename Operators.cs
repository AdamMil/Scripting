using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Scripting.Emit;
using Scripting.Runtime;

namespace Scripting.AST
{

#region Operator
public abstract class Operator
{
  /// <param name="name">The name displayed to the user in diagnostic messages.</param>
  protected Operator(string name)
  {
    Name = name;
  }

  public readonly string Name;

  public abstract void Emit(CodeGenerator cg, IList<ASTNode> nodes, ref ITypeInfo desiredType);
  public abstract void EmitThisOperator(CodeGenerator cg);
  public abstract object Evaluate(IList<ASTNode> nodes);
  public abstract ITypeInfo GetValueType(IList<ASTNode> nodes);
  
  public object Evaluate(params object[] values)
  {
    ASTNode[] nodes = new ASTNode[values.Length];
    for(int i=0; i<nodes.Length; i++)
    {
      nodes[i] = new LiteralNode(values[i]);
    }
    return Evaluate((IList<ASTNode>)nodes);
  }

  public static readonly UncheckedAddOperator      UncheckedAdd       = new UncheckedAddOperator();
  public static readonly UncheckedSubtractOperator UncheckedSubtract  = new UncheckedSubtractOperator();
  public static readonly UncheckedMultiplyOperator UncheckedMultiply  = new UncheckedMultiplyOperator();
  public static readonly DivideOperator            Divide             = new DivideOperator();
  
  public static readonly LogicalTruthOperator LogicalTruth = new LogicalTruthOperator();
}
#endregion

#region UnaryOperator
public abstract class UnaryOperator : Operator
{
  protected UnaryOperator(string name) : base(name) { }

  public override void Emit(CodeGenerator cg, IList<ASTNode> nodes, ref ITypeInfo desiredType)
  {
    if(nodes.Count != 1) throw new ArgumentException();
    Emit(cg, nodes[0], ref desiredType);
  }

  public sealed override object Evaluate(IList<ASTNode> nodes)
  {
    if(nodes.Count != 1) throw new ArgumentException();
    return Evaluate(nodes[0].Evaluate());
  }

  public sealed override ITypeInfo GetValueType(IList<ASTNode> nodes)
  {
    if(nodes.Count != 1) throw new ArgumentException();
    return GetValueType(nodes[0]);
  }

  public abstract void Emit(CodeGenerator cg, ASTNode node, ref ITypeInfo desiredType);
  public abstract object Evaluate(object obj);
  public abstract ITypeInfo GetValueType(ASTNode node);
}
#endregion

#region LogicalTruthOperator
public class LogicalTruthOperator : UnaryOperator
{
  public LogicalTruthOperator() : base("truth") { }

  public override void Emit(CodeGenerator cg, ASTNode node, ref ITypeInfo desiredType)
  {
    ITypeInfo type = TypeWrapper.Bool;
    node.Emit(cg, ref type);

    if(type != typeof(bool)) // if the node didn't give us a nice friendly boolean, we'll have to call Evaluate()
    {
      cg.EmitSafeConversion(type, TypeWrapper.Object);
      Slot tmp = cg.AllocLocalTemp(TypeWrapper.Object);
      tmp.EmitSet(cg);
      EmitThisOperator(cg);
      tmp.EmitGet(cg);
      cg.FreeLocalTemp(tmp);
      cg.EmitCall(GetType(), "Evaluate", typeof(object));
      type = TypeWrapper.Object;
      
      if(desiredType == TypeWrapper.Bool) // if the desired type is a boolean, we can unbox the object returned by Evaluate
      {
        cg.EmitUnsafeConversion(type, desiredType);
        type = desiredType;
      }
    }

    cg.EmitRuntimeConversion(type, desiredType);
  }

  public override void EmitThisOperator(CodeGenerator cg)
  {
    cg.EmitFieldGet(typeof(Operator), "LogicalTruth");
  }

  public override object Evaluate(object obj)
  {
    return obj != null && (!(obj is bool) || (bool)obj); // null and false are false. everything else is true.
  }

  public override ITypeInfo GetValueType(ASTNode node)
  {
    return TypeWrapper.Bool;
  }
}
#endregion

public abstract class NaryOperator : Operator
{
  protected NaryOperator(string name) : base(name) { }
}

#region ArithmeticOperator
public abstract class ArithmeticOperator : NaryOperator
{
  /// <param name="opMethod">The name of the method used for operator overloading. For instance, op_Addition.</param>
  protected ArithmeticOperator(string name, string opOverload) : base(name)
  {
    if(string.IsNullOrEmpty(name) || string.IsNullOrEmpty(opOverload))
    {
      throw new ArgumentException("Name and method name must not be empty.");
    }

    this.opOverload = opOverload;
  }

  public sealed override object Evaluate(IList<ASTNode> nodes)
  {
    if(nodes.Count < 2) throw new ArgumentException();
    object value = nodes[0].Evaluate();
    for(int i=1; i<nodes.Count; i++)
    {
      value = Evaluate(value, nodes[i].Evaluate());
    }
    return value;
  }

  public abstract object Evaluate(object a, object b);

  public sealed override void Emit(CodeGenerator cg, IList<ASTNode> nodes, ref ITypeInfo desiredType)
  {
    if(nodes.Count < 2) throw new ArgumentException();

    ITypeInfo lhs = nodes[0].ValueType;
    nodes[0].Emit(cg, ref lhs);

    for(int nodeIndex=1; nodeIndex<nodes.Count; nodeIndex++)
    {
      ITypeInfo rhs = nodes[nodeIndex].ValueType;
      bool shouldImplicitlyConvertToNumeric = false;

      retry:
      TypeCode ltc = lhs.TypeCode, rtc = rhs.TypeCode;

      if(shouldImplicitlyConvertToNumeric ||
         CG.IsPrimitiveNumeric(ltc) && CG.IsPrimitiveNumeric(rtc)) // if we're dealing with primitive numeric types
      {
        ITypeInfo realLhs=lhs, realRhs=rhs; // the real object types that will be converted to numeric
        if(shouldImplicitlyConvertToNumeric)
        {
          lhs = CG.GetImplicitConversionToNumeric(lhs);
          rhs = CG.GetImplicitConversionToNumeric(rhs);
          cg.EmitSafeConversion(realLhs, lhs); // convert the left side if necessary
        }

        ITypeInfo type = GetTypeForPrimitiveNumerics(lhs, rhs);
        cg.EmitSafeConversion(lhs, type);

        nodes[nodeIndex].Emit(cg, ref realRhs);
        if(shouldImplicitlyConvertToNumeric) cg.EmitSafeConversion(realRhs, rhs); // convert the right side if necessary
        cg.EmitSafeConversion(rhs, type);
        EmitOp(cg, true);
        lhs = type;
      }
      else // at least one type is a non-primitive. check for operator overloading and implicit conversions
      {
        Overload overload = GetOperatorOverload(lhs, rhs);
        if(overload != null) // if there's an operator overload, use it.
        {
          cg.EmitSafeConversion(lhs, overload.LeftParam);
          cg.EmitTypedNode(nodes[nodeIndex], overload.RightParam);
          cg.EmitCall(overload.Method);
          lhs = overload.Method.ReturnType;
          continue;
        }

        // maybe there are implicit conversions to primitive types
        ITypeInfo newLhs = CG.GetImplicitConversionToNumeric(lhs), newRhs = CG.GetImplicitConversionToNumeric(rhs);
        if(newLhs != null && newRhs != null)
        {
          // set a flag indicating that we have implicit conversions to numeric and jump to the top of the loop where
          // we can retry the operation
          shouldImplicitlyConvertToNumeric = true;
          goto retry;
        }

        // emit a call to the runtime version as a last resort
        cg.EmitSafeConversion(lhs, TypeWrapper.Object);
        Slot tmp = cg.AllocLocalTemp(TypeWrapper.Object);
        tmp.EmitSet(cg);
        EmitThisOperator(cg);
        tmp.EmitGet(cg);
        cg.FreeLocalTemp(tmp);
        cg.EmitTypedNode(nodes[nodeIndex], TypeWrapper.Object);
        cg.EmitCall(GetType(), "Evaluate", typeof(object), typeof(object));
        lhs = TypeWrapper.Object;
      }
    }
    
    cg.EmitSafeConversion(lhs, desiredType);
  }

  public override ITypeInfo GetValueType(IList<ASTNode> nodes)
  {
    if(nodes.Count == 0) throw new ArgumentException();
    ITypeInfo type = nodes[0].ValueType;
    for(int nodeIndex=1; nodeIndex<nodes.Count; nodeIndex++)
    {
      type = GetValueType(type, nodes[nodeIndex].ValueType);
    }
    return type;
  }

  protected abstract void EmitOp(CodeGenerator cg, bool signed);

  protected bool NormalizeTypesOrCallOverload(ref object a, ref object b, out object value)
  {
    ITypeInfo lhs = CG.GetTypeInfo(a), rhs = CG.GetTypeInfo(b);

    if(CG.IsPrimitiveNumeric(lhs) && CG.IsPrimitiveNumeric(rhs)) // if we're dealing with primitive numeric types
    {
      Type type = GetTypeForPrimitiveNumerics(lhs, rhs).DotNetType;
      a = Ops.ConvertTo(a, type);
      b = Ops.ConvertTo(b, type);
      value = null;
      return false;
    }
    else
    {
      Overload overload = GetOperatorOverload(lhs, rhs);
      if(overload != null)
      {
        value = overload.Method.Method.Invoke(null, new object[] { Ops.ConvertTo(a, overload.LeftParam.DotNetType),
                                                                   Ops.ConvertTo(b, overload.RightParam.DotNetType) });
        return true;
      }

      ITypeInfo newLhs = CG.GetImplicitConversionToNumeric(lhs), newRhs = CG.GetImplicitConversionToNumeric(rhs);
      if(newLhs != null && newRhs != null)
      {
        Type type = GetTypeForPrimitiveNumerics(newLhs, newRhs).DotNetType;
        a = Ops.ConvertTo(a, type);
        b = Ops.ConvertTo(b, type);
        value = null;
        return false;
      }
      
      throw NoOperation(lhs, rhs);
    }
  }
  
  protected Exception NoOperation(object a, object b)
  {
    return NoOperation(CG.GetType(a), CG.GetType(b));
  }
  
  protected Exception NoOperation(Type a, Type b)
  {
    return new CantApplyOperatorException(Name, a, b);
  }

  sealed class Overload
  {
    public Overload(IMethodInfo mi, IParameterInfo[] parms)
    {
      Method     = mi;
      LeftParam  = parms[0].ParameterType;
      RightParam = parms[1].ParameterType;
    }

    public readonly IMethodInfo Method;
    public readonly ITypeInfo LeftParam, RightParam;
  }

  Overload GetOperatorOverload(ITypeInfo lhs, ITypeInfo rhs)
  {
    // get a list of all operator overloads between the two types
    List<Overload> overloads = GetOperatorOverloads(lhs, rhs);

    // first see if any overload matches without the need for implicit conversions
    int match = -1;
    for(int i=0; i<overloads.Count; i++)
    {
      Overload overload = overloads[i];
      if(overload.LeftParam == lhs && overload.RightParam == rhs) // if it matches the exact types
      {
        if(match != -1) // if we have multiple matches, it's ambiguous
        {
          throw new AmbiguousCallException(overloads[match].Method, overload.Method);
        }

        match = i;
      }
    }

    if(match == -1)
    {
      // there was no exact match, but maybe there are operator overloads involving implicit conversions
      for(int i=0; i<overloads.Count; i++)
      {
        Overload overload = overloads[i];
        if(CG.HasImplicitConversion(lhs, overload.LeftParam) && CG.HasImplicitConversion(rhs, overload.RightParam))
        {
          if(match != -1)
          {
            throw new AmbiguousCallException(overloads[match].Method, overload.Method);
          }
          match = i;
        }
      }
    }

    return match == -1 ? null : overloads[match];
  }

  List<Overload> GetOperatorOverloads(ITypeInfo lhs, ITypeInfo rhs)
  {
    List<IMethodInfo> methods = new List<IMethodInfo>();
    methods.AddRange(lhs.GetMethods(BindingFlags.Public|BindingFlags.Static));

    if(lhs != rhs) // if we have two different types, look at both their overrides
    {
      methods.AddRange(rhs.GetMethods(BindingFlags.Public|BindingFlags.Static));
    }

    List<Overload> overloads = new List<Overload>();
    foreach(IMethodInfo mi in methods)
    {
      // skip the ones that aren't overloads for our operator
      if(!string.Equals(mi.Name, opOverload, StringComparison.Ordinal))
      {
        continue;
      }

      // skip the ones that don't have 2 parameters
      IParameterInfo[] parameters = mi.GetParameters();
      if(parameters.Length == 2)
      {
        overloads.Add(new Overload(mi, parameters));
      }
    }

    return overloads;
  }

  ITypeInfo GetValueType(ITypeInfo lhs, ITypeInfo rhs)
  {
    ITypeInfo type;

    if(CG.IsPrimitiveNumeric(lhs.DotNetType) && CG.IsPrimitiveNumeric(rhs.DotNetType)) // if they're primitive numerics
    {
      type = GetTypeForPrimitiveNumerics(lhs, rhs);
    }
    else // at least one type is a non-primitive. check for operator overloading and implicit conversions
    {
      Overload overload = GetOperatorOverload(lhs, rhs);
      if(overload != null)
      {
        type = overload.Method.ReturnType;
      }

      // maybe there are implicit conversions to primitive types
      ITypeInfo newLhs = CG.GetImplicitConversionToNumeric(lhs), newRhs = CG.GetImplicitConversionToNumeric(rhs);
      if(newLhs != null && newRhs != null)
      {
        type = GetTypeForPrimitiveNumerics(newLhs, newRhs);
      }
      else
      {
        type = TypeWrapper.Object; // as a last resort we'll invoke the runtime function, which return an Object
      }
    }
    
    return type;
  }
  
  readonly string opOverload;

  static ITypeInfo GetTypeForPrimitiveNumerics(ITypeInfo lhs, ITypeInfo rhs)
  {
    ITypeInfo type;
    TypeCode ltc = lhs.TypeCode, rtc = rhs.TypeCode;

    // if both have the same sign or one is larger than the other, or either is floating point...
    if(CG.IsSigned(ltc) == CG.IsSigned(rtc) || CG.SizeOfPrimitiveNumeric(ltc) != CG.SizeOfPrimitiveNumeric(rtc) ||
       CG.IsFloatingPoint(ltc) || CG.IsFloatingPoint(rtc))
    {
      // then we simply follow the eqSignPromotions table, taking the first one found in the table
      int left = Array.IndexOf(eqSignPromotions, lhs), right = Array.IndexOf(eqSignPromotions, rhs);
      if(left  == -1) left  = int.MaxValue;
      if(right == -1) right = int.MaxValue;

      int index = Math.Min(left, right);
      if(index == int.MaxValue) // undefined numeric conversions become int
      {
        type = TypeWrapper.Int;
      }
      else
      {
        type = eqSignPromotions[index];
      }
    }
    else // otherwise, they have different signs and the same size, and neither is floating point
    {
      int size = CG.SizeOfPrimitiveNumeric(ltc);
      if(size == 8) // ulong+long promotes to Integer
      {
        type = TypeWrapper.Integer;
      }
      else if(size == 4) // uint+int promotes to long
      {
        type = TypeWrapper.Long;
      }
      else // all others promote to int
      {
        type = TypeWrapper.Int;
      }
    }
    
    return type;
  }

  static readonly ITypeInfo[] eqSignPromotions =
  {
    TypeWrapper.Double, TypeWrapper.Single, TypeWrapper.ULong, TypeWrapper.Long, TypeWrapper.UInt
  };
}
#endregion

#region UncheckedAddOperator
public class UncheckedAddOperator : ArithmeticOperator
{
  internal UncheckedAddOperator() : base("add", "op_Addition") { }

  public override void EmitThisOperator(CodeGenerator cg)
  {
    cg.EmitFieldGet(typeof(Operator), "UncheckedAdd");
  }

  public override object Evaluate(object a, object b)
  {
    object ret;
    if(NormalizeTypesOrCallOverload(ref a, ref b, out ret)) return ret;

    switch(Convert.GetTypeCode(a))
    {
      case TypeCode.Byte: return (byte)a + (byte)b;
      case TypeCode.Double: return (double)a + (double)b;
      case TypeCode.Int16: return (short)a + (short)b;
      case TypeCode.Int32: return (int)a + (int)b;
      case TypeCode.Int64: return (long)a + (long)b;
      case TypeCode.SByte: return (sbyte)a + (sbyte)b;
      case TypeCode.Single: return (float)a + (float)b;
      case TypeCode.UInt16: return (ushort)a + (ushort)b;
      case TypeCode.UInt32: return (uint)a + (uint)b;
      case TypeCode.UInt64: return (ulong)a + (ulong)b;
    }

    throw NoOperation(a, b);
  }

  protected override void EmitOp(CodeGenerator cg, bool signed)
  {
    cg.ILG.Emit(OpCodes.Add);
  }
}
#endregion

#region UncheckedSubtractOperator
public class UncheckedSubtractOperator : ArithmeticOperator
{
  internal UncheckedSubtractOperator() : base("subtract", "op_Subtraction") { }

  public override void EmitThisOperator(CodeGenerator cg)
  {
    cg.EmitFieldGet(typeof(Operator), "UncheckedSubtract");
  }

  public override object Evaluate(object a, object b)
  {
    object ret;
    if(NormalizeTypesOrCallOverload(ref a, ref b, out ret)) return ret;

    switch(Convert.GetTypeCode(a))
    {
      case TypeCode.Byte: return (byte)a - (byte)b;
      case TypeCode.Double: return (double)a - (double)b;
      case TypeCode.Int16: return (short)a - (short)b;
      case TypeCode.Int32: return (int)a - (int)b;
      case TypeCode.Int64: return (long)a - (long)b;
      case TypeCode.SByte: return (sbyte)a - (sbyte)b;
      case TypeCode.Single: return (float)a - (float)b;
      case TypeCode.UInt16: return (ushort)a - (ushort)b;
      case TypeCode.UInt32: return (uint)a - (uint)b;
      case TypeCode.UInt64: return (ulong)a - (ulong)b;
    }

    throw NoOperation(a, b);
  }

  protected override void EmitOp(CodeGenerator cg, bool signed)
  {
    cg.ILG.Emit(OpCodes.Sub);
  }
}
#endregion

#region UncheckedMultiplyOperator
public class UncheckedMultiplyOperator : ArithmeticOperator
{
  internal UncheckedMultiplyOperator() : base("multiply", "op_Multiply") { }

  public override void EmitThisOperator(CodeGenerator cg)
  {
    cg.EmitFieldGet(typeof(Operator), "UncheckedMultiply");
  }

  public override object Evaluate(object a, object b)
  {
    object ret;
    if(NormalizeTypesOrCallOverload(ref a, ref b, out ret)) return ret;

    switch(Convert.GetTypeCode(a))
    {
      case TypeCode.Byte: return (byte)a * (byte)b;
      case TypeCode.Double: return (double)a * (double)b;
      case TypeCode.Int16: return (short)a * (short)b;
      case TypeCode.Int32: return (int)a * (int)b;
      case TypeCode.Int64: return (long)a * (long)b;
      case TypeCode.SByte: return (sbyte)a * (sbyte)b;
      case TypeCode.Single: return (float)a * (float)b;
      case TypeCode.UInt16: return (ushort)a * (ushort)b;
      case TypeCode.UInt32: return (uint)a * (uint)b;
      case TypeCode.UInt64: return (ulong)a * (ulong)b;
    }

    throw NoOperation(a, b);
  }

  protected override void EmitOp(CodeGenerator cg, bool signed)
  {
    cg.ILG.Emit(OpCodes.Mul);
  }
}
#endregion

#region DivideOperator
public class DivideOperator : ArithmeticOperator
{
  internal DivideOperator() : base("divide", "op_Division") { }

  public override void EmitThisOperator(CodeGenerator cg)
  {
    cg.EmitFieldGet(typeof(Operator), "Divide");
  }

  public override object Evaluate(object a, object b)
  {
    object ret;
    if(NormalizeTypesOrCallOverload(ref a, ref b, out ret)) return ret;

    switch(Convert.GetTypeCode(a))
    {
      case TypeCode.Byte: return (byte)a / (byte)b;
      case TypeCode.Double: return (double)a / (double)b;
      case TypeCode.Int16: return (short)a / (short)b;
      case TypeCode.Int32: return (int)a / (int)b;
      case TypeCode.Int64: return (long)a / (long)b;
      case TypeCode.SByte: return (sbyte)a / (sbyte)b;
      case TypeCode.Single: return (float)a / (float)b;
      case TypeCode.UInt16: return (ushort)a / (ushort)b;
      case TypeCode.UInt32: return (uint)a / (uint)b;
      case TypeCode.UInt64: return (ulong)a / (ulong)b;
    }

    throw NoOperation(a, b);
  }

  protected override void EmitOp(CodeGenerator cg, bool signed)
  {
    cg.ILG.Emit(signed ? OpCodes.Div : OpCodes.Div_Un);
  }
}
#endregion

} // namespace Scripting.AST