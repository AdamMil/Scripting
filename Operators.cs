using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Scripting.Emit;
using Scripting.Runtime;

namespace Scripting.AST
{

#region Base classes
#region Operator
public abstract class Operator
{
  /// <param name="name">The name displayed to the user in diagnostic messages.</param>
  protected Operator(string name)
  {
    if(string.IsNullOrEmpty(name)) throw new ArgumentException("Name must not be empty.");
    Name = name;
  }

  public readonly string Name;

  public abstract void CheckSemantics(ITypeInfo opContextType, IList<ASTNode> nodes);
  public abstract ITypeInfo Emit(CodeGenerator cg, ITypeInfo desiredType, IList<ASTNode> nodes);
  public abstract void EmitThisOperator(CodeGenerator cg);
  public abstract object Evaluate(IList<ASTNode> nodes);
  public abstract ITypeInfo GetValueType(IList<ASTNode> nodes);

  public virtual void SetValueContext(ITypeInfo opContextType, IList<ASTNode> nodes)
  {
    ITypeInfo type = GetValueType(nodes);
    foreach(ASTNode node in nodes) node.SetValueContext(type);
  }
  
  public object Evaluate(params object[] values)
  {
    ASTNode[] nodes = new ASTNode[values.Length];
    for(int i=0; i<nodes.Length; i++)
    {
      nodes[i] = new LiteralNode(values[i]);
    }
    return Evaluate((IList<ASTNode>)nodes);
  }

  public static readonly AddOperator      Add       = new AddOperator();
  public static readonly SubtractOperator Subtract  = new SubtractOperator();
  public static readonly MultiplyOperator Multiply  = new MultiplyOperator();
  public static readonly DivideOperator   Divide    = new DivideOperator();
  public static readonly ModulusOperator  Modulus   = new ModulusOperator();

  public static readonly BitwiseAndOperator BitwiseAnd = new BitwiseAndOperator();
  public static readonly BitwiseOrOperator  BitwiseOr  = new BitwiseOrOperator();
  public static readonly BitwiseXorOperator BitwizeXor = new BitwiseXorOperator();

  public static readonly LogicalTruthOperator LogicalTruth = new LogicalTruthOperator();
}
#endregion

#region UnaryOperator
public abstract class UnaryOperator : Operator
{
  protected UnaryOperator(string name) : base(name) { }

  public override void CheckSemantics(ITypeInfo opContextType, IList<ASTNode> nodes)
  {
    if(nodes.Count != 1) CompilerState.Current.Messages.Add(CoreDiagnostics.WrongOperatorArity, Name, 1, nodes.Count);
    CheckSemantics(opContextType, nodes[0]);
  }

  public override ITypeInfo Emit(CodeGenerator cg, ITypeInfo desiredType, IList<ASTNode> nodes)
  {
    if(nodes.Count != 1) throw new ArgumentException();
    return Emit(cg, desiredType, nodes[0]);
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

  public override void SetValueContext(ITypeInfo opContextType, IList<ASTNode> nodes)
  {
    if(nodes.Count != 1) throw new ArgumentException();
    SetValueContext(opContextType, nodes[0]);
  }

  public abstract void CheckSemantics(ITypeInfo opContextType, ASTNode node);
  public abstract ITypeInfo Emit(CodeGenerator cg, ITypeInfo desiredType, ASTNode node);
  public abstract object Evaluate(object obj);
  public abstract ITypeInfo GetValueType(ASTNode node);

  public virtual void SetValueContext(ITypeInfo opContextType, ASTNode node)
  {
    node.SetValueContext(GetValueType(node));
  }
}
#endregion

#region NAryOperator
public abstract class NaryOperator : Operator
{
  protected NaryOperator(string name) : base(name) { }
}
#endregion

#region NumericOperator
public abstract class NumericOperator : NaryOperator
{
  /// <param name="opMethod">The name of the method used for operator overloading. For instance, op_Addition.</param>
  protected NumericOperator(string name, string opOverload) : base(name)
  {
    if(string.IsNullOrEmpty(opOverload)) throw new ArgumentException("Method name must not be empty.");
    this.opOverload = opOverload;
  }

