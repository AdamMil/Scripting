using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using Scripting.AST;
using Scripting.Emit;

namespace Scripting.Runtime
{

#region Interfaces
public interface ICallable
{
  int MinArgs { get; }
  int MaxArgs { get; }

  object Call(params object[] args);
}

public interface ICallableWithKeywords : ICallable
{
  object Call(object[] positionalArgs, string[] keywords, object[] keywordValues);
}
#endregion

#region Binding
/// <summary>Represents a binding for a top level variable.</summary>
public sealed class Binding
{
  public Binding(string name, BindingContainer from) : this(name, Unbound, from) { }

  /// <param name="name">The name of the variable. This must not be empty.</param>
  /// <param name="value">The initial value of the variable. This can be set to <see cref="Unbound"/> if the value has
  /// not been defined yet.
  /// </param>
  /// <param name="from">The <see cref="BindingContainer"/> that created this binding. This must not be null.</param>
  public Binding(string name, object value, BindingContainer from)
  {
    if(from == null) throw new ArgumentNullException();
    if(string.IsNullOrEmpty(name)) throw new ArgumentException("Name must not be empty.");

    Value = value;
    Name  = name;
    From  = from;
  }

  public override int GetHashCode()
  {
    return Name.GetHashCode();
  }

  public object Value;
  public readonly string Name;
  public readonly BindingContainer From;

  public readonly static object Unbound = new Singleton("<UNBOUND>");
}
#endregion

#region BindingContainer
public abstract class BindingContainer
{
}
#endregion

#region BindingDictionary
/// <summary>A thread-safe container for variable bindings.</summary>
public sealed class BindingDictionary
{
  /// <summary>Binds a variable. If the variable was alreading bound from the same container, its value will be
  /// set, but if it was imported from another container, it will be rebound.
  /// </summary>
  public void Bind(string name, object value, BindingContainer from)
  {
    Binding bind;
    lock(Dict)
    {
      if(!Dict.TryGetValue(name, out bind) || bind.From != from)
      {
        Dict[name] = bind = new Binding(name, from);
      }
    }
    bind.Value = value;
  }

  /// <summary>Determines whether the given variable is bound within this dictionary and has a defined value (not equal
  /// to <see cref="Binding.Unbound"/>).
  /// </summary>
  public bool Contains(string name)
  {
    Binding bind;
    lock(Dict)
    {
      return Dict.TryGetValue(name, out bind) && bind.Value != Binding.Unbound;
    }
  }

  /// <summary>Returns the value of a bound variable. An exception will be thrown if the variable is unbound, or is
  /// bound to an undefined value.
  /// </summary>
  public object Get(string name)
  {
    Binding bind;
    lock(Dict)
    {
      if(!Dict.TryGetValue(name, out bind) || bind.Value == Binding.Unbound)
      {
        throw new UndefinedVariableException(name);
      }
    }
    return bind.Value;
  }

  /// <summary>Gets the binding for a variable. If the binding does not exist, it will be created.</summary>
  public Binding GetBinding(string name, BindingContainer from)
  {
    Binding bind;
    lock(Dict)
    {
      if(!Dict.TryGetValue(name, out bind))
      {
        Dict[name] = bind = new Binding(name, from);
      }
    }
    return bind;
  }

  /// <summary>Sets the value of a bound variable. If the variable is not bound, an exception will be thrown.</summary>
  public void Set(string name, object value)
  {
    Binding bind;
    lock(Dict)
    {
      if(!Dict.TryGetValue(name, out bind)) throw new UndefinedVariableException(name);
    }
    bind.Value = value;
  }

  public bool TryGet(string name, out object value)
  {
    Binding bind;
    lock(Dict)
    {
      if(!Dict.TryGetValue(name, out bind) || bind.Value == Binding.Unbound)
      {
        value = null;
        return false;
      }
    }
    value = bind.Value;
    return true;
  }

  public void Unbind(string name)
  {
    lock(Dict) Dict.Remove(name);
  }

  public readonly Dictionary<string,Binding> Dict = new Dictionary<string,Binding>();
}
#endregion

#region Function
public abstract class Function : ICallableWithKeywords
{
  public int MinArgs
  {
    get { return Template.ParameterCount; }
  }
  
  public int MaxArgs
  {
    get { return Template.HasListParameter ? -1 : Template.ParameterCount; }
  }
  
  public abstract object Call(params object[] args);
  public abstract object Call(object[] positionalArgs, string[] keywords, object[] keywordValues);

