using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Scripting.Emit;
using Scripting.Runtime;

namespace Scripting.AST
{

public abstract class Operator
{
  /// <param name="name">The name displayed to the user in diagnostic messages.</param>
  protected Operator(string name)
  {
    Name = name;
  }

  public readonly string Name;

  public abstract void Emit(CodeGenerator cg, IList<ASTNode> nodes, ref Type desiredType);
  public abstract object Evaluate(IList<ASTNode> nodes);
  public abstract Type GetValueType(IList<ASTNode> nodes);
  
  public object Evaluate(params object[] values)
  {
    ASTNode[] nodes = new ASTNode[values.Length];
    for(int i=0; i<nodes.Length; i++)
    {
      nodes[i] = new LiteralNode(values[i]);
    }
    return Evaluate((IList<ASTNode>)nodes);
  }

  public static readonly NaryOperator UncheckedAdd      = new UncheckedAddOperator();
  public static readonly NaryOperator UncheckedSubtract = new UncheckedSubtractOperator();
  public static readonly NaryOperator UncheckedMultiply = new UncheckedMultiplyOperator();
  public static readonly NaryOperator Divide = new DivideOperator();
}

public abstract class UnaryOperator : Operator
{
  protected UnaryOperator(string name) : base(name) { }
}

public abstract class NaryOperator : Operator
{
  protected NaryOperator(string name) : base(name) { }
}

#region ArithmeticOperator
public abstract class ArithmeticOperator : NaryOperator
{
  /// <param name="opMethod">The name of the method used for operator overloading. For instance, op_Addition.</param>
  protected ArithmeticOperator(string name, string opOverload, string opsMethod) : base(name)
  {
    if(string.IsNullOrEmpty(name) || string.IsNullOrEmpty(opOverload) || string.IsNullOrEmpty(opsMethod))
    {
      throw new ArgumentException("Name and method names must not be empty.");
    }

    this.opOverload = opOverload;
    this.opsMethod  = opsMethod;
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

  public sealed override void Emit(CodeGenerator cg, IList<ASTNode> nodes, ref Type desiredType)
  {
    if(nodes.Count < 2) throw new ArgumentException();

    Type lhs = nodes[0].ValueType;
    nodes[0].Emit(cg, ref lhs);

    for(int nodeIndex=1; nodeIndex<nodes.Count; nodeIndex++)
    {
      Type rhs = nodes[nodeIndex].ValueType;
      bool shouldImplicitlyConvertToNumeric = false;

      retry:
      TypeCode ltc = Type.GetTypeCode(lhs), rtc = Type.GetTypeCode(rhs);

      if(shouldImplicitlyConvertToNumeric ||
         CG.IsPrimitiveNumeric(ltc) && CG.IsPrimitiveNumeric(rtc)) // if we're dealing with primitive numeric types
      {
        Type realLhs=lhs, realRhs=rhs; // the real object types that will be converted to numeric
        // if set, one or both of lhs and rhs are non-numeric but can be converted to numeric types
        if(shouldImplicitlyConvertToNumeric)
        {
          lhs = CG.GetImplicitConversionToNumeric(lhs);
          rhs = CG.GetImplicitConversionToNumeric(rhs);
          ltc = Type.GetTypeCode(lhs);
          rtc = Type.GetTypeCode(rhs);
        }

        // and they both have the same sign or one is larger than the other, or either is floating point
        if(CG.IsSigned(ltc) == CG.IsSigned(rtc) || CG.SizeOfPrimitiveNumeric(ltc) != CG.SizeOfPrimitiveNumeric(rtc) ||
           CG.IsFloatingPoint(ltc) || CG.IsFloatingPoint(rtc))
        {
          // then we simply follow the eqSignPromotions table, taking the first one found in the table
          int left = Array.IndexOf(eqSignPromotions, lhs), right = Array.IndexOf(eqSignPromotions, rhs);
          if(left  == -1) left  = int.MaxValue;
          if(right == -1) right = int.MaxValue;

          if(shouldImplicitlyConvertToNumeric) cg.EmitSafeConversion(realLhs, lhs); // convert the left side if necessary

          int index = Math.Min(left, right);
          if(index == int.MaxValue) // undefined numeric conversions become int
          {
            cg.EmitSafeConversion(lhs, typeof(int));
            if(shouldImplicitlyConvertToNumeric)
            {
              nodes[nodeIndex].Emit(cg, ref realRhs);
              cg.EmitSafeConversion(realRhs, rhs); // convert the right side if necessary
            }
            else
            {
              rhs = typeof(int);
              nodes[nodeIndex].Emit(cg, ref rhs);
            }
            cg.EmitSafeConversion(rhs, typeof(int));
            EmitOp(cg, true);
            lhs = typeof(int);
          }
          else
          {
            cg.EmitSafeConversion(lhs, eqSignPromotions[index]);
            if(shouldImplicitlyConvertToNumeric)
            {
              nodes[nodeIndex].Emit(cg, ref realRhs);
              rhs = realRhs;
            }
            else
            {
              rhs = eqSignPromotions[index];
              nodes[nodeIndex].Emit(cg, ref rhs);
            }
            cg.EmitSafeConversion(rhs, eqSignPromotions[index]);
            EmitOp(cg, CG.IsSigned(eqSignPromotions[index])); // emit the actual operator
            lhs = eqSignPromotions[index];
          }
        }
        else // otherwise, they have different signs and the same size, and neither is floating point
        {
          int size = CG.SizeOfPrimitiveNumeric(ltc);
          Type newLhs;
          if(size == 8) // ulong+long promotes to Integer
          {
            newLhs = typeof(Integer);
          }
          else if(size == 4) // uint+int promotes to long
          {
            newLhs = typeof(long);
          }
          else // all others promote to int
          {
            newLhs = typeof(int);
          }

          // convert the left side and rerun the logic, which will take a different path
          cg.EmitSafeConversion(lhs, newLhs);
          lhs = newLhs;
          goto retry;
        }
      }
      else // at least one type is a non-primitive. check for operator overloading and implicit conversions
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

        if(match == -1) // if there was no match, try operator overloads involving implicit conversions
        {
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

        if(match != -1) // if there's an operator overload, use it.
        {
          cg.EmitSafeConversion(lhs, overloads[match].LeftParam);
          cg.EmitTypedNode(nodes[nodeIndex], overloads[match].RightParam);
          cg.EmitCall(overloads[match].Method);
          lhs = overloads[match].Method.ReturnType;
          continue;
        }

        // maybe there are implicit conversions to primitive types
        Type newLhs = CG.GetImplicitConversionToNumeric(lhs), newRhs = CG.GetImplicitConversionToNumeric(rhs);
        if(newLhs != null && newRhs != null)
        {
          // set a flag indicating that we have implicit conversions to numeric and jump to the top of the loop where
          // we can retry the operation
          shouldImplicitlyConvertToNumeric = true;
          goto retry;
        }

        // as a last resort, invoke the runtime function
        cg.EmitSafeConversion(lhs, typeof(object));
        cg.EmitTypedNode(nodes[nodeIndex], typeof(object));
        cg.EmitCall(typeof(Ops), opsMethod);
        lhs = typeof(object);
      }
    }
    
    cg.EmitSafeConversion(lhs, desiredType);
  }

  public override Type GetValueType(IList<ASTNode> nodes)
  {
    if(nodes.Count == 0) throw new ArgumentException();
    Type lhs = nodes[0].ValueType;

    for(int nodeIndex=1; nodeIndex<nodes.Count; nodeIndex++)
    {
      Type rhs = nodes[nodeIndex].ValueType;

      retry:
      TypeCode ltc = Type.GetTypeCode(lhs), rtc = Type.GetTypeCode(rhs);

      if(CG.IsPrimitiveNumeric(ltc) && CG.IsPrimitiveNumeric(rtc)) // if we're dealing with primitive numeric types
      {
        // and they both have the same sign or one is larger than the other, or either is floating point
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
            lhs = typeof(int);
          }
          else
          {
            lhs = eqSignPromotions[index];
          }
        }
        else // otherwise, they have different signs and the same size, and neither is floating point
        {
          int size = CG.SizeOfPrimitiveNumeric(ltc);
          if(size == 8) // ulong+long promotes to Integer
          {
            lhs = typeof(Integer);
          }
          else if(size == 4) // uint+int promotes to ulong
          {
            lhs = typeof(long);
          }
          else // all others promote to int
          {
            lhs = typeof(int);
          }
        }
      }
      else // at least one type is a non-primitive. check for operator overloading and implicit conversions
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
        
        if(match != -1) // if we had a single operator overload match, use the return type
        {
          lhs = overloads[match].Method.ReturnType;
          continue; // go to the next AST node
        }

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
        
        if(match != -1) // if there's an operator overload using implicit conversions, use it.
        {
          lhs = overloads[match].Method.ReturnType;
          continue;
        }

        // maybe there are implicit conversions to primitive types
        Type newLhs = CG.GetImplicitConversionToNumeric(lhs), newRhs = CG.GetImplicitConversionToNumeric(rhs);
        if(newLhs != null && newRhs != null)
        {
          // set the lhs and rhs to the numeric types and jump to the top of the loop, where we can use the normal
          // logic for numerics
          lhs = newLhs;
          rhs = newRhs;
          goto retry;
        }
        
        // as a last resort, we'd invoke the runtime function, which returns an object
        lhs = typeof(object);
      }
    }
    
