using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Scripting.Emit
{

public sealed class TypeGenerator : ITypeInfo
{
  public TypeGenerator(AssemblyGenerator assembly, TypeBuilder builder, ITypeInfo baseType, ITypeInfo declaringType)
  {
    this.Assembly      = assembly;
    this.TypeBuilder   = builder;
    this.baseType      = baseType == null ? TypeWrapper.Object : baseType;
    this.declaringType = declaringType;
  }

  [Flags]
  public enum PropertyOverride
  {
    Read=1, Write=2, Either=Read|Write
  }

  public bool IsSealed
  {
    get { return (TypeBuilder.Attributes&TypeAttributes.Sealed) != 0; }
  }

  /// <summary>Creates a public constructor with the given parameter types, and emits code to call the base constructor
  /// with the same parameter types.
  /// </summary>
  public CodeGenerator DefineChainedConstructor(params ITypeInfo[] paramTypes)
  {
    return DefineChainedConstructor(baseType.GetConstructor(InstanceSearch, paramTypes));
  }

  /// <summary>Creates a public constructor with the same parameters as the given constructor, and emits code to call
  /// the base constructor with those parameters.
  /// </summary>
  public CodeGenerator DefineChainedConstructor(IConstructorInfo parent)
  {
    return DefineChainedConstructor(MethodAttributes.Public, parent);
  }

  /// <summary>Creates a constructor with the same parameters as the given constructor, and emits code to call
  /// the base constructor with those parameters.
  /// </summary>
  public CodeGenerator DefineChainedConstructor(MethodAttributes attributes, IConstructorInfo parent)
  {
    if(parent.Method.IsPrivate) throw new ArgumentException("Private constructors cannot be called.");

    IParameterInfo[] parameters = parent.GetParameters();
    ITypeInfo[] paramTypes = ReflectionWrapperHelper.GetTypes(parameters);

    ConstructorBuilder cb = TypeBuilder.DefineConstructor(attributes, CallingConventions.Standard,
                                                          ReflectionWrapperHelper.Unwrap(paramTypes));

    for(int i=0; i<parameters.Length; i++) // define the parameters
    {
      ParameterBuilder pb = cb.DefineParameter(i+1, parameters[i].Attributes, parameters[i].Name);
      // add the ParamArray attribute to the parameter if the parent class has it
      if(parameters[i].IsParamArray)
      {
        pb.SetCustomAttribute(CG.GetCustomAttributeBuilder(typeof(ParamArrayAttribute)));
      }
    }

    CodeGenerator cg = CreateCodeGenerator(new ConstructorBuilderWrapper(this, cb, paramTypes));
    // emit the code to call the parent constructor
    cg.EmitThis();
    for(int i=0; i<parameters.Length; i++) cg.EmitArgGet(i);
    cg.EmitCall(parent);

    // add the constructor to the list of defined constructors
    if(constructors == null) constructors = new List<IConstructorInfo>();
    constructors.Add((IConstructorInfo)cg.Method);

    // return the code generator so the user can add other code
    return cg;
  }

  /// <summary>Defines a new public constructor that takes the given parameter types.</summary>
  public CodeGenerator DefineConstructor(params ITypeInfo[] types)
  {
    return DefineConstructor(MethodAttributes.Public, types);
  }

  /// <summary>Defines a new constructor that takes the given parameter types.</summary>
  public CodeGenerator DefineConstructor(MethodAttributes attributes, params ITypeInfo[] types)
  {
    ConstructorBuilder builder = TypeBuilder.DefineConstructor(attributes, CallingConventions.Standard,
                                                               ReflectionWrapperHelper.Unwrap(types));
    CodeGenerator cg = CreateCodeGenerator(new ConstructorBuilderWrapper(this, builder, types));

    // add the constructor to the list of defined constructors
    if(constructors == null) constructors = new List<IConstructorInfo>();
    constructors.Add((IConstructorInfo)cg.Method);

    return cg;
  }

  /// <summary>Defines a new public default constructor.</summary>
  public CodeGenerator DefineDefaultConstructor()
  {
    return DefineDefaultConstructor(MethodAttributes.Public);
  }

  /// <summary>Defines a new default constructor.</summary>
  public CodeGenerator DefineDefaultConstructor(MethodAttributes attributes)
  {
    return DefineChainedConstructor(TypeWrapper.EmptyTypes);
  }

  public FieldSlot DefineField(string name, ITypeInfo type)
  {
    return DefineField(FieldAttributes.Public, name, type);
  }

  public FieldSlot DefineField(FieldAttributes attrs, string name, ITypeInfo type)
  {
    if(string.IsNullOrEmpty(name)) throw new ArgumentException("Name must not be empty.");
    if(type == null) throw new ArgumentNullException();
    FieldBuilder fieldBuilder = TypeBuilder.DefineField(name, type.DotNetType, attrs);
    if(fields == null) fields = new List<IFieldInfo>();
    IFieldInfo field = new FieldInfoWrapper(this, type, fieldBuilder);
    fields.Add(field);
    return new FieldSlot(fieldBuilder.IsStatic ? null : new ThisSlot(this), field);
  }

  public CodeGenerator DefineMethod(string name, ITypeInfo retType, params ITypeInfo[] paramTypes)
  {
    return DefineMethod(MethodAttributes.Public, name, IsSealed, retType, paramTypes);
  }

  public CodeGenerator DefineMethod(string name, bool final, ITypeInfo retType, params ITypeInfo[] paramTypes)
  {
    return DefineMethod(MethodAttributes.Public, name, final, retType, paramTypes);
  }

  public CodeGenerator DefineMethod(MethodAttributes attrs, string name, ITypeInfo retType, params ITypeInfo[] paramTypes)
  {
    return DefineMethod(attrs, name, IsSealed, retType, paramTypes);
  }

  public CodeGenerator DefineMethod(MethodAttributes attrs, string name, bool final,
                                    ITypeInfo returnType, params ITypeInfo[] paramTypes)
  {
    if((attrs&MethodAttributes.Static) != 0) attrs &= ~MethodAttributes.Final; // static methods can't be marked final
    else if(final) attrs |= MethodAttributes.Final;
    MethodBuilder builder = TypeBuilder.DefineMethod(name, attrs, returnType.DotNetType,
                                                     ReflectionWrapperHelper.Unwrap(paramTypes));
    CodeGenerator cg = CreateCodeGenerator(new MethodBuilderWrapper(this, returnType, builder, paramTypes));

    // add the method to the list of defined methods
    if(methods == null) methods = new List<IMethodInfo>();
    methods.Add((IMethodInfo)cg.Method);

    return cg;
  }

  public CodeGenerator DefineMethodOverride(string name)
  {
    return DefineMethodOverride(baseType.GetMethod(name, InstanceSearch), IsSealed);
  }

  public CodeGenerator DefineMethodOverride(string name, params ITypeInfo[] paramTypes)
  {
    return DefineMethodOverride(baseType.GetMethod(name, InstanceSearch, paramTypes), IsSealed);
  }

  public CodeGenerator DefineMethodOverride(string name, bool final)
  {
    return DefineMethodOverride(baseType.GetMethod(name, InstanceSearch), final);
  }

  public CodeGenerator DefineMethodOverride(string name, bool final, params ITypeInfo[] paramTypes)
  {
    return DefineMethodOverride(baseType.GetMethod(name, InstanceSearch, paramTypes), final);
  }

  public CodeGenerator DefineMethodOverride(MethodInfo baseMethod)
  {
    return DefineMethodOverride(baseMethod, IsSealed);
  }

  public CodeGenerator DefineMethodOverride(MethodInfo baseMethod, bool final)
  {
    return DefineMethodOverride(new MethodInfoWrapper(baseMethod), final);
  }

  public CodeGenerator DefineMethodOverride(IMethodInfo baseMethod, bool final)
  {
    if(baseMethod.Method.IsPrivate) throw new ArgumentException("Private methods cannot be overriden.");
    
    // use the base method flags minus Abstract and NewSlot, and plus HideBySig
    MethodAttributes attrs = baseMethod.Attributes & ~(MethodAttributes.Abstract|MethodAttributes.NewSlot) |
                             MethodAttributes.HideBySig;
    if(final) attrs |= MethodAttributes.Final; // add Final if requested

    IParameterInfo[] parameters = baseMethod.GetParameters();
    MethodBuilder mb = TypeBuilder.DefineMethod(baseMethod.Name, attrs, baseMethod.ReturnType.DotNetType,
                                                ReflectionWrapperHelper.GetDotNetTypes(parameters));

    // define all the parameters to be the same as the base type's
    for(int i=0, offset=mb.IsStatic ? 0 : 1; i<parameters.Length; i++)
    {
      ParameterBuilder pb = mb.DefineParameter(i+offset, parameters[i].Attributes, parameters[i].Name);
      // add the ParamArray attribute to the parameter if the parent class has it
      if(parameters[i].IsParamArray)
      {
        pb.SetCustomAttribute(CG.GetCustomAttributeBuilder(typeof(ParamArrayAttribute)));
      }
    }

    // TODO: figure out how to use this properly
    //TypeBuilder.DefineMethodOverride(mb, baseMethod);
    CodeGenerator cg = CreateCodeGenerator(new MethodBuilderWrapper(this, baseMethod.ReturnType, mb, parameters));

    // add the method to the list of defined methods
    if(methods == null) methods = new List<IMethodInfo>();
    methods.Add((IMethodInfo)cg.Method);

    return cg;
  }

  /// <summary>Defines a new type nested within this type.</summary>
  public TypeGenerator DefineNestedType(TypeAttributes attributes, string name)
  {
    return DefineNestedType(attributes, name, (ITypeInfo)null);
  }

  /// <summary>Defines a new type nested within this type.</summary>
  public TypeGenerator DefineNestedType(TypeAttributes attributes, string name, Type baseType)
  {
    return DefineNestedType(attributes, name, baseType == null ? null : TypeWrapper.Get(baseType));
  }

  /// <summary>Defines a new type nested within this type.</summary>
  public TypeGenerator DefineNestedType(TypeAttributes attributes, string name, ITypeInfo baseType)
  {
    TypeBuilder builder = TypeBuilder.DefineNestedType(name, attributes, baseType == null ? null : baseType.DotNetType);
    TypeGenerator ret = new TypeGenerator(Assembly, builder, baseType, this);
    if(nestedTypes == null) nestedTypes = new List<TypeGenerator>();
    nestedTypes.Add(ret);
    return ret;
  }

  /// <summary>Defines a public read-only property with the given name and value type.</summary>
  public CodeGenerator DefineProperty(string name, ITypeInfo valueType)
  {
    return DefineProperty(MethodAttributes.Public, name, valueType, TypeWrapper.EmptyTypes);
  }

  /// <summary>Defines a read-only property with the given name and value type.</summary>
  public CodeGenerator DefineProperty(MethodAttributes attributes, string name, ITypeInfo valueType)
  {
    return DefineProperty(attributes, name, valueType, TypeWrapper.EmptyTypes);
  }

  /// <summary>Defines a public read-only indexer with the given name, value type, and parameter types.</summary>
  public CodeGenerator DefineProperty(string name, ITypeInfo valueType, params ITypeInfo[] paramTypes)
  {
    return DefineProperty(MethodAttributes.Public, name, valueType, paramTypes);
  }

  /// <summary>Defines a read-only property with the given name, value type, and parameter types.</summary>
  public CodeGenerator DefineProperty(MethodAttributes attributes, string name, ITypeInfo valueType,
                                      params ITypeInfo[] paramTypes)
  {
    PropertyBuilder pb = TypeBuilder.DefineProperty(name, PropertyAttributes.None, valueType.DotNetType,
                                                    ReflectionWrapperHelper.Unwrap(paramTypes));
    CodeGenerator cg = DefineMethod(attributes, "get_"+name, valueType, paramTypes);
    pb.SetGetMethod((MethodBuilder)cg.Method.Method);

    if(properties == null) properties = new List<IPropertyInfo>();
    properties.Add(new PropertyInfoWrapper(pb));

    return cg;
  }

  /// <summary>Defines a public read/write property with the given value type.</summary>
  public void DefineProperty(string name, ITypeInfo valueType, out CodeGenerator get, out CodeGenerator set)
  {
    DefineProperty(MethodAttributes.Public, name, valueType, TypeWrapper.EmptyTypes, out get, out set);
  }

  /// <summary>Defines a read/write property with the given value type.</summary>
  public void DefineProperty(MethodAttributes attributes, string name, ITypeInfo valueType,
                             out CodeGenerator get, out CodeGenerator set)
  {
    DefineProperty(attributes, name, valueType, TypeWrapper.EmptyTypes, out get, out set);
  }

  /// <summary>Defines a public read/write indexer with the given value type and parameter types.</summary>
  public void DefineProperty(string name, ITypeInfo valueType, ITypeInfo[] paramTypes,
                             out CodeGenerator get, out CodeGenerator set)
  {
    DefineProperty(MethodAttributes.Public, name, valueType, paramTypes, out get, out set);
  }

  /// <summary>Defines a read/write property with the given value type and parameter types.</summary>
  public void DefineProperty(MethodAttributes attributes, string name, ITypeInfo valueType, ITypeInfo[] paramTypes,
                             out CodeGenerator get, out CodeGenerator set)
  {
    Type[] dotNetParams = ReflectionWrapperHelper.Unwrap(paramTypes);
    PropertyBuilder pb = TypeBuilder.DefineProperty(name, PropertyAttributes.None, valueType.DotNetType, dotNetParams);
    get = DefineMethod(attributes, "get_"+name, valueType, paramTypes);
    set = DefineMethod(attributes, "set_"+name, null, paramTypes);
    pb.SetGetMethod((MethodBuilder)get.Method.Method);
    pb.SetSetMethod((MethodBuilder)set.Method.Method);

    if(properties == null) properties = new List<IPropertyInfo>();
    properties.Add(new PropertyInfoWrapper(pb));
  }

  /// <summary>Overrides a property with the given name. If the property is read-only, the getter will be returned.
  /// If the property is write-only, the setter will be returned. If the property is read/write, an exception will be
  /// thrown.
  /// </summary>
  public CodeGenerator DefinePropertyOverride(string name)
  {
    return DefinePropertyOverride(BaseType.GetProperty(name), PropertyOverride.Either, IsSealed);
  }

  /// <summary>Overrides a property with the given name. Only one of the getter or the setter can be overridden with
  /// this method.
  /// </summary>
  public CodeGenerator DefinePropertyOverride(string name, PropertyOverride po)
  {
    return DefinePropertyOverride(BaseType.GetProperty(name), po, IsSealed);
  }

  /// <summary>Overrides a property with the given name. If the property is read-only, the getter will be returned.
  /// If the property is write-only, the setter will be returned. If the property is read/write, an exception will be
  /// thrown. The property will be sealed if 'final' is true.
  /// </summary>
  public CodeGenerator DefinePropertyOverride(string name, bool final)
  {
    return DefinePropertyOverride(BaseType.GetProperty(name), PropertyOverride.Either, final);
  }

  /// <summary>Overrides a property with the given name. Only one of the getter or the setter can be overridden with
  /// this method. The property will be sealed if 'final' is true.
  /// </summary>
  public CodeGenerator DefinePropertyOverride(string name, PropertyOverride po, bool final)
  {
    return DefinePropertyOverride(BaseType.GetProperty(name), po, final);
  }

  /// <summary>Overrides the given property. If the property is read-only, the getter will be returned.
  /// If the property is write-only, the setter will be returned. If the property is read/write, an exception will be
  /// thrown.
  /// </summary>
  public CodeGenerator DefinePropertyOverride(PropertyInfo baseProp)
  {
    return DefinePropertyOverride(baseProp, PropertyOverride.Either, IsSealed);
  }

  /// <summary>Overrides the given property. Only one of the getter or the setter can be overridden with this method.</summary>
  public CodeGenerator DefinePropertyOverride(PropertyInfo baseProp, PropertyOverride po)
  {
    return DefinePropertyOverride(baseProp, po, IsSealed);
  }

  /// <summary>Overrides the given property. Only one of the getter or the setter can be overridden with this method.
  /// The property will be sealed if 'final' is true.
  /// </summary>
  public CodeGenerator DefinePropertyOverride(PropertyInfo baseProp, PropertyOverride po, bool final)
  {
    return DefinePropertyOverride(new PropertyInfoWrapper(baseProp), po, final);
  }

  /// <summary>Overrides the given property. Only one of the getter or the setter can be overridden with this method.
  /// The property will be sealed if 'final' is true.
  /// </summary>
  public CodeGenerator DefinePropertyOverride(IPropertyInfo baseProp, PropertyOverride po, bool final)
  {
    if(po == PropertyOverride.Either)
    {
      if(baseProp.CanRead && baseProp.CanWrite)
      {
        throw new ArgumentException("This property has both a getter and a setter. Specify which one to override.");
      }
    }
    else if(po == PropertyOverride.Read && !baseProp.CanRead)
    {
      throw new ArgumentException("This property has no getter.");
    }
    else if(po == PropertyOverride.Write && !baseProp.CanWrite)
    {
      throw new ArgumentException("This property has no setter.");
    }

    IMethodInfo methodToOverride = po == PropertyOverride.Read  ? baseProp.Getter :
                                   po == PropertyOverride.Write ? baseProp.Setter :
                                   baseProp.CanRead ? baseProp.Getter : baseProp.Setter;

    return DefineMethodOverride(methodToOverride, final);
  }

  /// <summary>Overrides the property with the given name.</summary>
  public void DefinePropertyOverride(string name, out CodeGenerator get, out CodeGenerator set)
  {
    DefinePropertyOverride(BaseType.GetProperty(name), IsSealed, out get, out set);
  }

  /// <summary>Overrides the property with the given name. The property will be sealed if 'final' is true.</summary>
  public void DefinePropertyOverride(string name, bool final, out CodeGenerator get, out CodeGenerator set)
  {
    DefinePropertyOverride(BaseType.GetProperty(name), final, out get, out set);
  }

  /// <summary>Overrides the given property.</summary>
  public void DefinePropertyOverride(PropertyInfo baseProp, out CodeGenerator get, out CodeGenerator set)
  {
    DefinePropertyOverride(baseProp, IsSealed, out get, out set);
  }

  /// <summary>Overrides the given property. The property will be sealed if 'final' is true.</summary>
  public void DefinePropertyOverride(PropertyInfo baseProp, bool final, out CodeGenerator get, out CodeGenerator set)
  {
    DefinePropertyOverride(new PropertyInfoWrapper(baseProp), final, out get, out set);
  }

  /// <summary>Overrides the given property. The property will be sealed if 'final' is true.</summary>
  public void DefinePropertyOverride(IPropertyInfo baseProp, bool final, out CodeGenerator get, out CodeGenerator set)
  {
    get = baseProp.CanRead  ? DefineMethodOverride(baseProp.Getter, final) : null;
    set = baseProp.CanWrite ? DefineMethodOverride(baseProp.Setter, final) : null;
  }

  /// <summary>Defines a new public static field.</summary>
  public FieldSlot DefineStaticField(string name, ITypeInfo type)
  {
    return DefineStaticField(FieldAttributes.Public, name, type);
  }

  /// <summary>Defines a new static field.</summary>
  public FieldSlot DefineStaticField(FieldAttributes attrs, string name, ITypeInfo type)
  {
    return DefineField(attrs|FieldAttributes.Static, name, type);
  }

  /// <summary>Defines a new public static method.</summary>
  public CodeGenerator DefineStaticMethod(string name, ITypeInfo retType, params ITypeInfo[] paramTypes)
  {
    return DefineMethod(MethodAttributes.Public|MethodAttributes.Static, name, false, retType, paramTypes);
  }

  /// <summary>Defines a new static method.</summary>
  public CodeGenerator DefineStaticMethod(MethodAttributes attrs, string name, ITypeInfo retType,
                                          params ITypeInfo[] paramTypes)
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

      if(nestedTypes != null) // nested types must be finished after their enclosing type
      {
        foreach(TypeGenerator tg in nestedTypes)
        {
          tg.FinishType();
        }
      }
    }

    return finishedType;
  }
  
  public CodeGenerator GetInitializer()
  {
    if(initializer == null)
    {
      ConstructorBuilder builder = TypeBuilder.DefineTypeInitializer();
      initializer = CreateCodeGenerator(new ConstructorBuilderWrapper(this, builder, TypeWrapper.EmptyTypes));
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

  CodeGenerator CreateCodeGenerator(ConstructorBuilderWrapper cb)
  {
    return CompilerState.Current.Language.CreateCodeGenerator(this, cb, cb.Builder.GetILGenerator());
  }

  CodeGenerator CreateCodeGenerator(MethodBuilderWrapper mb)
  {
    return CompilerState.Current.Language.CreateCodeGenerator(this, mb, mb.Builder.GetILGenerator());
  }

  const BindingFlags InstanceSearch = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
  const BindingFlags SearchAll = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;

  readonly ITypeInfo baseType, declaringType;

  List<TypeGenerator> nestedTypes;
  List<IFieldInfo> fields;
  List<IMethodInfo> methods;
  List<IConstructorInfo> constructors;
  List<IPropertyInfo> properties;
  CodeGenerator initializer;
  Type finishedType;

  #region ITypeInfo Members
  public TypeAttributes Attributes
  {
    get { return TypeBuilder.Attributes; }
  }

  public ITypeInfo BaseType
  {
    get { return baseType; }
  }

  public ITypeInfo DeclaringType
  {
    get { return declaringType; }
  }

  public Type DotNetType
  {
    get { return TypeBuilder; }
  }

  public ITypeInfo ElementType
  {
    get { throw new InvalidOperationException("This is not an array, pointer, or reference type."); }
  }
  
  public string FullName
  {
    get { return TypeBuilder.FullName; }
  }

  public bool IsValueType
  {
    get { return TypeBuilder.IsValueType; }
  }

  public string Name
  {
    get { return TypeBuilder.Name; }
  }

  public TypeCode TypeCode
  {
    get { return TypeCode.Object; }
  }

  public IConstructorInfo GetConstructor(BindingFlags flags, params ITypeInfo[] parameterTypes)
  {
    if(constructors != null)
    {
      foreach(IConstructorInfo cons in constructors)
      {
        if(MethodAttributesMatch(flags, cons) && ParametersMatch(parameterTypes, cons.GetParameters()))
        {
          return cons;
        }
      }
    }

    return null;
  }

  public IFieldInfo GetField(string name)
  {
    if(fields != null)
    {
      foreach(IFieldInfo field in fields)
      {
        if(string.Equals(name, field.Name, StringComparison.Ordinal))
        {
          return field;
        }
      }
    }

    return baseType == null ? null : baseType.GetField(name);
  }

  public ITypeInfo[] GetInterfaces()
  {
    return baseType == null ? new ITypeInfo[0] : baseType.GetInterfaces();
  }

  public IMethodInfo GetMethod(string name, BindingFlags flags, params ITypeInfo[] parameterTypes)
  {
    if(methods != null)
    {
      foreach(IMethodInfo method in methods)
      {
        if(string.Equals(name, method.Name, StringComparison.Ordinal) &&
           MethodAttributesMatch(flags, method) && ParametersMatch(parameterTypes, method.GetParameters()))
        {
          return method;
        }
      }
    }

    return baseType == null || (flags & BindingFlags.DeclaredOnly) != 0
      ? null : baseType.GetMethod(name, flags, parameterTypes);
  }

  public IMethodInfo[] GetMethods(BindingFlags flags)
  {
    if(methods == null)
    {
      return (flags & BindingFlags.DeclaredOnly) != 0 || baseType == null ?
        new IMethodInfo[0] : baseType.GetMethods(flags);
    }

    List<IMethodInfo> matches = new List<IMethodInfo>(methods.Count);
    foreach(IMethodInfo method in methods)
    {
      if(MethodAttributesMatch(flags, method)) matches.Add(method);
    }

    if(baseType != null && (flags & BindingFlags.DeclaredOnly) == 0)
    {
      int numToCheck = matches.Count;
      foreach(IMethodInfo inheritedMethod in baseType.GetMethods(flags))
      {
        bool shadowed = false;
        for(int i=0; i<numToCheck; i++)
        {
          if(SignaturesMatch(matches[i], inheritedMethod))
          {
            shadowed = true;
            break;
          }
        }
        if(!shadowed) matches.Add(inheritedMethod);
      }
    }

    return matches.ToArray();
  }

  public ITypeInfo GetNestedType(string name)
  {
    if(nestedTypes != null)
    {
      foreach(ITypeInfo type in nestedTypes)
      {
        if(string.Equals(type.Name, name, StringComparison.Ordinal))
        {
          return type;
        }
      }
    }    

    return null;
  }

  public IPropertyInfo GetProperty(string name)
  {
    if(properties != null)
    {
      foreach(IPropertyInfo property in properties)
      {
        if(string.Equals(name, property.Name, StringComparison.Ordinal))
        {
          return property;
        }
      }
    }

    return baseType == null ? null : baseType.GetProperty(name);
  }

  public bool IsAssignableFrom(ITypeInfo type)
  {
    return TypeBuilder.IsAssignableFrom(type.DotNetType);
  }

  public bool IsSubclassOf(ITypeInfo type)
  {
    return TypeBuilder.IsSubclassOf(type.DotNetType);
  }

  public ITypeInfo MakeArrayType()
  {
    return TypeWrapper.GetArray(this, TypeBuilder.MakeArrayType());
  }

  static bool MethodAttributesMatch(BindingFlags flags, IMethodBase method)
  {
    // if it's searching public and visible publically or searching nonpublic and visible nonpublically, then it matches
    if(((flags&BindingFlags.Public) == 0 || (method.Attributes&MethodAttributes.Public) == 0) &&
       ((flags&BindingFlags.NonPublic) == 0 ||
       (method.Attributes&(MethodAttributes.Assembly|MethodAttributes.Family|MethodAttributes.Private)) == 0))
    {
      return false;
    }
    
    return (flags&BindingFlags.Instance) != 0 && !method.IsStatic ||
           (flags&BindingFlags.Static)   != 0 &&  method.IsStatic;
  }
  
  static bool ParametersMatch(ITypeInfo[] search, IParameterInfo[] method)
  {
    if(search.Length != method.Length) return false;
    for(int i=0; i<search.Length; i++)
    {
      if(search[i] != method[i].ParameterType)
      {
        return false;
      }
    }
    return true;
  }

  static bool ParametersMatch(IParameterInfo[] search, IParameterInfo[] method)
  {
    if(search.Length != method.Length) return false;
    for(int i=0; i<search.Length; i++)
    {
      if(search[i].ParameterType != method[i].ParameterType)
      {
        return false;
      }
    }
    return true;
  }

  static bool SignaturesMatch(IMethodInfo a, IMethodInfo b)
  {
    return string.Equals(a.Name, b.Name, StringComparison.Ordinal) &&
           ParametersMatch(a.GetParameters(), b.GetParameters());
  }
  #endregion
}

} // namespace Scripting.Emit