  public override string ToString()
  {
    return Template.Name == null ? "<function>" : "<function '"+Template.Name+"'>";
  }
  
  public FunctionTemplate Template;
}
#endregion

#region FunctionTemplate
public class FunctionTemplate
{
  public FunctionTemplate(IntPtr funcPtr, string name, string[] parameterNames, Type[] parameterTypes, int numRequired,
                          bool hasListParam, bool hasDictParam)
  {
    TopLevel                = TopLevel.Current;
    Function                = funcPtr;
    Name                    = name;
    ParameterNames          = parameterNames;
    ParameterTypes          = parameterTypes;
    ParameterCount          = ParameterNames == null ? 0 : ParameterNames.Length;
    RequiredParameterCount  = numRequired;
    HasListParameter        = hasListParam;
    HasDictParameter        = hasDictParam;
  }

  public readonly TopLevel TopLevel;
  public readonly string Name;
  public readonly IntPtr Function;
  public readonly int ParameterCount, RequiredParameterCount;
  public readonly bool HasListParameter, HasDictParameter;

  /// <summary>Gets the name of the given parameter.</summary>
  public string GetParameterName(int index)
  {
    if(index < 0 || index >= ParameterCount) throw new ArgumentOutOfRangeException();
    return ParameterNames[index];
  }

  /// <summary>Gets the type of the given parameter.</summary>
  public Type GetParameterType(int index)
  {
    if(index < 0 || index >= ParameterCount) throw new ArgumentOutOfRangeException();
    return ParameterTypes == null ? typeof(object) : ParameterTypes[index];
  }

  /// <summary>Given a list of arguments and default argument values, returns a list of arguments appropriate for
  /// passing to the actual method.
  /// </summary>
  public object[] MakeArguments(object[] arguments, object[] defaultValues)
  {
    int numArgs = arguments.Length;
    if(!HasListParameter) // if there's no list parameter, we don't need to pack anything into it.
    {
      // if all the parameters are specified, simply return the array. it's possible that there's a dictionary
      // parameter, but it's assumed that the dictionary parameter is being passed as-is
      if(numArgs == ParameterCount)
      {
        return arguments;
      }
      else if(numArgs > ParameterCount) // otherwise, there are too many and no place to put them, so throw
      {
        throw new ArgumentException(Name+": expected at most "+ParameterCount+
                                    " positional arguments, but received "+numArgs);
      }
    }

    if(numArgs < RequiredParameterCount) // if there are too few parameters, throw an exception
    {
      throw new ArgumentException(Name+": expected at least "+RequiredParameterCount+
                                  " positional arguments, but received "+numArgs);
    }

    // if we've gotten here, then we have a list parameter and need to pack some arguments into it
    object[] newArgs = numArgs == ParameterCount ? arguments : new object[ParameterCount]; // don't reallocate if possible

    // the paramindex will be set to the list or dictionary parameter, whichever comes first
    int paramIndex = ParameterCount - (HasListParameter ? 1 : 0) - (HasDictParameter ? 1 : 0);
    // argIndex will be set to the index of the first argument to be packed into the list parameter, if any
    int argIndex = Math.Min(paramIndex, numArgs);

    // if we have a list parameter, paramIndex will be pointing to it. pack the remaining positional args into it.
    if(HasListParameter)
    {
      newArgs[paramIndex] = PackArguments(arguments, argIndex, numArgs-argIndex);
    }

    // if we have a dictionary parameter, it will either be at paramIndex or paramIndex+1.
    if(HasDictParameter)
    {
      newArgs[paramIndex + (HasListParameter ? 1 : 0)] = MakeKeywordDictionary(); // set it to an empty dictionary
    }

    if(arguments != newArgs) // if we reallocated the arguments array, copy the positional arguments from the original
    {
      Array.Copy(arguments, newArgs, argIndex);
    }

    // the number of optional parameters is equal to the number of normal parameters minus the number of normal args
    int optional = paramIndex - argIndex;
    if(optional != 0) // if we have any unspecified arguments, use the default values
    {
      Array.Copy(defaultValues, defaultValues.Length-optional, newArgs, argIndex, optional);
    }

    return newArgs;
  }

  /// <summary>Given positional arguments, default values, keywords, and keyword values, returns an array of arguments
  /// suitable for passing to the actual method.
  /// </summary>
  public object[] MakeArguments(object[] positionalArgs, object[] defaultValues,
                                string[] keywords, object[] keywordValues)
  {
    throw new NotImplementedException();
  }

