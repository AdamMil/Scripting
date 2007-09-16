using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Scripting.AST;
using Scripting.Runtime;

namespace Scripting.Emit
{

#region CG
public static class CG
{
  static CG()
  {
    // build a table of allowable implicit numeric conversions. all integer types are convertible to floating point,
    // decimal, and Integer. all signed integer types are convertible to larger signed integer types. all unsigned
    // integer types are convertible to larger integer types.
    numericConversions = new SortedList<ITypeInfo,ITypeInfo[]>(10, TypeComparer.Instance);
    numericConversions[TypeWrapper.SByte] = new ITypeInfo[]
      { TypeWrapper.Short, TypeWrapper.Int, TypeWrapper.Long, TypeWrapper.Integer,
        TypeWrapper.Double, TypeWrapper.Single, TypeWrapper.Decimal
      };
    numericConversions[TypeWrapper.Byte] = new ITypeInfo[]
      { TypeWrapper.Short, TypeWrapper.UShort, TypeWrapper.Int, TypeWrapper.UInt, TypeWrapper.Long, TypeWrapper.ULong,
        TypeWrapper.Integer, TypeWrapper.Double, TypeWrapper.Single, TypeWrapper.Decimal
      };
    numericConversions[TypeWrapper.Short] = new ITypeInfo[]
      { TypeWrapper.Int, TypeWrapper.Long, TypeWrapper.Integer, TypeWrapper.Double, TypeWrapper.Single,
        TypeWrapper.Decimal
      };
    numericConversions[TypeWrapper.UShort] = new ITypeInfo[]
      { TypeWrapper.Int, TypeWrapper.UInt, TypeWrapper.Long, TypeWrapper.ULong, TypeWrapper.Integer,
        TypeWrapper.Double, TypeWrapper.Single, TypeWrapper.Decimal
      };
    numericConversions[TypeWrapper.Int] = new ITypeInfo[]
      { TypeWrapper.Long, TypeWrapper.Integer, TypeWrapper.Double, TypeWrapper.Single, TypeWrapper.Decimal
      };
    numericConversions[TypeWrapper.UInt] = new ITypeInfo[]
      { TypeWrapper.Long, TypeWrapper.ULong, TypeWrapper.Integer,
        TypeWrapper.Double, TypeWrapper.Single, TypeWrapper.Decimal
      };
    numericConversions[TypeWrapper.Long] = new ITypeInfo[]
      { TypeWrapper.Integer, TypeWrapper.Double, TypeWrapper.Single, TypeWrapper.Decimal
      };
    numericConversions[TypeWrapper.ULong] = numericConversions[TypeWrapper.Long];
    numericConversions[TypeWrapper.Char] = numericConversions[TypeWrapper.UShort];
    numericConversions[TypeWrapper.Single] = new ITypeInfo[] { TypeWrapper.Double };
  }

  public static ITypeInfo GetCommonBaseType(ASTNode a, ASTNode b)
  {
    return GetCommonBaseType(a.ValueType, b.ValueType);
  }

  public static ITypeInfo GetCommonBaseType(ITypeInfo a, ITypeInfo b)
  {
    if(a == b) return a; // if they are the same type, return it
    if(a == TypeWrapper.Unknown || b == TypeWrapper.Unknown) return a; // if either is unknown, the base is unknown
    if(a == null || b == null) return TypeWrapper.Object; // if either is null, return Object

    // if either are void, return the other one.
    if(a == TypeWrapper.Void) return b;
    if(b == TypeWrapper.Void) return a;

    if(a.IsValueType != b.IsValueType)
    {
      return TypeWrapper.Object; // if one is reference and the other value, the ultimate base is Object
    }
    
    if(!a.IsValueType) // if they are both reference types
    {
      // if they are related by inheritence, return the common base
      if(a.IsSubclassOf(b)) return b;
      if(b.IsSubclassOf(a)) return a;
    }
    else // they are both value types
    {
      // if they're both primitive numerics, see if there are any implicit conversions
      if(IsPrimitiveNumeric(a) && IsPrimitiveNumeric(b))
      {
        if(HasImplicitConversion(a, b)) return b; // if one can be converted to the other, return the other
        if(HasImplicitConversion(b, a)) return a;
      }
    }

    // check if they implement any common interfaces
    ITypeInfo[] interfaces = a.GetInterfaces();
    foreach(ITypeInfo iface in b.GetInterfaces())
    {
      if(Array.IndexOf(interfaces, iface) != -1) // if so, return the common interface
      {
        return iface;
      }
    }

    return TypeWrapper.Object; // as a last resort, return object
  }

  public static ITypeInfo GetCommonBaseType(params ITypeInfo[] types)
  {
    if(types == null || types.Length == 0) throw new ArgumentException();

    ITypeInfo type = types[0];
    bool hadNullOrObject = false; // check if the result was Object because of an explicit null or object type

    if(type == null || type == TypeWrapper.Object)
    {
      hadNullOrObject = true;
    }

    for(int i=1; i<types.Length && type != TypeWrapper.Object; i++)
    {
      ITypeInfo other = types[i];
      if(other == null || other == TypeWrapper.Object)
      {
        hadNullOrObject = true;
        type = TypeWrapper.Object;
      }
      else
      {
        type = GetCommonBaseType(type, types[i]);
      }
    }
    
    // if it ended up being object and there were more than two base types, there may be a common interface that
    // GetCommonBaseType couldn't see due to being unable to see the whole array... unless it became object because of
    // an explicit null or object type, in which case it's definitive.
    if(type == TypeWrapper.Object && types.Length > 2 && !hadNullOrObject)
    {
      List<ITypeInfo> interfaces = new List<ITypeInfo>(types[0].GetInterfaces()); // get the interfaces of the first type
      for(int i=1; i<types.Length && interfaces.Count != 0; i++)
      {
        ITypeInfo[] otherInterfaces = types[i].GetInterfaces(); // get the interfaces of this type
        for(int j=interfaces.Count-1; j >= 0; j--) // remove all interfaces that are not common between the two lists
        {
          if(Array.IndexOf(otherInterfaces, interfaces[j]) == -1)
          {
            interfaces.RemoveAt(j);
          }
        }
      }
      
      if(interfaces.Count != 0) // if there were any interfaces left over, use one of them
      {
        type = interfaces[0];
      }
    }
    
    return type;
  }

  public static ITypeInfo GetCommonBaseType(params ASTNode[] nodes)
  {
    return GetCommonBaseType(ASTNode.GetNodeTypes(nodes));
  }

  public static ITypeInfo GetCommonBaseType(IList<ASTNode> nodes)
  {
    return GetCommonBaseType(ASTNode.GetNodeTypes(nodes));
  }

  public static CustomAttributeBuilder GetCustomAttributeBuilder(Type attributeType)
  {
    if(!attributeType.IsSubclassOf(typeof(Attribute))) throw new ArgumentException();
    return new CustomAttributeBuilder(attributeType.GetConstructor(Type.EmptyTypes), Ops.EmptyArray);
  }

  public static ITypeInfo GetImplicitConversionToPrimitiveNumeric(ITypeInfo type)
  {
    IMethodInfo dummy;
    return GetImplicitConversionToPrimitiveNumeric(type, out dummy);
  }

  public static ITypeInfo GetImplicitConversionToPrimitiveNumeric(ITypeInfo type, out IMethodInfo method)
  {
    method = null;

    if(IsPrimitiveNumeric(type))
    {
      return type;
    }
    else
    {
      IMethodInfo[] methods = type.GetMethods(BindingFlags.Public|BindingFlags.Static);
      foreach(IMethodInfo mi in methods)
      {
        if(string.Equals(mi.Name, "op_Implicit", StringComparison.Ordinal) &&
           IsPrimitiveNumeric(mi.ReturnType.DotNetType))
        {
          IParameterInfo[] parameters = mi.GetParameters();
          if(parameters.Length == 1 && parameters[0].ParameterType == type)
          {
            method = mi;
            return mi.ReturnType;
          }
        }
      }
      
      return null;
    }
  }

  public static Type GetType(object obj)
  {
    return obj == null ? null : obj.GetType();
  }

  public static Type GetTypeWithUnwrapping(object obj)
  {
    if(obj == null) return null;
    ITypeInfo typeInfo = obj as ITypeInfo;
    return typeInfo == null ? obj.GetType() : typeInfo.DotNetType;
  }

  public static ITypeInfo GetTypeInfo(object obj)
  {
    return obj == null ? null : TypeWrapper.Get(obj.GetType());
  }

  public static bool HasImplicitConversion(ITypeInfo from, ITypeInfo to)
  {
    if(to == null) throw new ArgumentNullException();
    if(from == to) return true;
    if(from == null) return !to.IsValueType; // null can be converted to reference types
    if(!to.IsValueType && to.IsAssignableFrom(from)) return true; // upcasts can be done implicitly
    
    if(IsNumeric(from.DotNetType) && IsNumeric(to.DotNetType))
    {
      ITypeInfo[] destTypes;
      return numericConversions.TryGetValue(from, out destTypes) && Array.IndexOf(destTypes, to) >= 0;
    }
    
    // check if the source type supports implicit conversion to the destination type, or to a primitive type that can
    // be implicitly converted to the destination type
    IMethodInfo mi = from.GetMethod("op_Implicit", BindingFlags.Public|BindingFlags.Static, to);
    if(mi != null &&
       (mi.ReturnType == to || mi.ReturnType.DotNetType.IsPrimitive && to.DotNetType.IsPrimitive &&
                               HasImplicitConversion(mi.ReturnType, to)))
    {
      return true;
    }
    
    return false;
  }

