using System;
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

  IConstructorInfo GetConstructor(BindingFlags flags, params Type[] parameterTypes);
  IFieldInfo GetField(string name);
  IMethodInfo GetMethod(string name, BindingFlags flags, params Type[] parameterTypes);
  ITypeInfo GetNestedType(string name);
  IPropertyInfo GetProperty(string name);
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

  public ITypeInfo DeclaringType
  {
    get { return new TypeWrapper(cons.DeclaringType); }
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
  public ConstructorBuilderWrapper(ConstructorBuilder method, Type[] parameters) : base(method)
  {
    this.parameters = ReflectionWrapperHelper.WrapParameters(parameters);
  }
  
  public ConstructorBuilder Builder
  {
    get { return (ConstructorBuilder)cons; }
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
}
#endregion

#region FieldInfoWrapper
public sealed class FieldInfoWrapper : IFieldInfo
{
  public FieldInfoWrapper(FieldInfo field)
  {
    if(field == null) throw new ArgumentNullException();
    this.field = field;
  }

  public FieldAttributes Attributes
  {
    get { return field.Attributes; }
  }

  public ITypeInfo DeclaringType
  {
    get { return new TypeWrapper(field.DeclaringType); }
  }

  public FieldInfo Field
  {
    get { return field; }
  }

  public ITypeInfo FieldType
  {
    get { return new TypeWrapper(field.FieldType); }
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
}
#endregion

#region MethodBuilderWrapper
public sealed class MethodBuilderWrapper : MethodInfoWrapper
{
  public MethodBuilderWrapper(MethodBuilder method, Type[] parameters) : base(method)
  {
    this.parameters = ReflectionWrapperHelper.WrapParameters(parameters);
  }

  public MethodBuilderWrapper(MethodBuilder method, IParameterInfo[] parameters) : base(method)
  {
    this.parameters = parameters;
  }

  public MethodBuilder Builder
  {
    get { return (MethodBuilder)method; }
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

  public ITypeInfo DeclaringType
  {
    get { return new TypeWrapper(method.DeclaringType); }
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

  public ITypeInfo ReturnType
  {
    get { return new TypeWrapper(method.ReturnType); }
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
    get { return new TypeWrapper(param.ParameterType); }
  }

  readonly ParameterInfo param;
}
#endregion

#region ParameterTypeWrapper
public sealed class ParameterTypeWrapper : IParameterInfo
{
  public ParameterTypeWrapper(Type type)
  {
    if(type == null) throw new ArgumentNullException();
    this.type       = new TypeWrapper(type);
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

  readonly TypeWrapper type;
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
    get { return new TypeWrapper(property.DeclaringType); }
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
    get { return new TypeWrapper(property.PropertyType); }
  }

  readonly PropertyInfo property;
}
#endregion

#region TypeWrapper
public sealed class TypeWrapper : ITypeInfo
{
  public TypeWrapper(Type type)
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
    get { return type.BaseType == null ? null : new TypeWrapper(type.BaseType); }
  }

  public ITypeInfo DeclaringType
  {
    get { return new TypeWrapper(type.DeclaringType); }
  }

  public Type DotNetType
  {
    get { return type; }
  }

  public string Name
  {
    get { return type.Name; }
  }

  public IConstructorInfo GetConstructor(BindingFlags flags, params Type[] parameterTypes)
  {
    ConstructorInfo cons = type.GetConstructor(flags, null, parameterTypes, null);
    return cons == null ? null : new ConstructorInfoWrapper(cons);
  }

  public IFieldInfo GetField(string name)
  {
    FieldInfo field = type.GetField(name, SearchAll);
    return field == null ? null : new FieldInfoWrapper(field);
  }

  public IMethodInfo GetMethod(string name, BindingFlags flags)
  {
    MethodInfo method = type.GetMethod(name, flags);
    return method == null ? null : new MethodInfoWrapper(method);
  }

  public IMethodInfo GetMethod(string name, BindingFlags flags, params Type[] parameterTypes)
  {
    MethodInfo method = type.GetMethod(name, flags, null, parameterTypes, null);
    return method == null ? null : new MethodInfoWrapper(method);
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

  const BindingFlags SearchAll = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;

  readonly Type type;
}
#endregion

#region ReflectionWrapperHelper
static class ReflectionWrapperHelper
{
  public static IParameterInfo[] WrapParameters(ParameterInfo[] parameters)
  {
    IParameterInfo[] wrappers = new IParameterInfo[parameters.Length];
    for(int i=0; i<wrappers.Length; i++)
    {
      wrappers[i] = new ParameterInfoWrapper(parameters[i]);
    }
    return wrappers;
  }
  
  public static IParameterInfo[] WrapParameters(Type[] parameterTypes)
  {
    IParameterInfo[] parameters = new IParameterInfo[parameterTypes.Length];
    for(int i=0; i<parameters.Length; i++)
    {
      parameters[i] = new ParameterTypeWrapper(parameterTypes[i]);
    }
    return parameters;
  }
}
#endregion

} // namespace Scripting.Emit