  public static readonly Type[] ConstructorTypes = new Type[]
    {
      typeof(IntPtr), typeof(string), typeof(string[]), typeof(Type[]),
      typeof(int), typeof(bool), typeof(bool), typeof(bool)
    };

  protected virtual System.Collections.IDictionary MakeKeywordDictionary()
  {
    return new System.Collections.Hashtable();
  }

  protected virtual object PackArguments(object[] arguments, int index, int length)
  {
    object[] packed = new object[length];
    Array.Copy(arguments, index, packed, 0, length);
    return packed;
  }
  
  readonly string[] ParameterNames;
  readonly Type[] ParameterTypes;
}
#endregion

#region InterpretedFunction
public sealed class InterpretedFunction : Function
{
  public InterpretedFunction(Language language, string name, string[] paramNames, ASTNode body)
    : this(language, name, paramNames, false, false, body) { }

  public InterpretedFunction(Language language, string name, string[] paramNames, bool hasList, bool hasDict,
                             ASTNode body)
    : this(language, name, ParameterNode.GetParameters(paramNames), body) { }

  public InterpretedFunction(Language language, string name, ParameterNode[] parameters, ASTNode body)
  {
    int numRequired, numOptional;
    bool hasList, hasDict;
    ParameterNode.Validate(parameters, out numRequired, out numOptional, out hasList, out hasDict);

    Template = (FunctionTemplate)language.FunctionTemplateType.GetConstructor(FunctionTemplate.ConstructorTypes)
                 .Invoke(new object[] { IntPtr.Zero, name, ParameterNode.GetNames(parameters),
                                        ParameterNode.GetTypes(parameters), numRequired, hasList, hasDict, false });
    Body = body;

    if(numOptional != 0)
    {
      DefaultValues = new object[numOptional];
      for(int i=0; i<numOptional; i++)
      {
        DefaultValues[i] = parameters[i-numRequired].DefaultValue.Evaluate();
      }
    }
  }

  public override object Call(params object[] args)
  {
    return DoCall(Template.MakeArguments(args, DefaultValues));
  }

  public override object Call(object[] positionalArgs, string[] keywords, object[] keywordValues)
  {
    return DoCall(Template.MakeArguments(positionalArgs, DefaultValues, keywords, keywordValues));
  }

  object DoCall(object[] args)
  {
    TopLevel oldTop = TopLevel.Current;
    bool newEnv = false;
    try
    {
      TopLevel.Current = Template.TopLevel;

      if(Template.ParameterCount != 0) // if the current function has parameters, bind them in a new scope
      {
        InterpreterEnvironment env = InterpreterEnvironment.PushNew();
        for(int i=0; i<Template.ParameterCount; i++)
        {
          env.Bind(Template.GetParameterName(i), args[i]);
        }
      }

      return Body.Evaluate();
    }
    finally
    {
      if(newEnv) InterpreterEnvironment.Pop();
      TopLevel.Current = oldTop;
    }
  }

  readonly ASTNode Body;
  readonly object[] DefaultValues;
}
#endregion

#region InterpreterEnvironment
/// <summary>Represents the variable environment</summary>
public sealed class InterpreterEnvironment
{
  public InterpreterEnvironment(InterpreterEnvironment parent)
  {
    this.parent = parent;
  }

  /// <summary>Binds a variable at this level of the environment.</summary>
  public void Bind(string name, object value)
  {
    if(name == null) throw new ArgumentNullException();
    if(dict == null)
    {
      dict = new System.Collections.Specialized.ListDictionary();
    }
    else if(dict.Contains(name))
    {
      throw new ArgumentException("A variable cannot be bound multiple times at a given level.");
    }
    dict[name] = value;
  }

  /// <summary>Gets the value of a bound variable. If the variable is not bound at this level, the parent environment
  /// will be consulted up to the top level. An exception will be thrown if the variable has not been bound.
  /// </summary>
  public object Get(string name)
  {
    if(name == null) throw new ArgumentNullException();

    object ret = dict == null ? null : dict[name];
    if(ret == null && (dict == null || !dict.Contains(name)))
    {
      // if the variable is not bound at this level, ask the parent (or top level if there is no parent)
      ret = parent != null ? parent.Get(name) : TopLevel.Current.Get(name);
    }
    return ret;
  }