  public static bool IsFloatingPoint(Type type)
  {
    return type == typeof(double) || type == typeof(float);
  }

  public static bool IsFloatingPoint(TypeCode typeCode)
  {
    return typeCode == TypeCode.Double || typeCode == TypeCode.Single;
  }

  public static bool IsNumeric(ITypeInfo type)
  {
    return IsNumeric(type.DotNetType);
  }

  public static bool IsNumeric(Type type)
  {
    return IsPrimitiveNumeric(type) || type == typeof(decimal) || type == typeof(Integer) || type == typeof(Complex);
  }

  public static bool IsPrimitiveNumeric(Type type)
  {
    return type.IsPrimitive && IsPrimitiveNumeric(Type.GetTypeCode(type));
  }

  public static bool IsPrimitiveNumeric(ITypeInfo type)
  {
    return IsPrimitiveNumeric(type.DotNetType);
  }

  public static bool IsPrimitiveNumeric(TypeCode typeCode)
  {
    switch(typeCode)
    {
      case TypeCode.Byte: case TypeCode.SByte: case TypeCode.Single: case TypeCode.Double:
      case TypeCode.Int16: case TypeCode.Int32: case TypeCode.Int64:
      case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64:
        return true;
      default:
        return false;
    }
  }

  public static bool IsSigned(ITypeInfo type)
  {
    return IsSigned(type.DotNetType);
  }

  public static bool IsSigned(Type type)
  {
    return type.IsPrimitive && IsSigned(Type.GetTypeCode(type));
  }

  public static bool IsSigned(TypeCode typeCode)
  {
    switch(typeCode)
    {
      case TypeCode.Decimal: case TypeCode.Double: case TypeCode.Int16: case TypeCode.Int32: case TypeCode.Int64:
      case TypeCode.SByte: case TypeCode.Single:
        return true;
      default:
        return false;
    }
  }
  
  public static int SizeOfPrimitiveNumeric(ITypeInfo type)
  {
    return SizeOfPrimitiveNumeric(type.DotNetType);
  }

  public static int SizeOfPrimitiveNumeric(Type type)
  {
    if(!type.IsPrimitive) throw new ArgumentException();
    return SizeOfPrimitiveNumeric(Type.GetTypeCode(type));
  }

  public static int SizeOfPrimitiveNumeric(TypeCode typeCode)
  {
    switch(typeCode)
    {
      case TypeCode.Int32: case TypeCode.UInt32: case TypeCode.Single:
        return 4;
      case TypeCode.Int64: case TypeCode.UInt64: case TypeCode.Double:
        return 8;
      case TypeCode.Int16: case TypeCode.UInt16: case TypeCode.Char:
        return 2;
      case TypeCode.SByte: case TypeCode.Byte:   case TypeCode.Boolean:
        return 1;
      default:
        throw new ArgumentException();
    }
  }

  internal static void SetCustomAttribute(MemberInfo info, CustomAttributeBuilder attributeBuilder)
  {
    if(info is MethodBuilder) ((MethodBuilder)info).SetCustomAttribute(attributeBuilder);
    else if(info is ConstructorBuilder) ((ConstructorBuilder)info).SetCustomAttribute(attributeBuilder);
    else ((TypeBuilder)info).SetCustomAttribute(attributeBuilder);
  }

  sealed class TypeComparer : IComparer<ITypeInfo>
  {
    TypeComparer() { }

    public int Compare(ITypeInfo x, ITypeInfo y)
    {
      return string.CompareOrdinal(x.FullName, y.FullName);
    }
    
    public readonly static TypeComparer Instance = new TypeComparer();
  }

  static SortedList<ITypeInfo,ITypeInfo[]> numericConversions;
}
#endregion

#region CodeGenerator
public class CodeGenerator
{
  public CodeGenerator(TypeGenerator tg, IMethodBase mb, ILGenerator ilg)
  {
    Assembly  = tg.Assembly;
    TypeGen   = tg;
    Method    = mb;
    ILG       = ilg;
    IsStatic  = mb.IsStatic;
  }

  public readonly AssemblyGenerator Assembly;
  public readonly TypeGenerator TypeGen;
  public readonly IMethodBase Method;
  public readonly ILGenerator ILG;
  public readonly bool IsStatic, IsDynamicMethod;
  
  /// <summary>Gets the slot where the closure created by the current function is stored.</summary>
  public Slot ClosureSlot
  {
    get
    {
      if(closureSlot == null) throw new InvalidOperationException("No closure has been created in this function.");
      return closureSlot;
    }
  }

  /// <summary>Gets whether the code generator is emitting debug code.</summary>
  public bool IsDebug
  {
    get { return Assembly.IsDebug; }
  }

  /// <summary>Gets whether the code generator is creating a generator function.</summary>
  /// <remarks>A generator function is a function that can return at any point and, when called again, will resume
  /// execution where it left off. This is used to implement 'yield' operators, etc.
  /// </remarks>
  public bool IsGenerator
  {
    get { return isGenerator; }
  }

  /// <summary>Allocates an uninitialized temporary variable.</summary>
  public Slot AllocLocalTemp(ITypeInfo type)
  {
    return AllocLocalTemp(type, false);
  }

  /// <summary>Allocates an uninitialized temporary variable.</summary>
  /// <param name="type">The type of the variable to allocate.</param>
  /// <param name="keepAround">This parameter should be set to true if the variable will need to be accessed by other
  /// AST nodes. For instance, a 'for' loop variable would need to be kept around so it could be accessed by the
  /// body of the loop. This is only used inside constructs such as generators, where the execution can jump out of
  /// the method via the a 'yield' statement, and the local variable needs to maintain its value when execution jumps
  /// back in.
  /// </param>
  /// <returns>A <see cref="Slot"/> that can be used to access the variable.</returns>
  public Slot AllocLocalTemp(ITypeInfo type, bool keepAround)
  {
    List<Slot> free = keepAround && IsGenerator ? nsTemps : localTemps;

    if(free != null) // see if we have any variables of that type that have been previously allocated
    {
      for(int i=0; i<free.Count; i++)
      {
        if(free[i].Type == type) // if so, return remove it from the free list and return it
        {
          Slot slot = free[i];
          free.RemoveAt(i);
          return slot;
        }
      }
    }

    // otherwise, allocate a new one
    return keepAround && IsGenerator ? AllocateTemporaryField(type)
             : new LocalSlot(this, ILG.DeclareLocal(type.DotNetType), type, "tmp$"+localNameIndex++);
  }

  /// <summary>Allocates a new local variable within the current lexical scope.</summary>
  /// <param name="name">The name of the local variable. This must be non-empty and unique within the scope, although
  /// it can be reused within nested scopes.
  /// </param>
  /// <param name="type">The type of the variable.</param>
  /// <returns>A slot that references the variable.</returns>
  /// <remarks>A lexical scope must have been opened with <see cref="BeginScope"/> before this method can be called.</remarks>
  public Slot AllocLocalVariable(string name, ITypeInfo type)
  {
    if(name == null || type == null) throw new ArgumentNullException();
    if(string.IsNullOrEmpty(name)) throw new ArgumentException("Name cannot be empty.");
    if(scopes == null || scopes.Count == 0) throw new InvalidOperationException("Not inside a lexical scope.");

    Dictionary<string,Slot> scope = scopes.Peek();
    if(scope.ContainsKey(name))
    {
      throw new ArgumentException("A variable named '"+name+"' is already defined in this scope.");
    }

    Slot slot;
    if(!IsDebug || IsGenerator) // in release mode, we'll reuse local variables by calling AllocLocalTemp.
    {                           // we'll also do this in a generator because AllocLocalTemp() already handles that well
      slot = AllocLocalTemp(type);
    }
    else // TODO: add a debug mode for generators that creates nicely-named field slots?
    {
      slot = new LocalSlot(this, ILG.DeclareLocal(type.DotNetType), type, name);
    }
    scope.Add(name, slot);
    return slot;
  }

  /// <summary>Begins a new lexical scope.</summary>
  /// <remarks>Creating a lexical scope is required before calling <see cref="AllocLocalVariable"/>. Each scope can
  /// only contain one variable with a given name, but scopes can be nested. All scopes that are opened must be closed
  /// with a call to <see cref="EndScope"/> before the method is completed with <see cref="Finish"/>.
  /// </remarks>
  public void BeginScope()
  {
    if(scopes == null) scopes = new Stack<Dictionary<string,Slot>>();
    scopes.Push(new Dictionary<string,Slot>());
  }

  /// <summary>Closes a lexical scope opened with <see cref="BeginScope"/>.</summary>
  /// <remarks>All lexical must be closed the method is completed with <see cref="Finish"/>.</remarks>
  public void EndScope()
  {
    if(scopes == null || scopes.Count == 0) throw new InvalidOperationException("Not in a lexical scope.");

    if(!IsDebug || IsGenerator) // if we're in release or generator mode, the local variables in the scope were
    {                           // allocated with AllocLocalTemp(), so we need to free them.
      foreach(Slot temp in scopes.Peek().Values)
      {
        FreeLocalTemp(temp);
      }
    }
    
    scopes.Pop();
  }