  [Flags]
  public enum Options
  {
    None=0, Checked=1, Promote=2
  }

  public override void CheckSemantics(ITypeInfo opContextType, IList<ASTNode> nodes)
  {
    ITypeInfo type = nodes[0].ValueType;
    for(int nodeIndex=1; nodeIndex<nodes.Count; nodeIndex++)
    {
      type = GetValueType(type, nodes[nodeIndex].ValueType);
      if(type == TypeWrapper.Invalid)
      {
        nodes[nodeIndex-1].AddMessage(CoreDiagnostics.CannotApplyOperator2, Name,
                             CG.TypeName(nodes[nodeIndex-1].ValueType), CG.TypeName(nodes[nodeIndex].ValueType));
        break;
      }
    }
  }

  public sealed override void EmitThisOperator(CodeGenerator cg)
  {
    cg.EmitFieldGet(typeof(Operator), Name);
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

  public object Evaluate(object a, object b)
  {
    return Evaluate(a, b, GetCurrentOptions());
  }

  public abstract object Evaluate(object a, object b, Options options);

  public sealed override ITypeInfo Emit(CodeGenerator cg, ITypeInfo desiredType, IList<ASTNode> nodes)
  {
    if(nodes.Count < 2) throw new ArgumentException();

    bool autoPromote = CompilerState.Current.Checked && CompilerState.Current.PromoteOnOverflow;
    ITypeInfo lhs = nodes[0].Emit(cg);

    for(int nodeIndex=1; nodeIndex<nodes.Count; nodeIndex++)
    {
      ITypeInfo rhs = nodes[nodeIndex].ValueType;
      bool shouldImplicitlyConvertToNumeric = false;

      retry:
      TypeCode ltc = lhs.TypeCode, rtc = rhs.TypeCode;

      if(!autoPromote && (shouldImplicitlyConvertToNumeric ||       // if we're dealing with primitive numeric types
         CG.IsPrimitiveNumeric(ltc) && CG.IsPrimitiveNumeric(rtc))) // and we don't need to worry about promotion
      {
        ITypeInfo realLhs=lhs, realRhs=rhs; // the real object types that will be converted to numeric
        if(shouldImplicitlyConvertToNumeric)
        {
          lhs = CG.GetImplicitConversionToPrimitiveNumeric(lhs);
          rhs = CG.GetImplicitConversionToPrimitiveNumeric(rhs);
          cg.EmitSafeConversion(realLhs, lhs); // convert the left side if necessary
        }

        ITypeInfo type = GetTypeForPrimitiveNumerics(lhs, rhs);
        cg.EmitSafeConversion(lhs, type);

        realRhs = nodes[nodeIndex].Emit(cg);
        if(shouldImplicitlyConvertToNumeric) cg.EmitSafeConversion(realRhs, rhs); // convert the right side if necessary
        cg.EmitSafeConversion(rhs, type);
        EmitOp(cg, type, true);
        lhs = type;
      }
      else // at least one type is a non-primitive, or we need to check for promotion
      {
        // if we got here because a type is non-primitive, then check overloads and implicit conversions...
        if(!autoPromote || !CG.IsPrimitiveNumeric(ltc) || !CG.IsPrimitiveNumeric(rtc))
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
          ITypeInfo newLhs = CG.GetImplicitConversionToPrimitiveNumeric(lhs),
                    newRhs = CG.GetImplicitConversionToPrimitiveNumeric(rhs);
          if(newLhs != null && newRhs != null)
          {
            // set a flag indicating that we have implicit conversions to numeric and jump to the top of the loop where
            // we can retry the operation
            shouldImplicitlyConvertToNumeric = true;
            goto retry;
          }
        }

        // emit a call to the runtime version as a last resort
        cg.EmitSafeConversion(lhs, TypeWrapper.Object);
        Slot tmp = cg.AllocLocalTemp(TypeWrapper.Object);
        tmp.EmitSet(cg);
        EmitThisOperator(cg);
        tmp.EmitGet(cg);
        cg.FreeLocalTemp(tmp);
        cg.EmitTypedNode(nodes[nodeIndex], TypeWrapper.Object);
        cg.EmitInt((int)GetCurrentOptions());
        cg.EmitCall(GetType(), "Evaluate", typeof(object), typeof(object), typeof(Options));
        lhs = TypeWrapper.Unknown;
      }
    }
    
    cg.EmitSafeConversion(lhs, desiredType);
    return desiredType;
  }