  /// <summary>Sets the value of a bound variable. If the variable is not bound at this level, the parent environment
  /// will be consulted up to the top level. An exception will be thrown if the variable has not been bound.
  /// </summary>
  public void Set(string name, object value)
  {
    if(name == null) throw new ArgumentNullException();

    if(dict != null && dict.Contains(name)) // if the variable is bound at this level, set it
    {
      dict[name] = value;
    }
    else if(parent != null) // otherwise, the variable is not bound at this level. try the parent if there is one
    {
      parent.Set(name, value);
    }
    else // or the top level if there's not.
    {
      TopLevel.Current.Set(name, value);
    }
  }

  /// <summary>Pushes a new interpreter environment frame onto the stack, updates the <see cref="Current"/> pointer,
  /// and returns it.
  /// </summary>
  /// <returns>The new current <see cref="InterpreterEnvironment"/>.</returns>
  public static InterpreterEnvironment PushNew()
  {
    InterpreterEnvironment newEnv = new InterpreterEnvironment(Current);
    Current = newEnv;
    return newEnv;
  }
  
  /// <summary>Pops the current interpreter environment.</summary>
  /// <exception cref="InvalidOperationException">Thrown if the environment stack for this thread is empty.</exception>
  public static void Pop()
  {
    InterpreterEnvironment env = Current;
    if(env == null) throw new InvalidOperationException();
    Current = env.parent;
  }

  [ThreadStatic] public static InterpreterEnvironment Current;

  readonly InterpreterEnvironment parent;
  System.Collections.Specialized.ListDictionary dict;
}
#endregion

#region RG (stuff that can't be written in C#)
public sealed class RG
{ 
  static RG()
  {
    string dllPath = System.IO.Path.Combine(Scripting.InstallationPath, "Scripting.LowLevel.dll");

    AssemblyGenerator ag = new AssemblyGenerator("Scripting.LowLevel", dllPath, false);
    TypeGenerator tg;
    CodeGenerator cg;

    #region Closure
    {
      tg = ag.DefineType(TypeAttributes.Public|TypeAttributes.Sealed, "Scripting.Backend.Closure", typeof(Function));
      tg.MarkAsNonUserCode();

      FieldSlot defaultValues = tg.DefineField(FieldAttributes.Private|FieldAttributes.InitOnly,
                                               "defaultValues", TypeWrapper.ObjectArray);
      FieldSlot template = new FieldSlot(new ThisSlot(tg), TypeWrapper.Get(typeof(Function)).GetField("Template"));

      #region Constructor(FunctionTemplate)
      cg = tg.DefineConstructor(TypeWrapper.Get(typeof(FunctionWrapper)));
      cg.EmitThis(); // this.Template = template
      cg.EmitArgGet(0);
      cg.EmitFieldSet(typeof(Function), "Template");
      cg.EmitReturn();
      cg.Finish();
      #endregion

      #region Constructor(FunctionTemplate, object[])
      cg = tg.DefineConstructor(TypeWrapper.Get(typeof(FunctionTemplate)), TypeWrapper.ObjectArray);
      cg.EmitThis(); // this.Template = template
      cg.EmitArgGet(0);
      cg.EmitFieldSet(typeof(Function), "Template");
      cg.EmitThis(); // this.defaultValues = defaultValues
      cg.EmitArgGet(1);
      cg.EmitFieldSet(defaultValues.Field);
      cg.EmitReturn();
      cg.Finish();
      #endregion

      #region Call(object[])
      cg = tg.DefineMethodOverride("Call", true, TypeWrapper.ObjectArray);
      Slot oldTop = cg.AllocLocalTemp(TypeWrapper.TopLevel);
      cg.EmitFieldGet(typeof(TopLevel), "Current");     // TopLevel oldTop = TopLevel.Current;
      oldTop.EmitSet(cg);

      cg.ILG.BeginExceptionBlock();                     // try {
      template.EmitGet(cg);                             // TopLevel.Current = Template.TopLevel
      cg.EmitFieldGet(typeof(FunctionTemplate), "TopLevel");
      cg.EmitFieldSet(typeof(TopLevel), "Current");

      template.EmitGet(cg);                             // return Template.FuncPtr(Template.FixArgs(
      cg.EmitArgGet(0);                                 //      args,
      defaultValues.EmitGet(cg);                        //      Defaults))
      cg.EmitCall(typeof(FunctionTemplate), "MakeArguments", typeof(object[]), typeof(object[]));

      template.EmitGet(cg);
      cg.EmitFieldGet(typeof(FunctionTemplate), "Function");
      cg.ILG.Emit(OpCodes.Tailcall); // TODO: with the addition of the exception block, this now has no effect. see if we can somehow preserve tail calling
      cg.ILG.EmitCalli(OpCodes.Calli, CallingConventions.Standard, typeof(object),
                       new Type[] { typeof(object[]) }, null);
      cg.EmitReturn();
      cg.ILG.BeginFinallyBlock();                       // } finally {
      oldTop.EmitGet(cg);                               // TopLevel.Current = oldTop;
      cg.EmitFieldSet(typeof(TopLevel), "Current");
      cg.ILG.EndExceptionBlock();                       // }
      cg.FreeLocalTemp(oldTop);
      cg.EmitReturn();
      cg.Finish();
      #endregion

      #region Call(object[], string[], object[])
      cg = tg.DefineMethodOverride("Call", true, TypeWrapper.ObjectArray,
                                   TypeWrapper.Get(typeof(string[])), TypeWrapper.ObjectArray);
      oldTop = cg.AllocLocalTemp(TypeWrapper.TopLevel);
      cg.EmitFieldGet(typeof(TopLevel), "Current");     // TopLevel oldTop = TopLevel.Current;
      oldTop.EmitSet(cg);

      cg.ILG.BeginExceptionBlock();                     // try {
      template.EmitGet(cg);                             // TopLevel.Current = Template.TopLevel

      template.EmitGet(cg);                             // return Template.FuncPtr(Template.MakeArgs(
      cg.EmitArgGet(0);                                 //      Positional,
      defaultValues.EmitGet(cg);                        //      Defaults,
      cg.EmitArgGet(1);                                 //      Keywords,
      cg.EmitArgGet(2);                                 //      Values))
      cg.EmitCall(typeof(FunctionTemplate), "MakeArguments",
                  typeof(object[]), typeof(object[]), typeof(string[]), typeof(object[]));

      template.EmitGet(cg);
      cg.EmitFieldGet(typeof(FunctionTemplate), "Function");
      cg.ILG.Emit(OpCodes.Tailcall); // TODO: with the addition of the exception block, this now has no effect. see if we can somehow preserve tail calling
      cg.ILG.EmitCalli(OpCodes.Calli, CallingConventions.Standard, typeof(object),
                       new Type[] { typeof(object[]) }, null);
      cg.EmitReturn();
      cg.ILG.BeginFinallyBlock();                       // } finally {
      oldTop.EmitGet(cg);                               // TopLevel.Current = oldTop;
      cg.EmitFieldSet(typeof(TopLevel), "Current");
      cg.ILG.EndExceptionBlock();                       // }
      cg.FreeLocalTemp(oldTop);
      cg.EmitReturn();
      cg.Finish();
      #endregion

      ClosureType = tg.FinishType();
    }
    #endregion

    try { ag.Save(); } catch { }
  }
 