  /// <summary>Returns true if <see cref="EmitConstant"/> can emit a value of the given type.</summary>
  /// <remarks>If you override this method, you should also override <see cref="EmitConstant"/>.</remarks>
  public virtual bool CanEmitConstant(Type type)
  {
    return Type.GetTypeCode(type) != TypeCode.Object ||
           type == typeof(Complex) || type == typeof(Integer) || typeof(Type).IsAssignableFrom(type) ||
           type.IsArray && CanEmitConstant(type.GetElementType());
  }

  /// <summary>Emits a Dup opcode, duplicating the item on top of the evaluation stack.</summary>
  public void EmitDup()
  {
    ILG.Emit(OpCodes.Dup);
  }

  /// <summary>Emits code to get the value of the given zero-based argument. The index is automatically adjusted to
  /// account for the 'this' pointer, so that should not be done manually.
  /// </summary>
  /// <remarks>The 'this' pointer cannot be accessed with this method. Use <see cref="EmitThis"/> to emit the 'this'
  /// pointer.
  /// </remarks>
  public void EmitArgGet(int index)
  {
    if(!IsStatic) index++; // if the method is not static, account for the 'this' pointer
    switch(index)
    {
      case 0: ILG.Emit(OpCodes.Ldarg_0); break; // use the most compact opcode for the job
      case 1: ILG.Emit(OpCodes.Ldarg_1); break;
      case 2: ILG.Emit(OpCodes.Ldarg_2); break;
      case 3: ILG.Emit(OpCodes.Ldarg_3); break;
      default: ILG.Emit(index<256 ? OpCodes.Ldarg_S : OpCodes.Ldarg, index); break;
    }
  }

  /// <summary>Emits code to get the address of the given zero-based argument. The index is automatically adjusted to
  /// account for the 'this' pointer, so that should not be done manually.
  /// </summary>
  /// <remarks>The 'this' pointer cannot be accessed with this method. Use <see cref="EmitThis"/> to emit the 'this'
  /// pointer.
  /// </remarks>
  public void EmitArgGetAddr(int index)
  {
    if(!IsStatic) index++; // if the method is not static, account for the 'this' pointer
    ILG.Emit(index<256 ? OpCodes.Ldarga_S : OpCodes.Ldarga, index);
  }

  /// <summary>Emits code to set the value of the given zero-based argument. The index is automatically adjusted to
  /// account for the 'this' pointer, so that should not be done manually.
  /// </summary>
  /// <remarks>The 'this' pointer cannot be accessed with this method. Use <see cref="EmitThis"/> to emit the 'this'
  /// pointer.
  /// </remarks>
  public void EmitArgSet(int index)
  {
    if(!IsStatic) index++; // if the method is not static, account for the 'this' pointer
    ILG.Emit(index<256 ? OpCodes.Starg_S : OpCodes.Starg, index);
  }

  /// <summary>Emits a set of nodes as an array using the most specific type possible.</summary>
  /// <remarks>Returns the type of the array created.</remarks>
  public ITypeInfo EmitArray(params ASTNode[] nodes)
  {
    return EmitArray((IList<ASTNode>)nodes);
  }

  /// <summary>Emits a set of nodes as an array using the most specific type possible.</summary>
  /// <remarks>Returns the type of the array created.</remarks>
  public ITypeInfo EmitArray(IList<ASTNode> nodes)
  {
    ITypeInfo elementType = CG.GetCommonBaseType(nodes);
    EmitArray(nodes, elementType);
    return elementType.MakeArrayType();
  }

  /// <summary>Emits a set of nodes as an array, converting each node to the given element type.</summary>
  public void EmitArray(IList<ASTNode> nodes, ITypeInfo elementType)
  {
    Type dotNetType = elementType.DotNetType;
    // if the element type is a primitive and all the nodes are constant, we can emit the array in a compact form.
    // EmitConstant() already knows how to do that, so we'll call it to do the work.
    if(dotNetType.IsPrimitive && ASTNode.AreConstant(nodes))
    {
      EmitConstant(ASTNode.EvaluateNodes(nodes, dotNetType));
    }
    else
    {
      EmitNewArray(dotNetType, nodes.Count);
      for(int i=0; i<nodes.Count; i++)
      {
        EmitDup();
        EmitInt(i);
        EmitTypedNode(nodes[i], elementType);
        EmitArrayStore(dotNetType);
      }
    }
  }

  /// <summary>Emits an array load opcode appropriate for the given element type (eg, Ldelem_I4 for Int32).</summary>
  public void EmitArrayLoad(Type elementType)
  {
    if(!elementType.IsValueType) // reference values are all represented by a simple reference
    {
      ILG.Emit(OpCodes.Ldelem_Ref);
    }
    else // but value types have varying representations
    {
      switch(Type.GetTypeCode(elementType))
      {
        case TypeCode.Boolean: case TypeCode.Byte: case TypeCode.SByte:
          ILG.Emit(OpCodes.Ldelem_I1);
          break;
        case TypeCode.Char: case TypeCode.UInt16: case TypeCode.Int16:
          ILG.Emit(OpCodes.Ldelem_I2);
          break;
        case TypeCode.Int32: case TypeCode.UInt32:
          ILG.Emit(OpCodes.Ldelem_I4);
          break;
        case TypeCode.Int64: case TypeCode.UInt64:
          ILG.Emit(OpCodes.Ldelem_I8);
          break;
        case TypeCode.Single:
          ILG.Emit(OpCodes.Ldelem_R4);
          break;
        case TypeCode.Double:
          ILG.Emit(OpCodes.Ldelem_R8);
          break;
        default:
          ILG.Emit(OpCodes.Ldelem, elementType);
          break;
      }
    }
  }

  /// <summary>Emits an array store opcode appropriate for the given element type (eg, Stelem_I4 for Int32).</summary>
  public void EmitArrayStore(Type elementType)
  {
    if(!elementType.IsValueType) // reference values are all represented by a simple reference
    {
      ILG.Emit(OpCodes.Stelem_Ref);
    }
    else // but value types have varying representations
    {
      switch(Type.GetTypeCode(elementType))
      {
        case TypeCode.Boolean: case TypeCode.Byte: case TypeCode.SByte:
          ILG.Emit(OpCodes.Stelem_I1);
          break;
        case TypeCode.Char: case TypeCode.UInt16: case TypeCode.Int16:
          ILG.Emit(OpCodes.Stelem_I2);
          break;
        case TypeCode.Int32: case TypeCode.UInt32:
          ILG.Emit(OpCodes.Stelem_I4);
          break;
        case TypeCode.Int64: case TypeCode.UInt64:
          ILG.Emit(OpCodes.Stelem_I8);
          break;
        case TypeCode.Single:
          ILG.Emit(OpCodes.Stelem_R4);
          break;
        case TypeCode.Double:
          ILG.Emit(OpCodes.Stelem_R8);
          break;
        default:
          ILG.Emit(OpCodes.Stelem, elementType);
          break;
      }
    }
  }

  /// <summary>Emits a boolean value.</summary>
  public void EmitBool(bool value)
  {
    EmitInt(value ? 1 : 0);
  }

  /// <summary>Emits a constant value. The value is only emitted once, in a global location. Subsequent calls will
  /// simply reference the value in the global location.
  /// </summary>
  public void EmitCachedConstant(object value)
  {
    if(value == null || value.GetType().IsPrimitive) // if the value is null or a simple primitive, don't cache it
    {
      EmitConstant(value);
    }
    else
    {
      GetCachedConstantSlot(value).EmitGet(this);
    }
  }
  
  /// <summary>Emits a call to the given constructor. This should only be used in a constructor, to call the
  /// constructor of the base class.
  /// </summary>
  public void EmitCall(ConstructorInfo ci)
  {
    ILG.Emit(OpCodes.Call, ci);
  }

  /// <summary>Emits a call to the given constructor. This should only be used in a constructor, to call the
  /// constructor of the base class.
  /// </summary>
  public void EmitCall(IConstructorInfo ci)
  {
    ILG.Emit(OpCodes.Call, ci.Method);
  }

  /// <summary>Emits a call to the given method.</summary>
  public void EmitCall(MethodInfo mi)
  {
    // if it's a static function, or it's an instance method declared in the same assembly as the caller and is
    // not virtual or known to not be overriden, we can safely use the Call opcode.
    if(mi.IsStatic || (mi.DeclaringType.Assembly == TypeGen.Assembly.Assembly &&
                       (!mi.IsVirtual || mi.DeclaringType.IsSealed || mi.IsFinal)))
    {
      ILG.Emit(OpCodes.Call, mi);
    }
    else // otherwise it's an instance method and might be overridden. even if it's non-virtual, we'll still emit a
    {    // virtual call because in that case it's defined in another assembly and might become virtual in the future.
      if(mi.DeclaringType.IsValueType) ILG.Emit(OpCodes.Constrained, mi.DeclaringType); // simply calls on value types
      ILG.Emit(OpCodes.Callvirt, mi);
    }
  }

  /// <summary>Emits a call to the given method.</summary>
  public void EmitCall(IMethodInfo mi)
  {
    EmitCall(mi.Method);
  }

  /// <summary>Emits a call to the given method. The method will be found by name, and work as long as the method has
  /// no overrides.
  /// </summary>
  public void EmitCall(Type type, string method)
  {
    EmitCall(type.GetMethod(method, SearchAll));
  }

  /// <summary>Emits a call to the given method. The method will be found by name and parameter signature.</summary>
  public void EmitCall(Type type, string method, params Type[] paramTypes)
  {
    EmitCall(type.GetMethod(method, paramTypes));
  }