  public override ITypeInfo GetValueType(IList<ASTNode> nodes)
  {
    if(nodes.Count == 0) throw new ArgumentException();

    ITypeInfo type = nodes[0].ValueType;
    for(int nodeIndex=1; nodeIndex<nodes.Count; nodeIndex++)
    {
      type = GetValueType(type, nodes[nodeIndex].ValueType);
    }
    
    // if the type is invalid, it will be caught by the semantic checker later, or the runtime checker. we'll return
    // 'unknown' now to avoid leaking the Invalid type into the rest of the system where it's not expected
    if(type == TypeWrapper.Invalid) type = TypeWrapper.Unknown;

    // with promotion enabled, we can never be sure of what type we'll return (if it's a primitive type)
    if(CompilerState.Current.Checked && CompilerState.Current.PromoteOnOverflow && CG.IsPrimitiveNumeric(type))
    {
      return nodes.Count == 1 ? type : TypeWrapper.Unknown;
    }
    else
    {
      return type;
    }
  }

  public override void SetValueContext(ITypeInfo opContextType, IList<ASTNode> nodes)
  {
    if(nodes.Count == 0) throw new ArgumentException();

    ITypeInfo type = nodes[0].ValueType;
    nodes[0].SetValueContext(type);

    for(int nodeIndex=1; nodeIndex<nodes.Count; nodeIndex++)
    {
      type = GetValueType(type, nodes[nodeIndex].ValueType);
      // if the type is invalid, it will be caught by the semantic checker later, or the runtime checker. we'll use
      // 'unknown' here to avoid leaking the Invalid type into the rest of the system where it's not expected
      nodes[nodeIndex].SetValueContext(type == TypeWrapper.Invalid ? TypeWrapper.Unknown : type);
    }
  }

  protected abstract void EmitOp(CodeGenerator cg, ITypeInfo typeOnStack, bool signed);

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

      ITypeInfo newLhs = CG.GetImplicitConversionToPrimitiveNumeric(lhs),
                newRhs = CG.GetImplicitConversionToPrimitiveNumeric(rhs);
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
    return NoOperation(CG.GetTypeWithUnwrapping(a), CG.GetTypeWithUnwrapping(b));
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

    if(lhs == TypeWrapper.Unknown || rhs == TypeWrapper.Unknown) // if either type is unknown
    {
      type = TypeWrapper.Unknown; // we'll invoke the runtime function, which returns an unknown type
    }
    else if(CG.IsPrimitiveNumeric(lhs.DotNetType) && CG.IsPrimitiveNumeric(rhs.DotNetType)) // if they're primitive numerics
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
      else
      {
        // maybe there are implicit conversions to primitive types
        ITypeInfo newLhs = CG.GetImplicitConversionToPrimitiveNumeric(lhs),
                  newRhs = CG.GetImplicitConversionToPrimitiveNumeric(rhs);
        if(newLhs != null && newRhs != null)
        {
          type = GetTypeForPrimitiveNumerics(newLhs, newRhs);
        }
        else
        {
          type = TypeWrapper.Invalid;
        }
      }
    }
    
    return type;
  }
  
  readonly string opOverload;

  static Options GetCurrentOptions()
  {
    Options options = Options.None;
    if(CompilerState.Current.Checked)
    {
      options |= Options.Checked;
      if(CompilerState.Current.PromoteOnOverflow) options |= Options.Promote;
    }
    return options;
  }

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
#endregion

#region Unary operators
#region LogicalTruthOperator
public sealed class LogicalTruthOperator : UnaryOperator
{
  public LogicalTruthOperator() : base("truth") { }

  public override ITypeInfo Emit(CodeGenerator cg, ITypeInfo desiredType, ASTNode node)
  {
    ITypeInfo type = node.Emit(cg);

    if(type == null) type = TypeWrapper.Bool; // we consider null to be false, and so does the runtime

    if(type != TypeWrapper.Bool) // if the node didn't give us a nice friendly boolean, we'll have to call Evaluate()
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

    return type;
  }

