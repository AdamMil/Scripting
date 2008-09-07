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
    int numNormalArgs = signature.ParamTypes.Length - (signature.HasParamArray ? 1 : 0);
    int minArgs = numNormalArgs;
    int maxArgs = signature.HasParamArray ? -1 : signature.ParamTypes.Length;

    FieldSlot thisField = null;
    CodeGenerator cg;

    #region Constructors
    if(!signature.IsConstructor) // normal methods take an IntPtr
    {
      cg = tg.DefineChainedConstructor(TypeWrapper.IntPtr);
    }
    else // constructors take no arguments
    {
      cg = tg.DefineChainedConstructor();
    }
    // whichever constructor was used, finish it
    cg.EmitReturn();
    cg.Finish();

    // if the method needs a 'this' pointer, create a constructor that takes the 'this' pointer
    if(!signature.IsConstructor && signature.RequireThisPtr)
    {
      thisField = tg.DefineField(FieldAttributes.Private | FieldAttributes.InitOnly, "thisPtr",
                                 signature.ParamTypes[0]);

      cg = tg.DefineConstructor(TypeWrapper.IntPtr, signature.ParamTypes[0]);
      cg.EmitThis();
      cg.EmitArgGet(0);
      cg.EmitCall(tg.BaseType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, TypeWrapper.IntPtr));
      cg.EmitThis();
      cg.EmitArgGet(1);
      cg.EmitFieldSet(thisField.Field);

      cg.EmitReturn();
      cg.Finish();
    }
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
    
    // TODO: add a custom attribute to the Call() method to cause the debugger to step through it?
    #region Call(object[])
    cg = tg.DefineMethodOverride("Call", TypeWrapper.ObjectArray);

    if(minArgs != 0 || maxArgs != -1) // if the argument counts aren't unlimited, emit a call to check the arity
    {
      cg.EmitArgGet(0);
      cg.ILG.Emit(OpCodes.Ldlen);

      if(thisField != null)
      {
        thisField.EmitGet(cg); // add one to the length if we have a 'this' pointer
        Label noThisPtr = cg.ILG.DefineLabel();
        cg.ILG.Emit(OpCodes.Brfalse_S, noThisPtr);
        cg.EmitInt(1);
        cg.ILG.Emit(OpCodes.Add);
        cg.ILG.MarkLabel(noThisPtr);
      }

      cg.EmitInt(minArgs);
      cg.EmitInt(maxArgs);
      cg.EmitCall(typeof(MethodWrapper), "CheckArity");
    }

    // if a 'this' pointer is specified, the arguments taken from the array will be shifted by one in position, so
    // we'll need a variable to keep track of the index
    Slot indexSlot = thisField == null ? null : cg.AllocLocalTemp(TypeWrapper.Int);
    if(indexSlot != null)
    {
      cg.EmitInt(0);
      indexSlot.EmitSet(cg);
    }

    for(int i=0; i<minArgs; i++) // required arguments
    {
      Label firstArgDone = new Label();

      if(i == 0 && thisField != null)
      {
        Label noThisPtr = cg.ILG.DefineLabel();
        firstArgDone = cg.ILG.DefineLabel();

        thisField.EmitGet(cg);
        cg.ILG.Emit(OpCodes.Brfalse_S, noThisPtr);
        thisField.EmitGet(cg);
        cg.ILG.Emit(OpCodes.Br_S, firstArgDone);
        cg.ILG.MarkLabel(noThisPtr);
      }

      cg.EmitArgGet(0); // load the object reference from the object[]
      if(indexSlot != null)
      {
        indexSlot.EmitGet(cg);
        if(i < signature.ParamTypes.Length-1) // increment the index after using it, if it's not the last one
        {
          cg.EmitDup();
          cg.EmitInt(1);
          cg.ILG.Emit(OpCodes.Add);
          indexSlot.EmitSet(cg);
        }
      }
      else
      {
        cg.EmitInt(i);
      }
      cg.ILG.Emit(OpCodes.Ldelem_Ref);

      if(signature.ParamTypes[i].DotNetType.IsByRef || signature.ParamTypes[i].DotNetType.IsPointer)
      {
        throw new NotImplementedException();
      }
      
      cg.EmitRuntimeConversion(TypeWrapper.Unknown, signature.ParamTypes[i]);

      if(i == 0 && thisField != null) cg.ILG.MarkLabel(firstArgDone);
    }
    
    if(minArgs < numNormalArgs) // if there are any optional arguments
    {
      throw new NotImplementedException();
    }
    
    if(signature.HasParamArray)
    {
      throw new NotImplementedException();
    }

    if(indexSlot != null) cg.FreeLocalTemp(indexSlot);

    // now that we've pushed the parameters, emit the actual call
    if(signature.IsConstructor) // if it's a constructor wrapper, emit a call to the constructor
    {
      cg.EmitNew(cons);
    }
    else
    {
      cg.EmitThis();
      cg.EmitFieldGet(typeof(FunctionWrapper), "MethodPtr");

      if(!signature.ReturnType.IsValueType) // we can tail call if we don't need to box afterwards
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

  public override string ToString()
  {
    System.Text.StringBuilder sb = new System.Text.StringBuilder();

    if(IsConstructor) sb.Append("cons ");
    if(RequireThisPtr) sb.Append("this ");

    sb.Append(ReturnType.Name).Append(" (");
    for(int i=0; i<ParamTypes.Length; i++)
    {
      if(i != 0) sb.Append(", ");
      if(HasParamArray && i == ParamTypes.Length-1) sb.Append(" params ");
      sb.Append(ParamTypes[i].Name);
    }
    sb.Append(')');

    return sb.ToString();
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
  public abstract int MinArgs { get; }
  public abstract int MaxArgs { get; }

  public abstract object Call(object[] args);

  public object Call(object[] positionalArgs, string[] keywords, object[] keywordValues)
  {
    throw new NotImplementedException();
  }

  protected static void CheckArity(int argCount, int min, int max)
  {
    if(argCount < min)
    {
      throw new ArgumentException("Method expects at least " + min.ToString() + " arguments, but received " +
                                  argCount.ToString());
    }

    if(max != -1 && argCount > max)
    {
      throw new ArgumentException("Method expects at most " + max.ToString() + " arguments, but received " +
                                  argCount.ToString());
    }
  }
}
#endregion

#region FunctionWrapper
/// <summary>Base class of non-constructor method wrappers.</summary>
public abstract class FunctionWrapper : MethodWrapper
{
  protected FunctionWrapper(IntPtr methodPtr)
  {
    MethodPtr = methodPtr;
  }

  protected readonly IntPtr MethodPtr;
}
#endregion

} // namespace Scripting.Runtime