  /// <summary>Emits a call to the given method. The method will be found by name, and work as long as the method has
  /// no overrides.
  /// </summary>
  public void EmitCall(ITypeInfo type, string method)
  {
    EmitCall(type.GetMethod(method, SearchAll).Method);
  }

  /// <summary>Emits a call to the given method. The method will be found by name and parameter signature.</summary>
  public void EmitCall(ITypeInfo type, string method, params ITypeInfo[] paramTypes)
  {
    EmitCall(type.GetMethod(method, SearchAll, paramTypes).Method);
  }

  /// <summary>Emits a constant value onto the stack.</summary>
  /// <remarks> This method does not work on all types. To check if a type can be emitted with this function, use the
  /// <see cref="CanEmitConstant"/> method. If you override this method, you should also override
  /// <see cref="CanEmitConstant"/>.
  /// </remarks>
  public virtual void EmitConstant(object value)
  {
    Array array = value as Array;
    if(array != null)
    {
      Type elementType = array.GetType().GetElementType();

      // primitive arrays can be stored in a compact form and loaded quickly, but this can't be done if we're prevented
      // from creating new metadata in the assembly (ie, if it's a dynamic method or snippet). we don't want to do it
      // for arrays that are too small, either, though.
      if(array.Length > 4 && elementType.IsPrimitive && !IsDynamicMethod && !Assembly.IsCreatingSnippet)
      {
        // get the array data into a byte array.
        byte[] data = array as byte[]; // if the array is already a byte array, use it.
        if(data == null) // otherwise, allocate a new array and copy the data into it.
        {
          data = new byte[Marshal.SizeOf(elementType) * array.Length];     // allocate a new array of the right size
          GCHandle handle = GCHandle.Alloc(array, GCHandleType.Pinned);    // pin the array
          Marshal.Copy(handle.AddrOfPinnedObject(), data, 0, data.Length); // copy the data from the array
          handle.Free();                                                   // unpin the array
        }

        FieldBuilder dataField =
          TypeGen.Assembly.Module.DefineInitializedData("data$"+dataIndex.Next+"_"+elementType.Name,
                                                        data, FieldAttributes.Static);
        EmitNewArray(array);
        EmitDup();
        EmitToken(dataField);
        EmitCall(typeof(RuntimeHelpers), "InitializeArray");
      }
      else
      {
        EmitNewArray(array);
        int index = 0;
        foreach(object element in array)
        {
          EmitDup();
          EmitInt(index++);
          EmitConstant(element);
          EmitArrayStore(elementType);
        }
      }
    }
    else
    {
      Type valueType = CG.GetType(value);
      switch(Type.GetTypeCode(valueType))
      {
        case TypeCode.Boolean: EmitInt((bool)value ? 1 : 0); break;
        case TypeCode.Byte:    EmitInt((int)(byte)value); break;
        case TypeCode.Char:    EmitInt((int)(char)value); break;
        case TypeCode.DateTime:
          EmitLong(((DateTime)value).ToBinary());
          EmitCall(typeof(DateTime), "FromBinary");
          break;
        case TypeCode.DBNull:  EmitFieldGet(typeof(DBNull), "Value"); break;
        case TypeCode.Decimal: 
        {
          decimal d = (decimal)value;
          // if the value has no fractional part and can be represented in a smaller than the bits array, do so
          if(d == decimal.Truncate(d) && d >= long.MinValue && d <= long.MaxValue)
          {
            if(d >= int.MinValue && d <= int.MaxValue)
            {
              EmitInt(decimal.ToInt32(d));
              EmitNew(typeof(decimal), typeof(int));
            }
            else
            {
              EmitLong(decimal.ToInt64(d));
              EmitNew(typeof(decimal), typeof(long));
            }
          }
          else
          {
            EmitConstant(decimal.GetBits((decimal)value));
            EmitNew(typeof(decimal), typeof(int[]));
          }
          break;
        }

        case TypeCode.Double: ILG.Emit(OpCodes.Ldc_R8, (double)value); break;
        case TypeCode.Empty:  ILG.Emit(OpCodes.Ldnull); break;
        case TypeCode.Int16:  EmitInt((int)(short)value); break;
        case TypeCode.Int32:  EmitInt((int)value); break;
        case TypeCode.Int64:  EmitLong((long)value); break;
        case TypeCode.SByte:  EmitInt((int)(sbyte)value); break;
        case TypeCode.Single: ILG.Emit(OpCodes.Ldc_R4, (float)value); break;
        case TypeCode.String: EmitString((string)value); break;
        case TypeCode.UInt16: EmitInt((int)(ushort)value); break;
        case TypeCode.UInt32: EmitInt((int)(uint)value); break;
        case TypeCode.UInt64: EmitLong((long)(ulong)value); break;

        case TypeCode.Object:
        default:
          if(value is Complex)
          {
            Complex c = (Complex)value;
            ILG.Emit(OpCodes.Ldc_R8, c.Real);
            if(c.Imaginary == 0)
            {
              EmitNew(typeof(Complex), typeof(double));
            }
            else
            {
              ILG.Emit(OpCodes.Ldc_R8, c.Imaginary);
              EmitNew(typeof(Complex), typeof(double), typeof(double));
            }
          }
          else if(value is Integer)
          {
            Integer i = (Integer)value;
            if(i >= int.MinValue && i <= int.MaxValue)
            {
              EmitInt(Integer.ToInt32(i));
              EmitNew(typeof(Integer), typeof(int));
            }
            else if(i >= uint.MinValue && i <= uint.MaxValue)
            {
              EmitInt((int)Integer.ToUInt32(i));
              EmitNew(typeof(Integer), typeof(uint));
            }
            else if(i >= long.MinValue && i <= long.MaxValue)
            {
              EmitConstant(Integer.ToInt64(i));
              EmitNew(typeof(Integer), typeof(long));
            }
            else if(i >= ulong.MinValue && i <= ulong.MaxValue)
            {
              EmitConstant(Integer.ToUInt64(i));
              EmitNew(typeof(Integer), typeof(ulong));
            }
            else
            {
              EmitConstant((short)i.Sign);
              EmitConstant(i.GetInternalData());
              EmitNew(typeof(Integer), typeof(short), typeof(uint[]));
            }
          }
          else if(value is Rational)
          {
            Rational r = (Rational)value;
            EmitConstant(r.Numerator);
            EmitConstant(r.Denominator);
            EmitCall(typeof(Rational), "Recreate");
          }
          else if(value is Type)
          {
            EmitType((Type)value);
          }
          else
          {
            throw new NotImplementedException("constant: "+valueType);
          }
          break;
      }
    }
  }

  /// <summary>Emits a constant value converted to the given type if possible.</summary>
  /// <returns>Returns the type that was actually emitted.</returns>
  /// <remarks>This method will attempt to do the conversion at compile time if possible.</remarks>
  public ITypeInfo EmitConstant(object value, ITypeInfo desiredType)
  {
    ITypeInfo valueType = CG.GetTypeInfo(value);
    // do the conversion at compile time if possible
    if(CanEmitConstant(desiredType.DotNetType) && CG.HasImplicitConversion(valueType, desiredType))
    {                                           
      EmitConstant(Ops.ConvertTo(value, desiredType.DotNetType));
      return desiredType;
    }
    else // otherwise, do the conversion at runtime
    {
      EmitConstant(value);
      return TryEmitSafeConversion(valueType, desiredType) ? desiredType : valueType;
    }
  }

  /// <summary>Emits a runtime conversion to <paramref name="desiredType"/> if <paramref name="typeOnStack"/> is
  /// <see cref="TypeWrapper.Unknown"/>, and a safe conversion otherwise.
  /// </summary>
  public void EmitConversion(ITypeInfo typeOnStack, ITypeInfo desiredType)
  {
    if(typeOnStack == TypeWrapper.Unknown) EmitRuntimeConversion(typeOnStack, desiredType);
    else EmitSafeConversion(typeOnStack, desiredType);
  }

  /// <summary>Emits a default value of the given type.</summary>
  /// <param name="type">The type of value to emit. If the type is null, a null value will be emitted. If the type
  /// is typeof(void), nothing will be emitted.
  /// </param>
  public void EmitDefault(ITypeInfo type)
  {
    if(type == null || !type.IsValueType) // use null for reference types
    {
      ILG.Emit(OpCodes.Ldnull);
      return;
    }

    switch(type.TypeCode)
    {
      case TypeCode.Boolean: case TypeCode.Byte: case TypeCode.Char: case TypeCode.Int16: case TypeCode.Int32:
      case TypeCode.SByte: case TypeCode.UInt16: case TypeCode.UInt32:
        EmitInt(0);
        break;

      case TypeCode.DBNull:
        EmitFieldGet(type, "Value"); // DBNull.Value
        break;

      case TypeCode.Decimal:
        EmitFieldGet(type, "Zero"); // Decimal.Zero
        break;

      case TypeCode.Double:
        ILG.Emit(OpCodes.Ldc_R8, 0.0);
        break;

      case TypeCode.Single:
        ILG.Emit(OpCodes.Ldc_R4, 0.0f);
        break;

      case TypeCode.Int64: case TypeCode.UInt64:
        EmitLong(0);
        break;

      default:
        if(type == TypeWrapper.Void)
        {
          return;
        }
        else // it's a struct, so create a zero initialized instance of it
        {
          Slot tmp = AllocLocalTemp(type);
          tmp.EmitGetAddr(this);
          ILG.Emit(OpCodes.Initobj, type.DotNetType);
          tmp.EmitGet(this);
          FreeLocalTemp(tmp);
        }
        break;
    }
  }