  public override void CheckSemantics(ITypeInfo opContextType, ASTNode node)
  {
    if(node.ValueType == TypeWrapper.Void) node.AddMessage(CoreDiagnostics.ExpectedValue);
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
#endregion

// TODO: make these work with only one argument. multiplication and division should use a default LHS of 1 if they
// receive only one value. all others should use a default LHS of zero.
#region Arithmetic operators
#region AddOperator
public sealed class AddOperator : NumericOperator
{
  internal AddOperator() : base("Add", "op_Addition") { }

  public override object Evaluate(object a, object b, Options options)
  {
    object ret;
    if(NormalizeTypesOrCallOverload(ref a, ref b, out ret)) return ret;

    TypeCode typeCode = Convert.GetTypeCode(a);
    if((options & Options.Checked) == 0) // unchecked
    {
      unchecked
      {
        switch(typeCode)
        {
          case TypeCode.Double: return (double)a + (double)b;
          case TypeCode.Int32: return (int)a + (int)b;
          case TypeCode.Int64: return (long)a + (long)b;
          case TypeCode.Single: return (float)a + (float)b;
          case TypeCode.UInt32: return (uint)a + (uint)b;
          case TypeCode.UInt64: return (ulong)a + (ulong)b;
        }
      }
    }
    else // checked
    {
      try
      {
        checked
        {
          switch(typeCode)
          {
            case TypeCode.Double: return (double)a + (double)b;
            case TypeCode.Int32: return (int)a + (int)b;
            case TypeCode.Int64: return (long)a + (long)b;
            case TypeCode.Single: return (float)a + (float)b;
            case TypeCode.UInt32: return (uint)a + (uint)b;
            case TypeCode.UInt64: return (ulong)a + (ulong)b;
          }
        }
      }
      catch(OverflowException)
      {
        if((options & Options.Promote) == 0) throw; // no promotion, so just let the exception go

        switch(typeCode) // only integer types can overflow
        {
          case TypeCode.Int32: return (long)(int)a + (int)b;
          case TypeCode.Int64: return new Integer((long)a) + (long)b;
          case TypeCode.UInt32: return (ulong)(uint)a + (uint)b;
          case TypeCode.UInt64: return new Integer((ulong)a) + (ulong)b;
        }
      }
    }

    throw NoOperation(a, b);
  }

  protected override void EmitOp(CodeGenerator cg, ITypeInfo typeOnStack, bool signed)
  {
    cg.ILG.Emit(CompilerState.Current.Checked ? signed ? OpCodes.Add_Ovf : OpCodes.Add_Ovf_Un : OpCodes.Add);
  }
}
#endregion

#region SubtractOperator
public sealed class SubtractOperator : NumericOperator
{
  internal SubtractOperator() : base("Subtract", "op_Subtraction") { }

  public override object Evaluate(object a, object b, Options options)
  {
    object ret;
    if(NormalizeTypesOrCallOverload(ref a, ref b, out ret)) return ret;

    TypeCode typeCode = Convert.GetTypeCode(a);
    if((options & Options.Checked) == 0) // unchecked
    {
      unchecked
      {
        switch(typeCode)
        {
          case TypeCode.Double: return (double)a - (double)b;
          case TypeCode.Int32: return (int)a - (int)b;
          case TypeCode.Int64: return (long)a - (long)b;
          case TypeCode.Single: return (float)a - (float)b;
          case TypeCode.UInt32: return (uint)a - (uint)b;
          case TypeCode.UInt64: return (ulong)a - (ulong)b;
        }
      }
    }
    else // checked
    {
      try
      {
        checked
        {
          switch(typeCode)
          {
            case TypeCode.Double: return (double)a - (double)b;
            case TypeCode.Int32: return (int)a - (int)b;
            case TypeCode.Int64: return (long)a - (long)b;
            case TypeCode.Single: return (float)a - (float)b;
            case TypeCode.UInt32: return (uint)a - (uint)b;
            case TypeCode.UInt64: return (ulong)a - (ulong)b;
          }
        }
      }
      catch(OverflowException)
      {
        if((options & Options.Promote) == 0) throw; // no promotion, so just let the exception go

        switch(typeCode) // only integer types can underflow
        {
          case TypeCode.Int32: return (long)(int)a - (int)b;
          case TypeCode.Int64: return new Integer((long)a) - (long)b;
          case TypeCode.UInt32: return (ulong)(uint)a - (uint)b;
          case TypeCode.UInt64: return new Integer((ulong)a) - (ulong)b;
        }
      }
    }

    throw NoOperation(a, b);
  }

  protected override void EmitOp(CodeGenerator cg, ITypeInfo typeOnStack, bool signed)
  {
    cg.ILG.Emit(OpCodes.Sub);
  }
}
#endregion

#region MultiplyOperator
public sealed class MultiplyOperator : NumericOperator
{
  internal MultiplyOperator() : base("Multiply", "op_Multiply") { }

  public override object Evaluate(object a, object b, Options options)
  {
    object ret;
    if(NormalizeTypesOrCallOverload(ref a, ref b, out ret)) return ret;

    TypeCode typeCode = Convert.GetTypeCode(a);
    if((options & Options.Checked) == 0) // unchecked
    {
      unchecked
      {
        switch(typeCode)
        {
          case TypeCode.Double: return (double)a * (double)b;
          case TypeCode.Int32: return (int)a * (int)b;
          case TypeCode.Int64: return (long)a * (long)b;
          case TypeCode.Single: return (float)a * (float)b;
          case TypeCode.UInt32: return (uint)a * (uint)b;
          case TypeCode.UInt64: return (ulong)a * (ulong)b;
        }
      }
    }
    else // checked
    {
      try
      {
        checked
        {
          switch(typeCode)
          {
            case TypeCode.Double: return (double)a * (double)b;
            case TypeCode.Int32: return (int)a * (int)b;
            case TypeCode.Int64: return (long)a * (long)b;
            case TypeCode.Single: return (float)a * (float)b;
            case TypeCode.UInt32: return (uint)a * (uint)b;
            case TypeCode.UInt64: return (ulong)a * (ulong)b;
          }
        }
      }
      catch(OverflowException)
      {
        if((options & Options.Promote) == 0) throw; // no promotion, so just let the exception go

        switch(typeCode) // only integer types can overflow
        {
          case TypeCode.Int32: return (long)(int)a * (int)b;
          case TypeCode.Int64: return new Integer((long)a) * (long)b;
          case TypeCode.UInt32: return (ulong)(uint)a * (uint)b;
          case TypeCode.UInt64: return new Integer((ulong)a) * (ulong)b;
        }
      }
    }

    throw NoOperation(a, b);
  }

  protected override void EmitOp(CodeGenerator cg, ITypeInfo typeOnStack, bool signed)
  {
    cg.ILG.Emit(CompilerState.Current.Checked ? signed ? OpCodes.Mul_Ovf : OpCodes.Mul_Ovf_Un : OpCodes.Mul);
  }
}
#endregion

#region DivideOperator
public sealed class DivideOperator : NumericOperator
{
  internal DivideOperator() : base("Divide", "op_Division") { }

  public override object Evaluate(object a, object b, Options options)
  {
    object ret;
    if(NormalizeTypesOrCallOverload(ref a, ref b, out ret)) return ret;

    switch(Convert.GetTypeCode(a))
    {
      case TypeCode.Double: return (double)a / (double)b;
      case TypeCode.Int32: return (int)a / (int)b;
      case TypeCode.Int64: return (long)a / (long)b;
      case TypeCode.Single: return (float)a / (float)b;
      case TypeCode.UInt32: return (uint)a / (uint)b;
      case TypeCode.UInt64: return (ulong)a / (ulong)b;
    }

    throw NoOperation(a, b);
  }

  protected override void EmitOp(CodeGenerator cg, ITypeInfo typeOnStack, bool signed)
  {
    cg.ILG.Emit(signed ? OpCodes.Div : OpCodes.Div_Un);
  }
}
#endregion

#region ModulusOperator
public sealed class ModulusOperator : NumericOperator
{
  internal ModulusOperator() : base("Modulus", "op_Modulus") { }

  public override object Evaluate(object a, object b, Options options)
  {
    object ret;
    if(NormalizeTypesOrCallOverload(ref a, ref b, out ret)) return ret;

    switch(Convert.GetTypeCode(a))
    {
      case TypeCode.Double: return LowLevel.DoubleRemainder((double)a, (double)b);
      case TypeCode.Int32: return (int)a % (int)b;
      case TypeCode.Int64: return (long)a % (long)b;
      case TypeCode.Single: return LowLevel.FloatRemainder((float)a, (float)b);
      case TypeCode.UInt32: return (uint)a % (uint)b;
      case TypeCode.UInt64: return (ulong)a % (ulong)b;
    }

    throw NoOperation(a, b);
  }

  protected override void EmitOp(CodeGenerator cg, ITypeInfo typeOnStack, bool signed)
  {
    if(typeOnStack == TypeWrapper.Double) cg.EmitCall(typeof(LowLevel), "DoubleRemainder");
    else if(typeOnStack == TypeWrapper.Single) cg.EmitCall(typeof(LowLevel), "FloatRemainder");
    else cg.ILG.Emit(signed ? OpCodes.Rem : OpCodes.Rem_Un);
  }
}
#endregion
#endregion

#region Bitwise operators
#region BitwiseAndOperator
public sealed class BitwiseAndOperator : NumericOperator
{
  internal BitwiseAndOperator() : base("BitwiseAnd", "op_BitwiseAnd") { }
  public override object Evaluate(object a, object b, Options options)
  {
    object ret;
    if(NormalizeTypesOrCallOverload(ref a, ref b, out ret)) return ret;

    switch(Convert.GetTypeCode(a))
    {
      case TypeCode.Int32: return (int)a & (int)b;
      case TypeCode.Int64: return (long)a & (long)b;
      case TypeCode.UInt32: return (uint)a & (uint)b;
      case TypeCode.UInt64: return (ulong)a & (ulong)b;
    }

    throw NoOperation(a, b);
  }

  protected override void EmitOp(CodeGenerator cg, ITypeInfo typeOnStack, bool signed)
  {
    cg.ILG.Emit(OpCodes.And);
  }
}
#endregion

#region BitwiseOrOperator
public sealed class BitwiseOrOperator : NumericOperator
{
  internal BitwiseOrOperator() : base("BitwiseOr", "op_BitwiseOr") { }
  public override object Evaluate(object a, object b, Options options)
  {
    object ret;
    if(NormalizeTypesOrCallOverload(ref a, ref b, out ret)) return ret;

    switch(Convert.GetTypeCode(a))
    {
      case TypeCode.Int32: return (int)a | (int)b;
      case TypeCode.Int64: return (long)a | (long)b;
      case TypeCode.UInt32: return (uint)a | (uint)b;
      case TypeCode.UInt64: return (ulong)a | (ulong)b;
    }

    throw NoOperation(a, b);
  }

  protected override void EmitOp(CodeGenerator cg, ITypeInfo typeOnStack, bool signed)
  {
    cg.ILG.Emit(OpCodes.Or);
  }
}
#endregion

#region BitwiseXorOperator
public sealed class BitwiseXorOperator : NumericOperator
{
  internal BitwiseXorOperator() : base("BitwiseXor", "op_BitwiseXor") { }
  public override object Evaluate(object a, object b, Options options)
  {
    object ret;
    if(NormalizeTypesOrCallOverload(ref a, ref b, out ret)) return ret;

    switch(Convert.GetTypeCode(a))
    {
      case TypeCode.Int32: return (int)a ^ (int)b;
      case TypeCode.Int64: return (long)a ^ (long)b;
      case TypeCode.UInt32: return (uint)a ^ (uint)b;
      case TypeCode.UInt64: return (ulong)a ^ (ulong)b;
    }

    throw NoOperation(a, b);
  }

  protected override void EmitOp(CodeGenerator cg, ITypeInfo typeOnStack, bool signed)
  {
    cg.ILG.Emit(OpCodes.Xor);
  }
}
#endregion
#endregion

} // namespace Scripting.AST