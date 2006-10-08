using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Scripting.Emit
{

public sealed class TypeGenerator
{
  public TypeGenerator(AssemblyGenerator assembly, TypeBuilder builder)
  {
    Assembly    = assembly;
    TypeBuilder = builder;
  }

  public Type BaseType
  {
    get { return TypeBuilder.BaseType; }
  }

  public bool IsSealed
  {
    get { return (TypeBuilder.Attributes&TypeAttributes.Sealed) != 0; }
  }

  /// <summary>Creates a public constructor with the given parameter types, and emits code to call the base constructor
  /// with the same parameter types.
  /// </summary>
  public CodeGenerator DefineChainedConstructor(params Type[] paramTypes)
  {
    ConstructorInfo ci = TypeBuilder.BaseType.GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,
                                                             null, paramTypes, null);
    if(ci == null || ci.IsPrivate) throw new ArgumentException("No non-private constructor could be found.");
    return DefineChainedConstructor(ci);
  }

  /// <summary>Creates a public constructor with the same parameters as the given constructor, and emits code to call
  /// the base constructor with those parameters.
  /// </summary>
  public CodeGenerator DefineChainedConstructor(ConstructorInfo parent)
  {
    return DefineChainedConstructor(MethodAttributes.Public, parent);
  }

  /// <summary>Creates a constructor with the same parameters as the given constructor, and emits code to call
  /// the base constructor with those parameters.
  /// </summary>
  public CodeGenerator DefineChainedConstructor(MethodAttributes attributes, ConstructorInfo parent)
  {
    ParameterInfo[] parameters = parent.GetParameters();
    Type[] paramTypes = GetTypes(parameters);

    ConstructorBuilder cb = TypeBuilder.DefineConstructor(attributes, CallingConventions.Standard, paramTypes);

    for(int i=0; i<parameters.Length; i++) // define the parameters
    {
      ParameterBuilder pb = cb.DefineParameter(i+1, parameters[i].Attributes, parameters[i].Name);
      // add the ParamArray attribute to the parameter if the parent class has it
      if(parameters[i].IsDefined(typeof(ParamArrayAttribute), false))
      {
        pb.SetCustomAttribute(CG.GetCustomAttributeBuilder(typeof(ParamArrayAttribute)));
      }
    }

    CodeGenerator cg = new CodeGenerator(this, cb);
    // emit the code to call the parent constructor
    cg.EmitThis();
    for(int i=0; i<parameters.Length; i++) cg.EmitArgGet(i);
    cg.EmitCall(parent);
    // return the code generator so the user can add other code
    return cg;
  }

  /// <summary>Defines a new public constructor that takes the given parameter types.</summary>
  public CodeGenerator DefineConstructor(params Type[] types)
  {
    return DefineConstructor(MethodAttributes.Public, types);
  }

  /// <summary>Defines a new constructor that takes the given parameter types.</summary>
  public CodeGenerator DefineConstructor(MethodAttributes attributes, params Type[] types)
  {
    return new CodeGenerator(this, TypeBuilder.DefineConstructor(attributes, CallingConventions.Standard, types));
  }

  /// <summary>Defines a new public default constructor.</summary>
  public CodeGenerator DefineDefaultConstructor()
  {
    return DefineDefaultConstructor(MethodAttributes.Public);
  }

  /// <summary>Defines a new default constructor.</summary>
  public CodeGenerator DefineDefaultConstructor(MethodAttributes attributes)
  {
    return new CodeGenerator(this, TypeBuilder.DefineDefaultConstructor(attributes));
  }

  public FieldSlot DefineField(string name, Type type)
  {
    return DefineField(FieldAttributes.Public, name, type);
  }

  public FieldSlot DefineField(FieldAttributes attrs, string name, Type type)
  {
    if(string.IsNullOrEmpty(name)) throw new ArgumentException("Name must not be empty.");
    if(type == null) throw new ArgumentNullException();
    FieldBuilder fieldBuilder = TypeBuilder.DefineField(name, type, attrs);
    return new FieldSlot(fieldBuilder.IsStatic ? null : new ThisSlot(TypeBuilder), fieldBuilder);
  }

  public CodeGenerator DefineMethod(string name, Type retType, params Type[] paramTypes)
  {
    return DefineMethod(MethodAttributes.Public, name, IsSealed, retType, paramTypes);
  }

  public CodeGenerator DefineMethod(string name, bool final, Type retType, params Type[] paramTypes)
  {
    return DefineMethod(MethodAttributes.Public, name, final, retType, paramTypes);
  }

  public CodeGenerator DefineMethod(MethodAttributes attrs, string name, Type retType, params Type[] paramTypes)
  {
    return DefineMethod(attrs, name, IsSealed, retType, paramTypes);
  }

  public CodeGenerator DefineMethod(MethodAttributes attrs, string name, bool final,
                                    Type retType, params Type[] paramTypes)
  {
    if((attrs&MethodAttributes.Static) != 0) attrs &= ~MethodAttributes.Final; // static methods can't be marked final
    else if(final) attrs |= MethodAttributes.Final;
    return new CodeGenerator(this, TypeBuilder.DefineMethod(name, attrs, retType, paramTypes));
  }

  public CodeGenerator DefineMethodOverride(string name)
  {
    return DefineMethodOverride(TypeBuilder.BaseType, name, IsSealed);
  }

  public CodeGenerator DefineMethodOverride(string name, params Type[] paramTypes)
  {
    return DefineMethodOverride(TypeBuilder.BaseType, name, IsSealed, paramTypes);
  }

  public CodeGenerator DefineMethodOverride(string name, bool final)
  {
    BindingFlags searchFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    return DefineMethodOverride(TypeBuilder.BaseType.GetMethod(name, searchFlags), final);
  }

  public CodeGenerator DefineMethodOverride(string name, bool final, params Type[] paramTypes)
  {
    return DefineMethodOverride(TypeBuilder.BaseType, name, final, paramTypes);
  }

  public CodeGenerator DefineMethodOverride(Type type, string name)
  {
    return DefineMethodOverride(type, name, IsSealed);
  }

  public CodeGenerator DefineMethodOverride(Type type, string name, params Type[] paramTypes)
  {
    return DefineMethodOverride(type, name, IsSealed, paramTypes);
  }

  public CodeGenerator DefineMethodOverride(Type type, string name, bool final)
  {
    return DefineMethodOverride(type.GetMethod(name, BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public),
                                final);
  }

  public CodeGenerator DefineMethodOverride(Type type, string name, bool final, params Type[] paramTypes)
  {
    return DefineMethodOverride(type.GetMethod(name, BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,
                                               null, paramTypes, null), final);
  }

  public CodeGenerator DefineMethodOverride(MethodInfo baseMethod)
  {
    return DefineMethodOverride(baseMethod, IsSealed);
  }

  public CodeGenerator DefineMethodOverride(MethodInfo baseMethod, bool final)
  {
    // use the base method flags minus Abstract and NewSlot, and plus HideBySig
    MethodAttributes attrs = baseMethod.Attributes & ~(MethodAttributes.Abstract|MethodAttributes.NewSlot) |
                             MethodAttributes.HideBySig;
    if(final) attrs |= MethodAttributes.Final; // add Final if requested

    ParameterInfo[] parameters = baseMethod.GetParameters();
    MethodBuilder mb = TypeBuilder.DefineMethod(baseMethod.Name, attrs, baseMethod.ReturnType, GetTypes(parameters));
    // define all the 
    for(int i=0, offset=mb.IsStatic ? 0 : 1; i<parameters.Length; i++)
    {
      mb.DefineParameter(i+offset, parameters[i].Attributes, parameters[i].Name);
    }

    // TODO: figure out how to use this properly
    //TypeBuilder.DefineMethodOverride(mb, baseMethod);
    return new CodeGenerator(this, mb);
  }

  public FieldSlot DefineStaticField(string name, Type type)
  {
    return DefineStaticField(FieldAttributes.Public, name, type);
  }

  public FieldSlot DefineStaticField(FieldAttributes attrs, string name, Type type)
  {
    return DefineField(attrs|FieldAttributes.Static, name, type);
  }

  public CodeGenerator DefineStaticMethod(string name, Type retType, params Type[] paramTypes)
  {
    return DefineMethod(MethodAttributes.Public|MethodAttributes.Static, name, false, retType, paramTypes);
  }

  public CodeGenerator DefineStaticMethod(MethodAttributes attrs, string name, Type retType, params Type[] paramTypes)
  {
    return DefineMethod(attrs|MethodAttributes.Static, name, false, retType, paramTypes);
  }

  public Type FinishType()
  {
    if(finishedType == null)
    {
      if(initializer != null) // if we have an intializer, finish it.
      {
        initializer.EmitReturn();
        initializer.Finish();
      }
      finishedType = TypeBuilder.CreateType();
    }

    return finishedType;
  }
  
  public CodeGenerator GetInitializer()
  {
    if(initializer == null)
    {
      initializer = new CodeGenerator(this, TypeBuilder.DefineTypeInitializer());
    }
    return initializer;
  }

  /// <summary>Marks this type as compiler-generated and not meant to be visible in the debugger.</summary>
  public void MarkAsNonUserCode()
  {
    SetCustomAttribute(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute));
    SetCustomAttribute(typeof(System.Diagnostics.DebuggerNonUserCodeAttribute));
    SetCustomAttribute(typeof(System.Diagnostics.DebuggerStepThroughAttribute));
  }

  /// <summary>Adds a custom attribute to the type being generated.</summary>
  public void SetCustomAttribute(Type attributeType)
  {
    SetCustomAttribute(CG.GetCustomAttributeBuilder(attributeType));
  }

  /// <summary>Adds a custom attribute to the type being generated.</summary>
  public void SetCustomAttribute(ConstructorInfo attributeConstructor, params object[] constructorArgs)
  {
    SetCustomAttribute(new CustomAttributeBuilder(attributeConstructor, constructorArgs));
  }

  /// <summary>Adds a custom attribute to the type being generated.</summary>
  public void SetCustomAttribute(CustomAttributeBuilder attributeBuilder)
  {
    CG.SetCustomAttribute(TypeBuilder, attributeBuilder);
  }

  public readonly AssemblyGenerator Assembly;
  public readonly TypeBuilder TypeBuilder;

  static Type[] GetTypes(ParameterInfo[] parameters)
  {
    Type[] paramTypes = new Type[parameters.Length];
    for(int i=0; i<parameters.Length; i++)
    {
      paramTypes[i] = parameters[i].ParameterType;
    }
    return paramTypes;
  }

  CodeGenerator initializer;
  Type finishedType;
}

} // namespace Scripting.Emit