  /// <summary>Emits code to retrieve the given field value.</summary>
  public void EmitFieldGet(Type type, string fieldName)
  {
    EmitFieldGet(type.GetField(fieldName, SearchAll));
  }

  /// <summary>Emits code to retrieve the given field value.</summary>
  public void EmitFieldGet(ITypeInfo type, string fieldName)
  {
    EmitFieldGet(type.GetField(fieldName));
  }

  /// <summary>Emits code to retrieve the given field value.</summary>
  public void EmitFieldGet(FieldInfo field)
  {
    if(field.IsLiteral) EmitConstant(field.GetValue(null)); // if it's a constant field, emit the value directly
    else ILG.Emit(field.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, field);
  }

  /// <summary>Emits code to retrieve the given field value.</summary>
  public void EmitFieldGet(IFieldInfo field)
  {
    EmitFieldGet(field.Field);
  }

  /// <summary>Emits code to retrieve the given field address.</summary>
  public void EmitFieldGetAddr(Type type, string fieldName)
  {
    EmitFieldGetAddr(type.GetField(fieldName, SearchAll));
  }

  /// <summary>Emits code to retrieve the given field address.</summary>
  public void EmitFieldGetAddr(ITypeInfo type, string fieldName)
  {
    EmitFieldGetAddr(type.GetField(fieldName));
  }

  /// <summary>Emits code to retrieve the given field address.</summary>
  public void EmitFieldGetAddr(FieldInfo field)
  {
    if(field.IsLiteral) throw new ArgumentException("Cannot get the address of a literal (constant) field");
    else ILG.Emit(field.IsStatic ? OpCodes.Ldsflda : OpCodes.Ldflda, field);
  }

  /// <summary>Emits code to retrieve the given field address.</summary>
  public void EmitFieldGetAddr(IFieldInfo field)
  {
    EmitFieldGetAddr(field.Field);
  }

  /// <summary>Emits code to set the given field value.</summary>
  public void EmitFieldSet(Type type, string fieldName)
  {
    EmitFieldSet(type.GetField(fieldName, SearchAll));
  }

  /// <summary>Emits code to set the given field value.</summary>
  public void EmitFieldSet(ITypeInfo type, string fieldName)
  {
    EmitFieldSet(type.GetField(fieldName));
  }

  /// <summary>Emits code to set the given field value.</summary>
  public void EmitFieldSet(FieldInfo field)
  {
    if(field.IsLiteral)
    {
      throw new ArgumentException("Cannot set a literal (constant) field");
    }
    else if(field.IsInitOnly && !(Method.Method is ConstructorInfo))
    {
      throw new ArgumentException("Cannot set a read-only field outside of a constructor.");
    }
    else
    {
      ILG.Emit(field.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld, field);
    }
  }

  /// <summary>Emits code to set the given field value.</summary>
  public void EmitFieldSet(IFieldInfo field)
  {
    EmitFieldSet(field.Field);
  }

  public void EmitIndirectLoad(Type type)
  {
    if(!type.IsValueType) // reference values are all represented by a simple reference
    {
      ILG.Emit(OpCodes.Ldind_Ref);
    }
    else // but value types have varying representations
    {
      switch(Type.GetTypeCode(type))
      {
        case TypeCode.Boolean: case TypeCode.Byte:
          ILG.Emit(OpCodes.Ldind_U1);
          break;
        case TypeCode.SByte:
          ILG.Emit(OpCodes.Ldind_I1);
          break;
        case TypeCode.Char: case TypeCode.UInt16:
          ILG.Emit(OpCodes.Ldind_U2);
          break;
        case TypeCode.Int16:
          ILG.Emit(OpCodes.Ldind_I2);
          break;
        case TypeCode.Int32:
          ILG.Emit(OpCodes.Ldind_I4);
          break;
        case TypeCode.UInt32:
          ILG.Emit(OpCodes.Ldind_U4);
          break;
        case TypeCode.Int64: case TypeCode.UInt64:
          ILG.Emit(OpCodes.Ldind_I8);
          break;
        case TypeCode.Single:
          ILG.Emit(OpCodes.Ldind_R4);
          break;
        case TypeCode.Double:
          ILG.Emit(OpCodes.Ldind_R8);
          break;
        default:
          ILG.Emit(OpCodes.Ldobj, type);
          break;
      }
    }
  }

  public void EmitIndirectStore(Type type)
  {
    if(!type.IsValueType) // reference values are all represented by a simple reference
    {
      ILG.Emit(OpCodes.Stind_Ref);
    }
    else // but value types have varying representations
    {
      switch(Type.GetTypeCode(type))
      {
        case TypeCode.Boolean: case TypeCode.Byte: case TypeCode.SByte:
          ILG.Emit(OpCodes.Stind_I1);
          break;
        case TypeCode.Char: case TypeCode.UInt16: case TypeCode.Int16:
          ILG.Emit(OpCodes.Stind_I2);
          break;
        case TypeCode.Int32: case TypeCode.UInt32:
          ILG.Emit(OpCodes.Stind_I4);
          break;
        case TypeCode.Int64: case TypeCode.UInt64:
          ILG.Emit(OpCodes.Stind_I8);
          break;
        case TypeCode.Single:
          ILG.Emit(OpCodes.Stind_R4);
          break;
        case TypeCode.Double:
          ILG.Emit(OpCodes.Stind_R8);
          break;
        default:
          ILG.Emit(OpCodes.Stobj, type);
          break;
      }
    }
  }

  /// <summary>Emits an Int32 value using the smallest code possible.</summary>
  public void EmitInt(int value)
  {
    OpCode op;
    switch(value)
    {
      case -1: op = OpCodes.Ldc_I4_M1; break; // certain integer values have their own opcodes. use them if possible
      case 0:  op = OpCodes.Ldc_I4_0; break;
      case 1:  op = OpCodes.Ldc_I4_1; break;
      case 2:  op = OpCodes.Ldc_I4_2; break;
      case 3:  op = OpCodes.Ldc_I4_3; break;
      case 4:  op = OpCodes.Ldc_I4_4; break;
      case 5:  op = OpCodes.Ldc_I4_5; break;
      case 6:  op = OpCodes.Ldc_I4_6; break;
      case 7:  op = OpCodes.Ldc_I4_7; break;
      case 8:  op = OpCodes.Ldc_I4_8; break;
      default:
        if(value >= -128 && value <= 127) // otherwise, if the value fits in a byte, we can use the short form
        {
          ILG.Emit(OpCodes.Ldc_I4_S, (byte)value);
        }
        else
        {
          ILG.Emit(OpCodes.Ldc_I4, value);
        }
        return;
    }

    ILG.Emit(op);
  }
  
  /// <summary>Emits an Int64 value using the smallest code possible.</summary>
  public void EmitLong(long value)
  {
    if(value >= int.MinValue && value <= int.MaxValue)
    {
      EmitInt((int)value);
      ILG.Emit(OpCodes.Conv_I8);
    }
    else
    {
      ILG.Emit(OpCodes.Ldc_I8, value);
    }
  }

  /// <summary>Emits code to create a new instance of an object of the given type using the default constructor.
  /// For value types, initializes the value to all zeros.
  /// </summary>
  public void EmitNew(Type type)
  {
    if(type.IsValueType) // value types don't have default constructors, so we'll call EmitDefault() which handles them
    {
      EmitDefault(TypeWrapper.Get(type));
    }
    else // otherwise it's a reference type, so emit the default constructor
    {
      EmitNew(type.GetConstructor(ConstructorSearch, null, Type.EmptyTypes, null));
    }
  }

  /// <summary>Emits code to create a new instance of an object of the given type using the default constructor.
  /// For value types, initializes the value to all zeros.
  /// </summary>
  public void EmitNew(ITypeInfo type)
  {
    if(type.IsValueType)
    {
      EmitNew(type.DotNetType);
    }
    else
    {
      EmitNew(type.GetConstructor(ConstructorSearch, TypeWrapper.EmptyTypes));
    }
  }

  /// <summary>Emits code to create a new instance of an object of the given type using the constructor that takes the
  /// given parameter types.
  /// </summary>
  public void EmitNew(Type type, params Type[] paramTypes)
  {
    if(paramTypes.Length == 0) // if there are no parameters, use the EmitNew(Type) overload, which handles value types
    {
      EmitNew(type);
    }
    else
    {
      EmitNew(type.GetConstructor(ConstructorSearch, null, paramTypes, null));
    }
  }

  /// <summary>Emits code to create a new instance of an object of the given type using the constructor that takes the
  /// given parameter types.
  /// </summary>
  public void EmitNew(ITypeInfo type, params ITypeInfo[] paramTypes)
  {
    if(paramTypes.Length == 0) // if there are no parameters, use the EmitNew(Type) overload, which handles value types
    {
      EmitNew(type);
    }
    else
    {
      EmitNew(type.GetConstructor(ConstructorSearch, paramTypes));
    }
  }

  /// <summary>Emits code to create a new instance of an object of the given type using the given
  /// <see cref="ConstructorInfo"/>.
  /// </summary>
  public void EmitNew(ConstructorInfo ci)
  {
    ILG.Emit(OpCodes.Newobj, ci);
  }

