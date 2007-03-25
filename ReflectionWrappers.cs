using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Scripting.Emit
{

// these exist because you can't perform inspection of types that are not yet complete. so by using these inspection
// interfaces, i can search for methods, etc, on incomplete types by implementing these interfaces on TypeGenerator,
// CodeGenerator, etc.

#region Interfaces
public interface IConstructorInfo : IMethodBase
{
  new ConstructorInfo Method { get; }
}

public interface IFieldInfo : IMemberInfo
{
  FieldAttributes Attributes { get; }
  FieldInfo Field { get; }
  ITypeInfo FieldType { get; }
  bool IsStatic { get; }
}

public interface IMemberInfo
{
  ITypeInfo DeclaringType { get; }
  string Name { get; }
}

public interface IMethodBase : IMemberInfo
{
  MethodAttributes Attributes { get; }
  bool IsStatic { get; }
  MethodBase Method { get; }
  IParameterInfo[] GetParameters();
}

public interface IMethodInfo : IMethodBase
{
  new MethodInfo Method { get; }
  ITypeInfo ReturnType { get; }
}

public interface IParameterInfo
{
  ParameterAttributes Attributes { get; }
  bool IsParamArray { get; }
  string Name { get; }
  ITypeInfo ParameterType { get; }
}

public interface IPropertyInfo : IMemberInfo
{
  bool CanRead { get; }
  bool CanWrite { get; }
  IMethodInfo Getter { get; }
  IMethodInfo Setter { get; }
  PropertyInfo Property { get; }
  ITypeInfo PropertyType { get; }
}

public interface ITypeInfo : IMemberInfo
{
  TypeAttributes Attributes { get; }
  ITypeInfo BaseType { get; }
  Type DotNetType { get; }
  ITypeInfo ElementType { get; }
  bool IsValueType { get; }
  string FullName { get; }
  TypeCode TypeCode { get; }

  IConstructorInfo GetConstructor(BindingFlags flags, params ITypeInfo[] parameterTypes);
  IFieldInfo GetField(string name);
  ITypeInfo[] GetInterfaces();
  IMethodInfo GetMethod(string name, BindingFlags flags, params ITypeInfo[] parameterTypes);
  IMethodInfo[] GetMethods(BindingFlags flags);
  ITypeInfo GetNestedType(string name);
  IPropertyInfo GetProperty(string name);

  bool IsAssignableFrom(ITypeInfo type);
  bool IsSubclassOf(ITypeInfo type);

  ITypeInfo MakeArrayType();
}
#endregion

#region ConstructorInfoWrapper
public class ConstructorInfoWrapper : IConstructorInfo
{
  public ConstructorInfoWrapper(ConstructorInfo constructor)
  {
    if(constructor == null) throw new ArgumentNullException();
    cons = constructor;
  }

  public MethodAttributes Attributes
  {
    get { return cons.Attributes; }
  }

  public virtual ITypeInfo DeclaringType
  {
    get { return TypeWrapper.Get(cons.DeclaringType); }
  }

  public bool IsStatic
  {
    get { return cons.IsStatic; }
  }

  public ConstructorInfo Method
  {
    get { return cons; }
  }

  MethodBase IMethodBase.Method
  {
    get { return cons; }
  }

  public string Name
  {
    get { return cons.Name; }
  }

  public virtual IParameterInfo[] GetParameters()
  {
    return ReflectionWrapperHelper.WrapParameters(cons.GetParameters());
  }

  protected readonly ConstructorInfo cons;
}
#endregion

#region ConstructorBuilderWrapper
public sealed class ConstructorBuilderWrapper : ConstructorInfoWrapper
{
  public ConstructorBuilderWrapper(ITypeInfo declaringType, ConstructorBuilder method, ITypeInfo[] parameterTypes)
    : this(declaringType, method, ReflectionWrapperHelper.WrapParameters(parameterTypes)) { }

  public ConstructorBuilderWrapper(ITypeInfo declaringType, ConstructorBuilder method, IParameterInfo[] parameters)
    : base(method)
  {
    if(declaringType == null || parameters == null) throw new ArgumentNullException();
    this.declaringType = declaringType;
    this.parameters    = parameters;
  }
  
  public ConstructorBuilder Builder
  {
    get { return (ConstructorBuilder)cons; }
  }

  public override ITypeInfo DeclaringType
  {
    get { return declaringType; }
  }

  public void DefineParameter(int index, ParameterAttributes attributes, string name)
  {
    ((ParameterTypeWrapper)parameters[index]).Define(attributes, name);
  }

  public override IParameterInfo[] GetParameters()
  {
    return (IParameterInfo[])parameters.Clone();
  }
  
  readonly IParameterInfo[] parameters;
  readonly ITypeInfo declaringType;
}
#endregion

#region FieldInfoWrapper
public sealed class FieldInfoWrapper : IFieldInfo
{
  public FieldInfoWrapper(ITypeInfo declaringType, ITypeInfo fieldType, FieldInfo field)
  {
    if(declaringType == null || fieldType == null || field == null) throw new ArgumentNullException();
    this.declaringType = declaringType;
    this.fieldType     = fieldType;
    this.field         = field;
  }

  public FieldAttributes Attributes
  {
    get { return field.Attributes; }
  }

  public ITypeInfo DeclaringType
  {
    get { return declaringType; }
  }

  public FieldInfo Field
  {
    get { return field; }
  }

  public ITypeInfo FieldType
  {
    get { return fieldType; }
  }

  public bool IsStatic
  {
    get { return field.IsStatic; }
  }

  public string Name
  {
    get { return field.Name; }
  }

  readonly FieldInfo field;
  readonly ITypeInfo declaringType, fieldType;
}
#endregion

#region MethodBuilderWrapper
public sealed class MethodBuilderWrapper : MethodInfoWrapper
{
  public MethodBuilderWrapper(ITypeInfo declaringType, ITypeInfo returnType, MethodBuilder method,
                              ITypeInfo[] parameterTypes)
    : this(declaringType, returnType, method, ReflectionWrapperHelper.WrapParameters(parameterTypes)) { }

  public MethodBuilderWrapper(ITypeInfo declaringType, ITypeInfo returnType, MethodBuilder method,
                              IParameterInfo[] parameters) : base(method)
  {
    if(declaringType == null || returnType == null || parameters == null) throw new ArgumentNullException();
    this.declaringType = declaringType;
    this.returnType    = returnType;
    this.parameters    = parameters;
  }

  public MethodBuilder Builder
  {
    get { return (MethodBuilder)method; }
  }

  public override ITypeInfo DeclaringType
  {
    get { return declaringType; }
  }

  public override ITypeInfo ReturnType
  {
    get { return returnType; }
  }

  public void DefineParameter(int index, ParameterAttributes attributes, string name)
  {
    ((ParameterTypeWrapper)parameters[index]).Define(attributes, name);
  }

  public override IParameterInfo[] GetParameters()
  {
    return (IParameterInfo[])parameters.Clone();
  }
  
  readonly IParameterInfo[] parameters;
  readonly ITypeInfo declaringType, returnType;
}
#endregion

#region MethodInfoWrapper
public class MethodInfoWrapper : IMethodInfo
{
  public MethodInfoWrapper(MethodInfo method)
  {
    if(method == null) throw new ArgumentNullException();
    this.method = method;
  }

  public MethodAttributes Attributes
  {
    get { return method.Attributes; }
  }

  public virtual ITypeInfo DeclaringType
  {
    get { return TypeWrapper.Get(method.DeclaringType); }
  }

  public bool IsStatic
  {
    get { return method.IsStatic; }
  }

  public MethodInfo Method
  {
    get { return method; }
  }

  public string Name
  {
    get { return method.Name; }
  }

  public virtual ITypeInfo ReturnType
  {
    get { return TypeWrapper.Get(method.ReturnType); }
  }

  MethodBase IMethodBase.Method
  {
    get { return method; }
  }

  public virtual IParameterInfo[] GetParameters()
  {
    return ReflectionWrapperHelper.WrapParameters(method.GetParameters());
  }

  protected readonly MethodInfo method;
}
#endregion

#region ParameterInfoWrapper
public sealed class ParameterInfoWrapper : IParameterInfo
{
  public ParameterInfoWrapper(ParameterInfo param)
  {
    if(param == null) throw new ArgumentNullException();
    this.param = param;
  }

  public ParameterAttributes Attributes
  {
    get { return param.Attributes; }
  }

  public object DefaultValue
  {
    get { return param.DefaultValue; }
  }

  public bool IsParamArray
  {
    get { return param.IsDefined(typeof(ParamArrayAttribute), false); }
  }

  public string Name
  {
    get { return param.Name; }
  }

  public ITypeInfo ParameterType
  {
    get { return TypeWrapper.Get(param.ParameterType); }
  }

  readonly ParameterInfo param;
}
#endregion

#region ParameterTypeWrapper
public sealed class ParameterTypeWrapper : IParameterInfo
{
  public ParameterTypeWrapper(ITypeInfo type)
  {
    if(type == null) throw new ArgumentNullException();
    this.type       = type;
    this.attributes = ParameterAttributes.None;
  }

  public ParameterAttributes Attributes
  {
    get { return attributes; }
  }

  public bool IsParamArray
  {
    get { return false; }
  }

  public string Name
  {
    get { return name; }
  }

  public ITypeInfo ParameterType
  {
    get { return type; }
  }

  internal void Define(ParameterAttributes attributes, string name)
  {
    this.attributes = attributes;
    this.name       = name;
  }

  readonly ITypeInfo type;
  string name;
  ParameterAttributes attributes;
}
#endregion

#region PropertyInfoWrapper
public sealed class PropertyInfoWrapper : IPropertyInfo
{
  public PropertyInfoWrapper(PropertyInfo property)
  {
    if(property == null) throw new ArgumentNullException();
    this.property = property;
  }

  public bool CanRead
  {
    get { return property.CanRead; }
  }

  public bool CanWrite
  {
    get { return property.CanWrite; }
  }

  public ITypeInfo DeclaringType
  {
    get { return TypeWrapper.Get(property.DeclaringType); }
  }

  public IMethodInfo Getter
  {
    get
    {
      MethodInfo method = property.GetGetMethod(true);
      return method == null ? null : new MethodInfoWrapper(method);
    }
  }

  public IMethodInfo Setter
  {
    get
    {
      MethodInfo method = property.GetSetMethod(true);
      return method == null ? null : new MethodInfoWrapper(method);
    }
  }

  public string Name
  {
    get { return property.Name; }
  }

  public PropertyInfo Property
  {
    get { return property; }
  }

  public ITypeInfo PropertyType
  {
    get { return TypeWrapper.Get(property.PropertyType); }
  }

  readonly PropertyInfo property;
}
#endregion

#region TypeWrapper
public class TypeWrapper : ITypeInfo
{
  internal TypeWrapper(Type type)
  {
    if(type == null) throw new ArgumentNullException();
    this.type = type;
  }

  public TypeAttributes Attributes
  {
    get { return type.Attributes; }
  }

  public ITypeInfo BaseType
  {
    get { return type.BaseType == null ? null : TypeWrapper.Get(type.BaseType); }
  }

  public ITypeInfo DeclaringType
  {
    get { return TypeWrapper.Get(type.DeclaringType); }
  }

  public Type DotNetType
  {
    get { return type; }
  }

  public virtual ITypeInfo ElementType
  {
    get { return TypeWrapper.Get(type.GetElementType()); }
  }
  
  public string FullName
  {
    get { return type.FullName; }
  }

  public bool IsValueType
  {
    get { return type.IsValueType; }
  }

  public string Name
  {
    get { return type.Name; }
  }

  public TypeCode TypeCode
  {
    get { return Type.GetTypeCode(type); }
  }

  public IConstructorInfo GetConstructor(BindingFlags flags, params ITypeInfo[] parameterTypes)
  {
    ConstructorInfo cons = type.GetConstructor(flags, null, ReflectionWrapperHelper.Unwrap(parameterTypes), null);
    return cons == null ? null : new ConstructorInfoWrapper(cons);
  }

  public IFieldInfo GetField(string name)
  {
    FieldInfo field = type.GetField(name, SearchAll);
    return field == null ? null : new FieldInfoWrapper(this, TypeWrapper.Get(field.FieldType),  field);
  }

  public ITypeInfo[] GetInterfaces()
  {
    return ReflectionWrapperHelper.Wrap(type.GetInterfaces());
  }

  public IMethodInfo GetMethod(string name, BindingFlags flags)
  {
    MethodInfo method = type.GetMethod(name, flags);
    return method == null ? null : new MethodInfoWrapper(method);
  }

  public IMethodInfo GetMethod(string name, BindingFlags flags, params ITypeInfo[] parameterTypes)
  {
    MethodInfo method = type.GetMethod(name, flags, null, ReflectionWrapperHelper.Unwrap(parameterTypes), null);
    return method == null ? null : new MethodInfoWrapper(method);
  }

  public IMethodInfo[] GetMethods(BindingFlags flags)
  {
    MethodInfo[] methods = type.GetMethods(flags);
    IMethodInfo[] iMethods = new IMethodInfo[methods.Length];
    for(int i=0; i<iMethods.Length; i++) iMethods[i] = new MethodInfoWrapper(methods[i]);
    return iMethods;
  }

  public ITypeInfo GetNestedType(string name)
  {
    Type nestedType = type.GetNestedType(name, BindingFlags.Public|BindingFlags.NonPublic);
    return nestedType == null ? null : new TypeWrapper(nestedType);
  }

  public IPropertyInfo GetProperty(string name)
  {
    PropertyInfo property = type.GetProperty(name, SearchAll);
    return property == null ? null : new PropertyInfoWrapper(property);
  }

  public bool IsAssignableFrom(ITypeInfo type)
  {
    return this.type.IsAssignableFrom(type.DotNetType);
  }

  public bool IsSubclassOf(ITypeInfo type)
  {
    return this.type.IsSubclassOf(type.DotNetType);
  }

  public ITypeInfo MakeArrayType()
  {
    return TypeWrapper.GetArray(this, type.MakeArrayType());
  }

  const BindingFlags SearchAll = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;

  readonly Type type;
  
  /// <summary>Gets an <see cref="ITypeInfo"/> interface for a <see cref="Type"/> object.</summary>
  /// <remarks>This method exists so that <see cref="ITypeInfo"/> objects can be checked for equality with a reference
  /// comparison.
  /// </remarks>
  public static TypeWrapper Get(Type type)
  {
    if(type == null) throw new ArgumentNullException();
    if(type is TypeBuilder) throw new ArgumentException("Use a TypeGenerator instead of wrapping a TypeBuilder.");

    TypeWrapper ret;
    lock(wrappers)
    {
      if(!wrappers.TryGetValue(type, out ret))
      {
        wrappers[type] = ret = new TypeWrapper(type);
      }
    }
    return ret;
  }

  public static ITypeInfo GetArray(ITypeInfo elementType, Type type)
  {
    if(type == null) throw new ArgumentNullException();

    if(elementType.GetType() == typeof(TypeWrapper))
    {
      return Get(type); // don't create an ArrayWrapper for an array of a built-in type
    }

    TypeWrapper ret;
    lock(wrappers)
    {
      if(!wrappers.TryGetValue(type, out ret))
      {
        wrappers[type] = ret = new ArrayTypeWrapper(elementType, type);
      }
    }
    return ret;
  }

  static readonly Dictionary<Type, TypeWrapper> wrappers = new Dictionary<Type, TypeWrapper>();

  public static readonly TypeWrapper Bool    = Get(typeof(bool));
  public static readonly TypeWrapper Byte    = Get(typeof(byte));
  public static readonly TypeWrapper SByte   = Get(typeof(sbyte));
  public static readonly TypeWrapper Short   = Get(typeof(short));
  public static readonly TypeWrapper UShort  = Get(typeof(ushort));
  public static readonly TypeWrapper Char    = Get(typeof(char));
  public static readonly TypeWrapper Int     = Get(typeof(int));
  public static readonly TypeWrapper UInt    = Get(typeof(uint));
  public static readonly TypeWrapper Long    = Get(typeof(long));
  public static readonly TypeWrapper ULong   = Get(typeof(ulong));
  public static readonly TypeWrapper Decimal = Get(typeof(decimal));
  public static readonly TypeWrapper Integer = Get(typeof(Runtime.Integer));
  public static readonly TypeWrapper Single  = Get(typeof(float));
  public static readonly TypeWrapper Double  = Get(typeof(double));
  public static readonly TypeWrapper String  = Get(typeof(string));
  public static readonly TypeWrapper Object  = Get(typeof(object));
  public static readonly TypeWrapper IntPtr  = Get(typeof(IntPtr));
  public static readonly TypeWrapper Void    = Get(typeof(void));
  public static readonly TypeWrapper ICallable    = Get(typeof(Runtime.ICallable));
  public static readonly TypeWrapper TopLevel     = Get(typeof(Runtime.TopLevel));
  public static readonly TypeWrapper ObjectArray  = Get(typeof(object[]));
  public static readonly ITypeInfo[] EmptyTypes = new ITypeInfo[0];
}
#endregion

#region ArrayTypeWrapper
public class ArrayTypeWrapper : TypeWrapper
{
  internal ArrayTypeWrapper(ITypeInfo elementType, Type type) : base(type)
  {
    if(elementType == null) throw new ArgumentNullException();
    this.elementType = elementType;
  }

  public override ITypeInfo ElementType
  {
    get { return elementType; }
  }

  readonly ITypeInfo elementType;
}
#endregion

#region ReflectionWrapperHelper
static class ReflectionWrapperHelper
{
  public static ITypeInfo[] GetTypes(IParameterInfo[] parameters)
  {
    ITypeInfo[] paramTypes = new ITypeInfo[parameters.Length];
    for(int i=0; i<parameters.Length; i++)
    {
      paramTypes[i] = parameters[i].ParameterType;
    }
    return paramTypes;
  }

  public static Type[] GetDotNetTypes(IParameterInfo[] parameters)
  {
    Type[] paramTypes = new Type[parameters.Length];
    for(int i=0; i<parameters.Length; i++)
    {
      paramTypes[i] = parameters[i].ParameterType.DotNetType;
    }
    return paramTypes;
  }

  public static Type[] Unwrap(ITypeInfo[] iTypes)
  {
    Type[] types = new Type[iTypes.Length];
    for(int i=0; i<types.Length; i++)
    {
      types[i] = iTypes[i].DotNetType;
    }
    return types;
  }

  public static ITypeInfo[] Wrap(Type[] types)
  {
    ITypeInfo[] iTypes = new ITypeInfo[types.Length];
    for(int i=0; i<iTypes.Length; i++)
    {
      iTypes[i] = TypeWrapper.Get(types[i]);
    }
    return iTypes;
  }

  public static IParameterInfo[] WrapParameters(ITypeInfo[] parameterTypes)
  {
    IParameterInfo[] wrappers = new IParameterInfo[parameterTypes.Length];
    for(int i=0; i<wrappers.Length; i++)
    {
      wrappers[i] = new ParameterTypeWrapper(parameterTypes[i]);
    }
    return wrappers;
  }

  public static IParameterInfo[] WrapParameters(ParameterInfo[] parameters)
  {
    IParameterInfo[] wrappers = new IParameterInfo[parameters.Length];
    for(int i=0; i<wrappers.Length; i++)
    {
      wrappers[i] = new ParameterInfoWrapper(parameters[i]);
    }
    return wrappers;
  }
}
#endregion

} // namespace Scripting.Emit