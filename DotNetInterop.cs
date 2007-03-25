using System;
using System.Reflection;
using System.Reflection.Emit;
using Scripting.Emit;
using Scripting.Runtime;

namespace Scripting.Emit
{

#region DotNetInterop
public static class DotNetInterop
{
  internal static ITypeInfo MakeMethodWrapper(TypeGenerator parentClass, Signature signature,
                                         IMethodBase method, string name)
  {
    return MakeMethodWrapper(parentClass, signature, name, method as IConstructorInfo);
  }

  static ITypeInfo MakeMethodWrapper(TypeGenerator parentClass, Signature signature, string name, IConstructorInfo cons)
  {
    TypeGenerator tg =
      parentClass.DefineNestedType(TypeAttributes.NestedPublic|TypeAttributes.Sealed, name,
                                   signature.IsConstructor ? typeof(MethodWrapper) : typeof(FunctionWrapper));

    // number of normal (non param-array) arguments, and min/max allowable arguments
    int numNormalArgs = signature.HasParamArray ? signature.ParamTypes.Length - 1 : signature.ParamTypes.Length;
    int minArgs = numNormalArgs;
    int maxArgs = signature.HasParamArray ? -1 : signature.ParamTypes.Length;
    
    CodeGenerator cg;

    #region Constructor
    if(!signature.IsConstructor) // normal methods take an IntPtr and a boolean and simply pass it to the base class.
    {
      cg = tg.DefineChainedConstructor(TypeWrapper.IntPtr, TypeWrapper.Bool);
    }
    else // constructors have a default constructor that passes 'false' to the base class
    {
      cg = tg.DefineDefaultConstructor();
      cg.EmitThis();
      cg.EmitBool(false);
      cg.EmitCall(tg.BaseType.GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic, TypeWrapper.Bool));
    }
    // whichever constructor was used, finish it
    cg.EmitReturn();
    cg.Finish();
    #endregion
    
    #region MinArgs and MaxArgs
    cg = tg.DefinePropertyOverride("MinArgs");
    cg.EmitInt(minArgs);
    cg.EmitReturn();
    cg.Finish();
    
    cg = tg.DefinePropertyOverride("MaxArgs");
    cg.EmitInt(maxArgs);
    cg.EmitReturn();
    cg.Finish();
    #endregion
    
    MethodInfo checkArity = null;
    #region CheckArity
    if(minArgs != 0 || maxArgs != -1) // if the argument counts aren't unlimited, emit a function to check the arity
    {
      cg = tg.DefineStaticMethod(MethodAttributes.Private, "CheckArity", TypeWrapper.Void, TypeWrapper.ObjectArray);
      
      Slot intSlot = cg.AllocLocalTemp(TypeWrapper.Int);

      if(minArgs != 0) // check minimum
      {
        Label end = cg.ILG.DefineLabel();
        cg.EmitArgGet(0);
        cg.ILG.Emit(OpCodes.Ldlen);
        cg.EmitInt(minArgs);
        cg.ILG.Emit(OpCodes.Bge_S, end);
        
        cg.EmitString((signature.IsConstructor ? "Constructor" : "Function") + " expects at least ");
        cg.EmitInt(minArgs);
        intSlot.EmitSet(cg);
        intSlot.EmitGetAddr(cg);
        cg.EmitCall(typeof(int), "ToString", Type.EmptyTypes);
        cg.EmitString(" arguments, but received ");
        cg.EmitArgGet(0);
        cg.ILG.Emit(OpCodes.Ldlen);
        intSlot.EmitSet(cg);
        intSlot.EmitGetAddr(cg);
        cg.EmitCall(typeof(int), "ToString", Type.EmptyTypes);
        cg.EmitCall(typeof(string), "Concat", typeof(string), typeof(string), typeof(string), typeof(string));
        cg.EmitNew(typeof(ArgumentException), typeof(string));
        cg.ILG.Emit(OpCodes.Throw);
        
        cg.ILG.MarkLabel(end);
      }
      
      if(maxArgs != -1)
      {
        Label end = cg.ILG.DefineLabel();
        cg.EmitArgGet(0);
        cg.ILG.Emit(OpCodes.Ldlen);
        cg.EmitInt(maxArgs);
        cg.ILG.Emit(OpCodes.Ble_S, end);

        cg.EmitString((signature.IsConstructor ? "Constructor" : "Function") + " expects at most ");
        cg.EmitInt(maxArgs);
        intSlot.EmitSet(cg);
        intSlot.EmitGetAddr(cg);
        cg.EmitCall(typeof(int), "ToString", Type.EmptyTypes);
        cg.EmitString(" arguments, but received ");
        cg.EmitArgGet(0);
        cg.ILG.Emit(OpCodes.Ldlen);
        intSlot.EmitSet(cg);
        intSlot.EmitGetAddr(cg);
        cg.EmitCall(typeof(int), "ToString", Type.EmptyTypes);
        cg.EmitCall(typeof(string), "Concat", typeof(string), typeof(string), typeof(string), typeof(string));
        cg.EmitNew(typeof(ArgumentException), typeof(string));
        cg.ILG.Emit(OpCodes.Throw);

        cg.ILG.MarkLabel(end);
      }

      cg.EmitReturn();
      cg.Finish();

      checkArity = (MethodInfo)cg.Method.Method;
    }
    #endregion
    
    #region Call(object[])
    cg = tg.DefineMethodOverride("Call", TypeWrapper.ObjectArray);
    
    if(checkArity != null) // check the arity if any
    {
      cg.EmitArgGet(0);
      cg.EmitCall(checkArity);
    }