  /// <summary>Emits code to create a new instance of an object of the given type using the given
  /// <see cref="IConstructorInfo"/>.
  /// </summary>
  public void EmitNew(IConstructorInfo ci)
  {
    ILG.Emit(OpCodes.Newobj, ci.Method);
  }

  /// <summary>Emits code to create a new zero-based, one-dimensional array of the given element type and length.</summary>
  public void EmitNewArray(Type elementType, int length)
  {
    if(elementType == null) throw new ArgumentNullException();
    if(length < 0) throw new ArgumentOutOfRangeException();

    if(length == 0 && elementType == typeof(object)) // for an empty object arrays, grab a reference to the static one
    {
      EmitFieldGet(typeof(Ops), "EmptyArray");
    }
    else // otherwise allocate it
    {
      EmitInt(length);
      ILG.Emit(OpCodes.Newarr, elementType);
    }
  }

  /// <summary>Emits code to create a new array of the same rank and bounds as the given array.</summary>
  public void EmitNewArray(Array template)
  {
    Type elementType = template.GetType().GetElementType();

    if(template.Rank == 1 && template.GetLowerBound(0) == 0) // if it's a zero-based one-dimensional array use Newarr
    {
      EmitNewArray(elementType, template.Length);
    }
    else
    {
      bool zeroBased = true;
      int[] lengths = new int[template.Rank];
      int[] bounds  = new int[template.Rank];
      for(int i=0; i<template.Rank; i++)
      {
        lengths[i] = template.GetLength(i);
        bounds[i]  = template.GetLowerBound(i);
        if(bounds[i] != 0)
        {
          zeroBased = false;
        }
      }

      EmitType(elementType);
      EmitConstant(lengths);
      if(zeroBased) // if the dimensions are all zero-based, we can invoke the overload that just takes the lengths
      {
        EmitCall(typeof(Array), "CreateInstance", typeof(int[]));
      }
      else // otherwise, we 'll invoke the one that takes the lengths and the lower bounds
      {
        EmitConstant(bounds);
        EmitCall(typeof(Array), "CreateInstance", typeof(int[]), typeof(int[]));
      }
    }
  }

  /// <summary>Emits a null reference.</summary>
  public void EmitNull()
  {
    ILG.Emit(OpCodes.Ldnull);
  }

  /// <summary>Emits a set of nodes as an array with an element type of <see cref="System.Object"/>.</summary>
  public void EmitObjectArray(params ASTNode[] nodes)
  {
    EmitArray(nodes, TypeWrapper.Object);
  }

  /// <summary>Emits a set of nodes as an array with an element type of <see cref="System.Object"/>.</summary>
  public void EmitObjectArray(IList<ASTNode> nodes)
  {
    EmitArray(nodes, TypeWrapper.Object);
  }

  /// <summary>Emits a pop opcode, which removes the topmost item from the evaluation stack.</summary>
  public void EmitPop()
  {
    ILG.Emit(OpCodes.Pop);
  }

  /// <summary>Emits a return opcode.</summary>
  public void EmitReturn()
  {
    ILG.Emit(OpCodes.Ret);
  }
  
  public void EmitReturn(ASTNode node)
  {
    EmitTypedNode(node, ((IMethodInfo)Method).ReturnType);
    ILG.Emit(OpCodes.Ret);
  }

  public void EmitRuntimeConversion(ITypeInfo typeOnStack, ITypeInfo destinationType)
  {
    EmitRuntimeConversion(typeOnStack, destinationType, CompilerState.Current.Checked);
  }

  /// <summary>Emits code to convert a value from one type to another. If not enough information is available to
  /// perform the conversion safely at compile time, code to perform a runtime conversion will be emitted.
  /// </summary>
  public void EmitRuntimeConversion(ITypeInfo typeOnStack, ITypeInfo destinationType, bool checkOverflow)
  {
    if(!TryEmitSafeConversion(typeOnStack, destinationType, checkOverflow))
    {
      EmitSafeConversion(typeOnStack, TypeWrapper.Object);

      // check for some built-in types and emit smaller code for them
      if(destinationType == TypeWrapper.ICallable)
      {
        EmitCall(typeof(Ops), "ConvertToCallable");
      }
      else // fall back to the general case conversion function if necessary
      {
        // if the destination type is a pointer, get the type that it points to and convert to that. we'll get the
        // pointer later via unboxing
        Type rootType = destinationType.DotNetType;
        if(rootType.IsPointer || rootType.IsByRef)
        {
          rootType = rootType.GetElementType();
          if(!rootType.IsValueType || rootType.IsPointer || rootType.IsByRef)
          {
            throw new ArgumentException(typeOnStack.FullName+" could not be converted to "+destinationType.FullName);
          }
        }

        EmitType(rootType);
        EmitCall(typeof(Ops), "ConvertTo", typeof(object), typeof(Type));

        // at this point, the object on the stack is an instance of rootType (potentially boxed)
        if(rootType.IsValueType) // if the root type is a value type, we can either return the value or a pointer to it
        {
          ILG.Emit(OpCodes.Unbox, rootType);

          // if we want the actual value, we'll load it indirectly from the pointer. otherwise, if we want the
          // pointer, we'll simply leave it on the stack.
          if(!destinationType.DotNetType.IsPointer && !destinationType.DotNetType.IsByRef)
          {
            EmitIndirectLoad(rootType);
          }
        }
        else if(destinationType.DotNetType != typeof(object)) // otherwise, it's a reference type. perform a downcast if necessary
        {
          ILG.Emit(OpCodes.Castclass, destinationType.DotNetType);
        }
      }
    }
  }

  /// <summary>Emits code to convert a value from one type to another.</summary>
  public void EmitSafeConversion(ITypeInfo typeOnStack, ITypeInfo destType)
  {
    EmitSafeConversion(typeOnStack, destType, CompilerState.Current.Checked);
  }

  /// <summary>Emits code to convert a value from one type to another. Numeric values will be converted without loss of
  /// data, but the value may change in the representation of the destination type. For instance, a byte value of 255
  /// can be converted into a signed byte value without loss, but it will have the value -1. To emit code to ensure
  /// that the value does not change, pass true for <paramref name="checkOverflow"/>.
  /// </summary>
  /// <param name="checkOverflow">If true, overflow will be checked to ensure that the value does not change after
  /// conversion.
  /// </param>
  public void EmitSafeConversion(ITypeInfo typeOnStack, ITypeInfo destType, bool checkOverflow)
  {
    if(!TryEmitSafeConversion(typeOnStack, destType, checkOverflow))
    {
      throw new ArgumentException(typeOnStack.FullName+" cannot be converted to "+destType.FullName);
    }
  }

  public void EmitString(string value)
  {
    if(value == null) ILG.Emit(OpCodes.Ldnull);
    else ILG.Emit(OpCodes.Ldstr, value);
  }

  public void EmitThis()
  {
    if(IsStatic) throw new InvalidOperationException("The current method is static.");
    ILG.Emit(OpCodes.Ldarg_0);
  }

  /// <summary>Emits code to get the runtime token of the given field.</summary>
  public void EmitToken(FieldInfo field)
  {
    ILG.Emit(OpCodes.Ldtoken, field);
  }

  /// <summary>Emits code to get the runtime token of the given method.</summary>
  public void EmitToken(MethodInfo method)
  {
    ILG.Emit(OpCodes.Ldtoken, method);
  }

  /// <summary>Emits a reference to the given type.</summary>
  public void EmitType(ITypeInfo type)
  {
    EmitType(type.DotNetType);
  }

  /// <summary>Emits a reference to the given type.</summary>
  public void EmitType(Type type)
  {
    if(type.IsByRef)
    {
      EmitType(type.GetElementType());
      EmitCall(typeof(Type), "MakeByRefType");
    }
    else if(type.IsPointer)
    {
      EmitType(type.GetElementType());
      EmitCall(typeof(Type), "MakePointerType");
    }
    else
    {
      ILG.Emit(OpCodes.Ldtoken, type);
      EmitCall(typeof(Type), "GetTypeFromHandle");
    }
  }

  public void EmitTypedNode(ASTNode node, ITypeInfo desiredType)
  {
    EmitConversion(node.Emit(this), desiredType);
  }
  
  public void EmitTypedOperator(Operator op, ITypeInfo desiredType, params ASTNode[] nodes)
  {
    EmitTypedOperator(op, desiredType, (IList<ASTNode>)nodes);
  }

  public void EmitTypedOperator(Operator op, ITypeInfo desiredType, IList<ASTNode> nodes)
  {
    EmitConversion(op.Emit(this, desiredType, nodes), desiredType);
  }

  public void EmitTypedSlot(Slot slot, ITypeInfo desiredType)
  {
    slot.EmitGet(this);
    EmitConversion(slot.Type, desiredType);
  }

  /// <summary>Emits code to convert a value from one type to another. This method attempts all the same conversions
  /// as <see cref="EmitSafeConversion"/>, but will additionally perform downcasts and unboxing, neither of which are
  /// guaranteed to succeed.
  /// </summary>
  public void EmitUnsafeConversion(ITypeInfo typeOnStack, ITypeInfo destType)
  {
    EmitUnsafeConversion(typeOnStack, destType, CompilerState.Current.Checked);
  }