    return lhs;
  }
  
  protected abstract void EmitOp(CodeGenerator cg, bool signed);
  protected abstract object Evaluate(object a, object b);
  
  sealed class Overload
  {
    public Overload(MethodInfo mi, ParameterInfo[] parms)
    {
      Method     = mi;
      LeftParam  = parms[0].ParameterType;
      RightParam = parms[1].ParameterType;
    }

    public readonly MethodInfo Method;
    public readonly Type LeftParam, RightParam;
  }

  List<Overload> GetOperatorOverloads(Type lhs, Type rhs)
  {
    List<MethodInfo> methods = new List<MethodInfo>();
    methods.AddRange(lhs.GetMethods(BindingFlags.Public|BindingFlags.Static));

    if(lhs != rhs) // if we have two different types, look at both their overrides
    {
      methods.AddRange(rhs.GetMethods(BindingFlags.Public|BindingFlags.Static));
    }

    List<Overload> overloads = new List<Overload>();
    foreach(MethodInfo mi in methods)
    {
      // skip the ones that aren't overloads for our operator
      if(!string.Equals(mi.Name, opOverload, StringComparison.Ordinal))
      {
        continue;
      }

      // skip the ones that don't have 2 parameters
      ParameterInfo[] parameters = mi.GetParameters();
      if(parameters.Length == 2)
      {
        overloads.Add(new Overload(mi, parameters));
      }
    }

    return overloads;
  }

  readonly string opOverload, opsMethod;

  static readonly Type[] eqSignPromotions =
  {
    typeof(double), typeof(float), typeof(ulong), typeof(long), typeof(uint)
  };
}
#endregion