  public static readonly Type ClosureType;
}
#endregion

#region Scripting
public static class Scripting
{
  public static string InstallationPath
  {
    get { return "e:/"; }
  }
}
#endregion

#region Singleton
public sealed class Singleton
{
  public Singleton(string name)
  {
    if(string.IsNullOrEmpty(name)) throw new ArgumentException("Name must not be empty.");
    this.name = name;
  }

  public override string ToString()
  {
    return name;
  }

  readonly string name;
}
#endregion

#region TopLevel
public sealed class TopLevel : BindingContainer
{ 
  public void Bind(string name, object value)
  {
    Globals.Bind(name, value, this);
  }

  public bool Contains(string name)
  {
    return Globals.Contains(name);
  }

  public void ExportAll(TopLevel topLevel)
  {
    foreach(KeyValuePair<string,Binding> pair in Globals.Dict)
    {
      topLevel.Globals.Dict[pair.Key] = pair.Value;
    }
  }

  public object Get(string name)
  {
    return Globals.Get(name);
  }

  public Binding GetBinding(string name)
  {
    return Globals.GetBinding(name, this);
  }

  public void Set(string name, object value)
  {
    Globals.Set(name, value);
  }

  public void Unbind(string name)
  {
    Globals.Unbind(name);
  }

  public bool TryGet(string name, out object value)
  {
    return Globals.TryGet(name, out value);
  }

  public readonly BindingDictionary Globals = new BindingDictionary();

  [ThreadStatic] public static TopLevel Current;
}
#endregion

} // namespace Scripting.Runtime