  /// <summary>Emits code to convert a value from one type to another. This method attempts all the same conversions
  /// as <see cref="EmitSafeConversion"/>, but will additionally perform downcasts and unboxing, neither of which are
  /// guaranteed to succeed.
  /// </summary>
  public void EmitUnsafeConversion(ITypeInfo typeOnStack, ITypeInfo destType, bool checkOverflow)
  {
    if(!TryEmitUnsafeConversion(typeOnStack, destType, checkOverflow))
    {
      throw new ArgumentException(typeOnStack.FullName+" cannot be converted to "+destType.FullName);
    }
  }

  /// <summary>Emits a node in a void context, meaning that the evaluation stack will be unchanged.</summary>
  public void EmitVoid(ASTNode node)
  {
    ITypeInfo type = node.Emit(this);
    if(type != TypeWrapper.Void)
    {
      throw new CompileTimeException("Node emitted in a void context must not visibly alter the stack.");
    }
  }
  
  /// <summary>Emits a collection of nodes in a void context, meaning that the evaluation stack will be unchanged.</summary>
  public void EmitVoids(ICollection<ASTNode> nodes)
  {
    foreach(ASTNode node in nodes) EmitVoid(node);
  }

  public void Finish()
  {
    if(scopes != null && scopes.Count != 0)
    {
      throw new InvalidOperationException("Not all lexical scopes were closed.");
    }
  }

  public void FreeLocalTemp(Slot slot)
  {
    if(slot == null) throw new ArgumentNullException();

    if(slot is LocalSlot)
    {
      if(localTemps == null) localTemps = new List<Slot>();
      localTemps.Add(slot);
    }
    else
    {
      if(nsTemps == null) nsTemps = new List<Slot>();
      nsTemps.Add(slot);
    }
  }

  /// <summary>Gets an array of cached constant <see cref="Binding"/> objects that were emitted.</summary>
  public Binding[] GetCachedBindings()
  {
    return constantCache == null ? new Binding[0] : constantCache.GetBindings();
  }

  /// <summary>Gets an array of cached constant non-<see cref="Binding"/> objects that were emitted.</summary>
  public object[] GetCachedNonBindings()
  {
    return constantCache == null ? new object[0] : constantCache.GetNonBindings();
  }

  /// <summary>Returns a slot that holds the given value. The slot and its value should be treated as read-only.</summary>
  public Slot GetCachedConstantSlot(object value)
  {
    if(constantCache == null)
    {
      constantCache = new ConstantCache(this);
    }

    return constantCache.GetSlot(value);
  }

  /// <summary>Marks the current method as being a generator method. This must be done before any code is emitted or
  /// variables are allocated in the method.
  /// </summary>
  public void MakeGenerator()
  {
    isGenerator = true;
  }

  /// <summary>Marks this method as compiler generated and not meant to be visible in the debugger.</summary>
  public void MarkAsNonUserCode()
  {
    SetCustomAttribute(typeof(CompilerGeneratedAttribute));
    SetCustomAttribute(typeof(DebuggerNonUserCodeAttribute));
    SetCustomAttribute(typeof(DebuggerStepThroughAttribute));
  }

  /// <summary>Adds a custom attribute to the method or constructor being generated.</summary>
  public void SetCustomAttribute(Type attributeType)
  {
    SetCustomAttribute(CG.GetCustomAttributeBuilder(attributeType));
  }

  /// <summary>Adds a custom attribute to the method or constructor being generated.</summary>
  public void SetCustomAttribute(ConstructorInfo attributeConstructor, params object[] constructorArgs)
  {
    SetCustomAttribute(new CustomAttributeBuilder(attributeConstructor, constructorArgs));
  }

  /// <summary>Adds a custom attribute to the method or constructor being generated.</summary>
  public void SetCustomAttribute(CustomAttributeBuilder attributeBuilder)
  {
    CG.SetCustomAttribute(Method.Method, attributeBuilder);
  }

  public TypeGenerator SetupClosure(ClosureSlot[] closures, bool referencesParentClosure)
  {
    if(closures == null || closures.Length == 0) throw new ArgumentException("No closures were given.");
    
    TypeGenerator closureType = TypeGen.DefineNestedType(TypeAttributes.NestedPrivate|TypeAttributes.Sealed,
                                                         "closure$"+closureIndex.Next);

    // if the closure references its parent closure, create a constructor that takes the 'this' pointer (the parent
    // closure) and stores it
    if(referencesParentClosure)
    {
      FieldSlot parent = closureType.DefineField("$parent", TypeGen);
      CodeGenerator cons = closureType.DefineConstructor(TypeGen);
      cons.EmitThis(); // call the base constructor
      cons.EmitCall(cons.TypeGen.BaseType.GetConstructor(BindingFlags.Public|BindingFlags.Instance,
                                                         TypeWrapper.EmptyTypes));
      cons.EmitThis(); // set the parent field
      parent.EmitSet(cons);
      cons.EmitReturn(); // return
      cons.Finish();

      EmitThis();
      EmitNew((IConstructorInfo)cons.Method);
    }
    else
    {
      CodeGenerator cons = closureType.DefineDefaultConstructor();
      cons.EmitReturn();
      cons.Finish();
      EmitNew((IConstructorInfo)cons.Method);
    }

    closureSlot = AllocLocalTemp(closureType, true);
    closureSlot.EmitSet(this);

    foreach(ClosureSlot slot in closures)
    {
      closureType.DefineField(slot.Name, slot.Type);
      slot.EmitInitialization(this);
    }
    
    return closureType;
  }

  public bool TryEmitSafeConversion(ITypeInfo typeOnStack, ITypeInfo destType)
  {
    return TryEmitSafeConversion(typeOnStack, destType, CompilerState.Current.Checked);
  }

  public bool TryEmitSafeConversion(ITypeInfo typeOnStack, ITypeInfo destType, bool checkOverflow)
  {
    // the destination type cannot be null, although the source type can.
    if(destType == null) throw new ArgumentNullException();
    if(typeOnStack == destType) return true; // if the types are the same, no work needs to be done

    if(destType == TypeWrapper.Void) // if we're converting to 'void', we simply pop the value from the stack
    {
      EmitPop();
      return true;
    }

    // if the value on the stack is null, it can be converted to any non-value type
    if(typeOnStack == null) return !destType.IsValueType;

    // if the destination type is a reference type and it's compatible with the type on the stack...
    if(!destType.IsValueType && destType.IsAssignableFrom(typeOnStack))
    {
      // then box the type on the stack if necessary, and return true
      if(typeOnStack.IsValueType) ILG.Emit(OpCodes.Box, typeOnStack.DotNetType);
      return true;
    }

    // if both types are primitives, we can use the built-in conversion opcodes
    if(typeOnStack.DotNetType.IsPrimitive && destType.DotNetType.IsPrimitive)
    {
      switch(destType.TypeCode)
      {
        case TypeCode.Boolean:
          // if we're converting to bool, we'll simply convert non-I4 types to I4, preserving signed-ness
          if(typeOnStack == TypeWrapper.Double || typeOnStack == TypeWrapper.Single || typeOnStack == TypeWrapper.Long)
          {
            ILG.Emit(OpCodes.Conv_I4);
          }
          else if(typeOnStack == TypeWrapper.ULong)
          {
            ILG.Emit(OpCodes.Conv_U4);
          }
          break;
        case TypeCode.SByte:
          ILG.Emit(checkOverflow ? CG.IsSigned(typeOnStack) ? OpCodes.Conv_Ovf_I1 : OpCodes.Conv_Ovf_I1_Un
                                 : OpCodes.Conv_I1);
          break;
        case TypeCode.Byte:
          ILG.Emit(checkOverflow ? CG.IsSigned(typeOnStack) ? OpCodes.Conv_Ovf_U1 : OpCodes.Conv_Ovf_U1_Un
                                 : OpCodes.Conv_U1);
          break;
        case TypeCode.Int16:
          if(CG.SizeOfPrimitiveNumeric(typeOnStack) >= 2)
          {
            ILG.Emit(checkOverflow ? CG.IsSigned(typeOnStack) ? OpCodes.Conv_Ovf_I2 : OpCodes.Conv_Ovf_I2_Un
                                   : OpCodes.Conv_I2);
          }
          break;
        case TypeCode.UInt16: case TypeCode.Char: // chars are also 16-bit integers
          if(CG.SizeOfPrimitiveNumeric(typeOnStack) >= 2)
          {
            ILG.Emit(checkOverflow ? CG.IsSigned(typeOnStack) ? OpCodes.Conv_Ovf_U2 : OpCodes.Conv_Ovf_U2_Un
                                   : OpCodes.Conv_U2);
          }
          break;
        case TypeCode.Int32:
          if(CG.SizeOfPrimitiveNumeric(typeOnStack) >= 4)
          {
            ILG.Emit(checkOverflow ? CG.IsSigned(typeOnStack) ? OpCodes.Conv_Ovf_I4 : OpCodes.Conv_Ovf_I4_Un
                                   : OpCodes.Conv_I4);
          }
          break;
        case TypeCode.UInt32:
          if(CG.SizeOfPrimitiveNumeric(typeOnStack) >= 4)
          {
            ILG.Emit(checkOverflow ? CG.IsSigned(typeOnStack) ? OpCodes.Conv_Ovf_U4 : OpCodes.Conv_Ovf_U4_Un
                                   : OpCodes.Conv_U4);
          }
          break;
        case TypeCode.Int64:
          if(checkOverflow)
          {
            ILG.Emit(typeOnStack == TypeWrapper.ULong ? OpCodes.Conv_Ovf_I8_Un :
                     typeOnStack == TypeWrapper.Double || typeOnStack == TypeWrapper.Single ? OpCodes.Conv_Ovf_I8 : OpCodes.Conv_I8);
          }
          else if(typeOnStack != TypeWrapper.ULong)
          {
            ILG.Emit(OpCodes.Conv_I8);
          }
          break;
        case TypeCode.UInt64:
          if(checkOverflow)
          {
            ILG.Emit(CG.IsSigned(typeOnStack) ? OpCodes.Conv_Ovf_U8 : OpCodes.Conv_U8);
          }
          else if(typeOnStack != TypeWrapper.Long)
          {
            ILG.Emit(OpCodes.Conv_U8);
          }
          break;
        case TypeCode.Single:
          if(!CG.IsSigned(typeOnStack))
          {
            ILG.Emit(OpCodes.Conv_R_Un);
          }
          else
          {
            ILG.Emit(OpCodes.Conv_R4);
          }
          break;
        case TypeCode.Double:
          if(!CG.IsSigned(typeOnStack))
          {
            ILG.Emit(OpCodes.Conv_R_Un);
          }
          ILG.Emit(OpCodes.Conv_R8);
          break;
        default:
          return false;
      }

      return true;
    }

    // see about conversion from primitive numerics to Decimal and Integer
    if(CG.IsPrimitiveNumeric(typeOnStack) && CG.IsNumeric(destType))
    {
      // both Decimal and Integer have constructors that take float, double, int, uint, long, and ulong, except
      // that Integer is lacking float
      if(typeOnStack == TypeWrapper.Single && destType == TypeWrapper.Integer) // convert float to double for Integer
      {
        ILG.Emit(OpCodes.Conv_R8);
        typeOnStack = TypeWrapper.Double;
      }
      else if(CG.SizeOfPrimitiveNumeric(typeOnStack) < 4) // make small integer values into int or uint
      {
        typeOnStack = CG.IsSigned(typeOnStack) ? TypeWrapper.Int : TypeWrapper.UInt;
      }
      EmitNew(destType, typeOnStack);
      return true;
    }

    // check if the source type supports implicit conversion to the destination type, or to a primitive type that can
    // be implicitly converted to the destination type
    IMethodInfo mi = typeOnStack.GetMethod("op_Implicit", BindingFlags.Public|BindingFlags.Static, typeOnStack);
    if(mi != null &&
       (mi.ReturnType == destType || destType.DotNetType.IsPrimitive && mi.ReturnType.DotNetType.IsPrimitive &&
                                     CG.HasImplicitConversion(mi.ReturnType, destType)))
    {
      EmitCall(mi);
      if(mi.ReturnType != destType)
      {
        EmitSafeConversion(mi.ReturnType, destType);
      }
      return true;
    }

    return false; // give up
  }