public sealed class UncheckedAddOperator : ArithmeticOperator
{
  internal UncheckedAddOperator() : base("add", "op_Addition", "UncheckedAdd") { }

  protected override void EmitOp(CodeGenerator cg, bool signed)
  {
    cg.ILG.Emit(OpCodes.Add);
  }

  protected override object Evaluate(object a, object b)
  {
    return Ops.UncheckedAdd(a, b);
  }
}

public sealed class UncheckedSubtractOperator : ArithmeticOperator
{
  internal UncheckedSubtractOperator() : base("subtract", "op_Subtraction", "UncheckedSubtract") { }

  protected override void EmitOp(CodeGenerator cg, bool signed)
  {
    cg.ILG.Emit(OpCodes.Sub);
  }

  protected override object Evaluate(object a, object b)
  {
    return Ops.UncheckedSubtract(a, b);
  }
}

public sealed class UncheckedMultiplyOperator : ArithmeticOperator
{
  internal UncheckedMultiplyOperator() : base("multiply", "op_Multiply", "UncheckedMultiply") { }

  protected override void EmitOp(CodeGenerator cg, bool signed)
  {
    cg.ILG.Emit(OpCodes.Mul);
  }

  protected override object Evaluate(object a, object b)
  {
    return Ops.UncheckedMultiply(a, b);
  }
}

public sealed class DivideOperator : ArithmeticOperator
{
  internal DivideOperator() : base("divide", "op_Division", "Divide") { }

  protected override void EmitOp(CodeGenerator cg, bool signed)
  {
    cg.ILG.Emit(signed ? OpCodes.Div : OpCodes.Div_Un);
  }

  protected override object Evaluate(object a, object b)
  {
    return Ops.Divide(a, b);
  }
}

} // namespace Scripting.AST