    for(int i=0; i<minArgs; i++) // required arguments
    {
      cg.EmitArgGet(0); // load the object reference
      cg.EmitInt(i);
      cg.ILG.Emit(OpCodes.Ldelem_Ref);

      if(signature.ParamTypes[i].DotNetType.IsByRef || signature.ParamTypes[i].DotNetType.IsPointer)
      {
        throw new NotImplementedException();
      }
      
      cg.EmitRuntimeConversion(TypeWrapper.Object, signature.ParamTypes[i]);
    }
    
    if(minArgs < numNormalArgs) // if there are any optional arguments
    {
      throw new NotImplementedException();
    }
    
    if(signature.HasParamArray)
    {
      throw new NotImplementedException();
    }
    
    // now that we've pushed the parameters, emit the actual call
    if(signature.IsConstructor) // if it's a constructor wrapper, emit a call to the constructor
    {
      cg.EmitNew(cons);
    }
    else
    {
      cg.EmitThis();
      cg.EmitFieldGet(typeof(FunctionWrapper), "MethodPtr");

      if(!signature.ReturnType.IsValueType) // we can tail call if we don't need to box or emit afterwards
      {
        cg.ILG.Emit(OpCodes.Tailcall);
      }

      cg.ILG.EmitCalli(OpCodes.Calli, signature.Convention, signature.ReturnType.DotNetType,
                       ReflectionWrapperHelper.Unwrap(signature.ParamTypes), null);
    }

    cg.EmitSafeConversion(signature.ReturnType, TypeWrapper.Object);

    cg.EmitReturn();
    cg.Finish();
    #endregion
    
    return tg;
  }
}
#endregion

#region Signature
sealed class Signature
{
  public Signature(IMethodBase method) : this(method, false) { }

  /// <param name="dontRequireThisPtr">All methods are treated as static methods. Normally, instance methods are
  /// converted to static methods by adding the declaring type as the first parameter. If this parameter is true,
  /// the declaring type will not be added as the first parameter. This is only useful in the case that the 'this'
  /// pointer will be automatically added by the runtime, for example when invoking a delegate.
  /// </param>
  public Signature(IMethodBase method, bool dontRequireThisPtr)
  {
    IParameterInfo[] parameters = method.GetParameters();

    MethodBase methodBase = (MethodBase)method.Method;
    
    IsConstructor  = methodBase is ConstructorInfo;
    RequireThisPtr = !IsConstructor && !methodBase.IsStatic && !dontRequireThisPtr;
    Convention     = methodBase.CallingConvention == CallingConventions.VarArgs ?
                       CallingConventions.VarArgs : CallingConventions.Standard;
    ReturnType     = IsConstructor ? method.DeclaringType : ((IMethodInfo)method).ReturnType;
    ParamTypes     = new ITypeInfo[parameters.Length + (RequireThisPtr ? 1 : 0)];
    HasParamArray  = parameters.Length != 0 && parameters[parameters.Length-1].IsParamArray;
    
    if(RequireThisPtr)
    {
      ITypeInfo thisPtrType = method.DeclaringType;
      // methods need 'this' /pointers/, so we make sure it's a pointer type (eg, int becomes int*)
      if(thisPtrType.IsValueType)
      {
        thisPtrType = TypeWrapper.Get(thisPtrType.DotNetType.MakePointerType());
      }
      ParamTypes[0] = thisPtrType;
    }

    for(int i=0; i<parameters.Length; i++)
    {
      ParamTypes[i + (RequireThisPtr ? 1 : 0)] = parameters[i].ParameterType;
    }
  }

  public override bool Equals(object obj)
  {
 	  Signature other = obj as Signature;
 	  if(other == null) return false;
 	  
 	  // check all the easily-comparable members
 	  if(ParamTypes.Length != other.ParamTypes.Length || HasParamArray != other.HasParamArray ||
 	     RequireThisPtr != other.RequireThisPtr || ReturnType != other.ReturnType || Convention != other.Convention ||
 	     IsConstructor != other.IsConstructor)
    {
      return false;
    }

    for(int i=0; i<ParamTypes.Length; i++) // then check each parameter type
    {
      if(ParamTypes[i] != other.ParamTypes[i]) return false;
    }
    
    return true;
  }
  
  public override int GetHashCode()
  {
 	  int hash = ReturnType.GetHashCode();
 	  for(int i=0; i<ParamTypes.Length; i++)
 	  {
 	    hash ^= ParamTypes[i].GetHashCode();
 	  }
 	  return hash;
  }

  public readonly ITypeInfo[] ParamTypes;
  public readonly ITypeInfo ReturnType;
  public readonly CallingConventions Convention;
  public readonly bool IsConstructor, RequireThisPtr, HasParamArray;
}
#endregion

} // namespace Scripting.Emit

namespace Scripting.Runtime
{

#region MethodWrapper
/// <summary>Base class of all function wrappers (including functions, delegates, and constructors).</summary>
public abstract class MethodWrapper : ICallableWithKeywords
{
  protected MethodWrapper(bool isStatic)
  {
    IsStatic = isStatic;
  }

  public readonly bool IsStatic;

  public abstract int MinArgs { get; }
  public abstract int MaxArgs { get; }

  public abstract object Call(object[] args);

  public object Call(object[] positionalArgs, string[] keywords, object[] keywordValues)
  {
    throw new NotImplementedException();
  }
}
#endregion

#region FunctionWrapper
/// <summary>Base class of non-constructor method wrappers.</summary>
public abstract class FunctionWrapper : MethodWrapper
{
  protected FunctionWrapper(IntPtr methodPtr, bool isStatic) : base(isStatic)
  {
    MethodPtr = methodPtr;
  }

  protected readonly IntPtr MethodPtr;
}
#endregion

} // namespace Scripting.Runtime