  public bool TryEmitUnsafeConversion(ITypeInfo typeOnStack, ITypeInfo destinationType)
  {
    return TryEmitUnsafeConversion(typeOnStack, destinationType, CompilerState.Current.Checked);
  }

  public bool TryEmitUnsafeConversion(ITypeInfo typeOnStack, ITypeInfo destinationType, bool checkOverflow)
  {
    if(!TryEmitSafeConversion(typeOnStack, destinationType, checkOverflow))
    {
      // these conversions are not guaranteed to work
      if(typeOnStack.DotNetType == typeof(object) && destinationType.IsValueType) // unbox value types
      {
        ILG.Emit(OpCodes.Unbox, destinationType.DotNetType);
        EmitIndirectLoad(destinationType.DotNetType);
      }
      else if(!destinationType.IsValueType && destinationType.IsSubclassOf(typeOnStack)) // downcast reference types
      {
        ILG.Emit(OpCodes.Castclass, destinationType.DotNetType);
      }
      else
      {
        return false;
      }
    }
    
    return true;
  }

  #region ConstantCache
  sealed class ConstantCache
  {
    public ConstantCache(CodeGenerator cg)
    {
      CodeGen = cg;
    }

    /// <summary>Gets a list of the <see cref="Binding"/> objects that were added.</summary>
    public Binding[] GetBindings()
    {
      List<Binding> bindings = new List<Binding>();
      foreach(object obj in objects)
      {
        Binding binding = obj as Binding;
        if(binding != null)
        {
          bindings.Add(binding);
        }
      }
      return bindings.ToArray();
    }

    /// <summary>Gets a list of the non-<see cref="Binding"/> objects that were added.</summary>
    public object[] GetNonBindings()
    {
      List<object> list = new List<object>();
      foreach(object obj in objects)
      {
        if(!(obj is Binding))
        {
          list.Add(obj);
        }
      }
      return list.ToArray();
    }

    public Slot GetSlot(object value)
    {
      if(value == null) throw new ArgumentNullException();

      List<int> indices;
      if(indexLookup.TryGetValue(value.GetType(), out indices))
      {
        if(value is Binding) // if it's a binding, we can compare by object reference
        {
          for(int i=0; i<indices.Count; i++)
          {
            int index = indices[i];
            if(value == objects[index]) return slots[index];
          }
        }
        else if(value is Array) // if it's an array, do array comparison
        {
          Array array = (Array)value;
          for(int i=0; i<indices.Count; i++)
          {
            int index = indices[i];
            if(ArraysEqual(array, (Array)objects[index])) return slots[index];
          }
        }
        else // otherwise, do simple value comparison
        {
          for(int i=0; i<indices.Count; i++)
          {
            int index = indices[i];
            if(value.Equals(objects[index])) return slots[index];
          }
        }
      }

      // if it gets here, the value was not found in the cache. add it
      TypeGenerator privateClass = CodeGen.Assembly.GetPrivateClass();
      Slot slot;
      if(!CodeGen.IsDynamicMethod) // in regular methods, constants are emitted as fields
      {
        slot = privateClass.DefineStaticField(FieldAttributes.Public|FieldAttributes.InitOnly,
                                              "const$" + slots.Count, CG.GetTypeInfo(value));
        CodeGenerator initializer = privateClass.GetInitializer();
        EmitConstantValue(value, initializer);
        slot.EmitSet(initializer);
      }
      else
      {
        throw new NotImplementedException();
      }

      // add the index to the type lookup table
      if(indices == null)
      {
        indices = new List<int>();
        indexLookup.Add(value.GetType(), indices);
      }
      indices.Add(objects.Count);

      objects.Add(value);
      slots.Add(slot);

      // and return the slot
      return slot;
    }

    static bool ArraysEqual(Array a, Array b)
    {
      if(a.Length != b.Length || a.Rank != b.Rank) return false; // ensure that they're the same length and rank
      
      if(a.Rank == 1) // if they're both one-dimensional arrays, compare the items
      {
        for(int i=0; i<a.Length; i++)
        {
          if(!object.Equals(a.GetValue(i), b.GetValue(i)))
          {
            return false;
          }
        }
      }
      else // they're both multidimensional arrays
      {
        for(int i=0; i<a.Rank; i++) // ensure that each array has the same size and bounds
        {
          if(a.GetLength(i) != b.GetLength(i) || a.GetLowerBound(i) != b.GetLowerBound(i))
          {
            return false;
          }
        }
        
        // compare the elements
        int[] indices = new int[a.Rank];
        for(int i=0; i<indices.Length; i++) // initialize indices to their lower bounds
        {
          indices[i] = a.GetLowerBound(i);
        }

        for(int index=0; index<a.Length; index++)
        {
          if(!object.Equals(a.GetValue(indices), b.GetValue(indices)))
          {
            return false;
          }
          
          // move to the next array index
          for(int i=a.Rank-1; i>=0; i--)
          {
            if(++indices[i] == a.GetUpperBound(i))
            {
              indices[i] = a.GetLowerBound(i);
            }
            else
            {
              break;
            }
          }
        }
      }
      
      return true;
    }
    
    readonly List<object> objects = new List<object>();
    readonly List<Slot> slots = new List<Slot>();
    readonly Dictionary<Type,List<int>> indexLookup = new Dictionary<Type,List<int>>();
    readonly CodeGenerator CodeGen;

    static void EmitConstantValue(object value, CodeGenerator cg)
    {
      if(value is Binding)
      {
        Binding binding = (Binding)value;
        cg.EmitFieldGet(typeof(TopLevel), "Current");
        cg.EmitString(binding.Name);
        cg.EmitCall(typeof(TopLevel), "GetBinding");
      }
      else
      {
        cg.EmitConstant(value);
      }
    }
  }
  #endregion
  
  Slot AllocateTemporaryField(ITypeInfo type)
  {
    return TypeGen.DefineField("tmp$"+localNameIndex++, type);
  }

  const BindingFlags ConstructorSearch = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
  const BindingFlags SearchAll = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;

  /// <summary>A list of cached constants, grouped by type.</summary>
  ConstantCache constantCache;
  /// <summary>A list of currently unused temporary variables.</summary>
  List<Slot> localTemps, nsTemps;
  /// <summary>A stack of the current lexical scopes.</summary>
  Stack<Dictionary<string,Slot>> scopes;
  /// <summary>The slot referencing the closure where variables referenced by nested functions will be stored.</summary>
  Slot closureSlot;
  /// <summary>The current index for naming temporary variables.</summary>
  int localNameIndex;
  /// <summary>Whether or not the current method is a generator method.</summary>
  bool isGenerator;

  static readonly Index dataIndex = new Index(), closureIndex = new Index();
}
#endregion

} // namespace Scripting.Emit