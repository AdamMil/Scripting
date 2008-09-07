using System;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using Scripting.AST;
using Scripting.Runtime;

namespace Scripting.Emit
{

#region Slot
/// <summary>Represents the concept of a Slot, which is an arbitrary location for variable storage.</summary>
public abstract class Slot
{
  /// <summary>Gets whether this slot supports address-of operations.</summary>
  public abstract bool CanGetAddr { get; }
  /// <summary>Gets whether this slot supports read operations.</summary>
  public abstract bool CanRead { get; }
  /// <summary>Gets whether this slot supports write operations.</summary>
  public abstract bool CanWrite { get; }

  /// <summary>Gets the type of the variable stored in this slot.</summary>
  public abstract ITypeInfo Type { get; }

  /// <summary>Emits code to retrieve the slot value and push it onto the stack. The value must be an instance of
  /// type <see cref="Type"/>.
  /// </summary>
  /// <exception cref="NotSupportedException">Thrown if the slot is write-only or does not support code generation.</exception>
  public abstract void EmitGet(CodeGenerator cg);

  /// <summary>Emits code to retrieve the address of the value. The address must be a pointer to a value of type
  /// <see cref="Type"/>.
  /// </summary>
  /// <exception cref="NotSupportedException">Thrown if this slot does not support address-of operations.</exception>
  public abstract void EmitGetAddr(CodeGenerator cg);

  /// <summary>Emits code to set the value of a slot given a value of the correct type on the stack.</summary>
  /// <exception cref="NotSupportedException">Thrown if the slot is read-only or does not support code generation.</exception>
  public void EmitSet(CodeGenerator cg)
  {
    EmitSet(cg, Type, false);
  }

  /// <summary>Emits code to set the value of the slot given a value of type <paramref name="typeOnStack"/> on the
  /// stack.
  /// </summary>
  /// <exception cref="NotSupportedException">Thrown if the slot is read-only or does not support code generation.</exception>
  public void EmitSet(CodeGenerator cg, ITypeInfo typeOnStack)
  {
    EmitSet(cg, typeOnStack, false);
  }

  /// <summary>Emits code to set the value of the slot given another slot that contains the new value.</summary>
  /// <remarks>The default implementation retrieves the value from the slot and calls
  /// <see cref="EmitSet(CodeGenerator,Type)"/>.
  /// </remarks>
  /// <exception cref="NotSupportedException">Thrown if the slot is read-only or does not support code generation.</exception>
  public void EmitSet(CodeGenerator cg, Slot valueSlot)
  {
    EmitSet(cg, valueSlot, false);
  }

  /// <summary>Emits code to set the value of the slot given an <see cref="ASTNode"/> representing the new value.</summary>
  /// <remarks>The default implementation emits the node and calls <see cref="EmitSet(CodeGenerator,Type)"/>.</remarks>
  /// <exception cref="NotSupportedException">Thrown if the slot is read-only or does not support code generation.</exception>
  public void EmitSet(CodeGenerator cg, ASTNode valueNode)
  {
    EmitSet(cg, valueNode, false);
  }

  /// <summary>Emits code to set the value of a slot given a value of the correct type on the stack.</summary>
  /// <exception cref="NotSupportedException">Thrown if the slot is read-only or does not support code generation.</exception>
  public void EmitSet(CodeGenerator cg, bool initialize)
  {
    EmitSet(cg, Type, initialize);
  }

  /// <summary>Emits code to set the value of the slot given a value of type <paramref name="typeOnStack"/> on the
  /// stack.
  /// </summary>
  /// <exception cref="NotSupportedException">Thrown if the slot is read-only or does not support code generation.</exception>
  public abstract void EmitSet(CodeGenerator cg, ITypeInfo typeOnStack, bool initialize);

  /// <summary>Emits code to set the value of the slot given another slot that contains the new value.</summary>
  /// <remarks>The default implementation retrieves the value from the slot and calls
  /// <see cref="EmitSet(CodeGenerator,Type)"/>.
  /// </remarks>
  /// <exception cref="NotSupportedException">Thrown if the slot is read-only or does not support code generation.</exception>
  public virtual void EmitSet(CodeGenerator cg, Slot valueSlot, bool initialize)
  {
    valueSlot.EmitGet(cg);
    EmitSet(cg, valueSlot.Type, initialize);
  }

  /// <summary>Emits code to set the value of the slot given an <see cref="ASTNode"/> representing the new value.</summary>
  /// <remarks>The default implementation emits the node and calls <see cref="EmitSet(CodeGenerator,Type)"/>.</remarks>
  /// <exception cref="NotSupportedException">Thrown if the slot is read-only or does not support code generation.</exception>
  public virtual void EmitSet(CodeGenerator cg, ASTNode valueNode, bool initialize)
  {
    EmitSet(cg, valueNode.Emit(cg), initialize);
  }

  /// <summary>In interpreted execution, evaluates the slot to retrieve the value.</summary>
  /// <remarks>The default implementation throws <see cref="NotSupportedException"/>.</remarks>
  /// <exception cref="NotSupportedException">
  /// Thrown if the slot is write-only or does not support interpreted execution.
  /// </exception>
  public virtual object EvaluateGet()
  {
    throw new NotSupportedException("This slot does not support interpreted evaluation.");
  }

  /// <summary>In interpreted execution, evaluates the slot to set the value.</summary>
  /// <remarks>This method converts the value to type <see cref="Type"/> and calls <see cref="EvaluateTypedSet"/>.</remarks>
  /// <exception cref="NotSupportedException">Thrown if this slot does not support interpreted execution.</exception>
  public void EvaluateSet(object newValue, bool initialize)
  {
    EvaluateTypedSet(Ops.ConvertTo(newValue, Type.DotNetType), initialize);
  }

  public abstract bool IsSameAs(Slot other);

  /// <summary>In interpreted execution, evaluates the slot to set the value given an instance of type
  /// <see cref="Type"/> (or null, for nullable types).
  /// </summary>
  /// <param name="newValue">The value to assign to the slot, which should be an instance of <see cref="Type"/>, or
  /// null (for nullable types).
  /// </param>
  /// <param name="initialize">If true, the slot is being set for the first time.</param>
  /// <remarks>The default implementation throws <see cref="NotSupportedException"/>.</remarks>
  /// <exception cref="NotSupportedException">
  /// Thrown if the slot is read-only or does not support interpreted execution.
  /// </exception>
  protected virtual void EvaluateTypedSet(object newValue, bool initialize)
  {
    throw new NotSupportedException("This slot does not support interpreted evaluation.");
  }
}
#endregion

#region ArraySlot
public sealed class ArraySlot : Slot
{
  public ArraySlot(Slot array, int index) : this(array,  index, null) { }

  public ArraySlot(Slot array, int index, ITypeInfo desiredType)
  {
    if(array == null) throw new ArgumentNullException();
    if(!array.Type.DotNetType.IsArray) throw new ArgumentException("Slot does not reference an array.");

    this.Array       = array;
    this.elementType = array.Type.ElementType;
    this.desiredType = desiredType == null ? elementType : desiredType;
  }

  public override bool CanGetAddr
  {
    get { return elementType == desiredType; }
  }

  public override bool CanRead
  {
    get { return true; }
  }

  public override bool CanWrite
  {
    get { return true; }
  }

  public override ITypeInfo Type
  {
    get { return desiredType; }
  }

  public override void EmitGet(CodeGenerator cg)
  {
    Array.EmitGet(cg);
    cg.EmitInt(Index);
    cg.EmitArrayLoad(elementType.DotNetType);
    cg.EmitUnsafeConversion(elementType, desiredType);
  }

  public override void EmitGetAddr(CodeGenerator cg)
  {
    if(elementType != desiredType) throw new NotSupportedException();
    Array.EmitGet(cg);
    cg.EmitInt(Index);
    cg.ILG.Emit(OpCodes.Ldelema, elementType.DotNetType);
  }

  public override void EmitSet(CodeGenerator cg, ASTNode valueNode, bool initialize)
  {
    Array.EmitGet(cg);
    cg.EmitInt(Index);
    cg.EmitTypedNode(valueNode, elementType);
    cg.EmitArrayStore(elementType.DotNetType);
  }

  public override void EmitSet(CodeGenerator cg, Slot valueSlot, bool initialize)
  {
    Array.EmitGet(cg);
    cg.EmitInt(Index);
    cg.EmitTypedSlot(valueSlot, elementType);
    cg.EmitArrayStore(elementType.DotNetType);
  }

  public override void EmitSet(CodeGenerator cg, ITypeInfo typeOnStack, bool initialize)
  {
    Slot temp = cg.AllocLocalTemp(elementType);
    temp.EmitSet(cg, typeOnStack);
    EmitSet(cg, temp);
    cg.FreeLocalTemp(temp);
  }

  public override object EvaluateGet()
  {
    Array array = (Array)Array.EvaluateGet();
    return array.GetValue(Index);
  }

  protected override void EvaluateTypedSet(object newValue, bool initialize)
  {
    Array array = (Array)Array.EvaluateGet();
    array.SetValue(newValue, Index);
  }

  public override bool IsSameAs(Slot other)
  {
    ArraySlot otherArray = other as ArraySlot;
    return otherArray != null && Array.IsSameAs(otherArray.Array) && Index == otherArray.Index;
  }

  public readonly Slot Array;
  public readonly int Index;
  readonly ITypeInfo elementType, desiredType;
}
#endregion

#region ClosureSlot
public sealed class ClosureSlot : Slot
{
  /// <summary>Initializes the closure slot with a name, type, and initial value.</summary>
  /// <param name="name">
  /// The name of the slot. This name must be unique and must match a field name within the closure type.
  /// </param>
  /// <param name="initialValue">A slot used to initialize the closure slot. If not null, the closure slot will be set
  /// to this slot's value when it is first created.
  /// </param>
  /// <remarks>The closure slot is given a depth of zero. This is the constructor to be used within the method where
  /// the closure values are defined.
  /// </remarks>
  public ClosureSlot(string name, ITypeInfo type, Slot initialValue) : this(name, type, 0)
  {
    this.initialValueSlot = initialValue;
  }

  /// <param name="depth">This parameter determines how many parent closures we'll need to traverse before finding the
  /// closure that contains the value. A depth of zero is used in the function where the closure values are defined and
  /// bound. Closure slots within nested functions that use the closure value should have a depth of one, unless they
  /// create their own closures, which causes the depth to increase further by one. Each closure definition between the
  /// place where the variable is defined and where it's used increases its depth by one.
  /// </param>
  ClosureSlot(string name, ITypeInfo type, int depth)
  {
    if(type == null) throw new ArgumentNullException();
    if(depth < 0) throw new ArgumentOutOfRangeException("depth");
    this.Name  = name;
    this.type  = type;
    this.Depth = depth;
  }

  public override bool CanGetAddr
  {
    get { return true; }
  }

  public override bool CanRead
  {
    get { return true; }
  }

  public override bool CanWrite
  {
    get { return true; }
  }

  public readonly int Depth;

  public string Name
  {
    get { return name; }
    set
    {
      if(string.IsNullOrEmpty(value)) throw new ArgumentException("Name cannot be empty.");
      name = value;
    }
  }

  public override ITypeInfo Type
  {
    get { return type; }
  }

  public ClosureSlot CloneWithDepth(int depth)
  {
    return new ClosureSlot(name, type, depth);
  }

  public override void EmitGet(CodeGenerator cg)
  {
    GetFieldSlot(cg).EmitGet(cg);
  }

  public override void EmitGetAddr(CodeGenerator cg)
  {
    GetFieldSlot(cg).EmitGetAddr(cg);
  }

  public override void EmitSet(CodeGenerator cg, ASTNode valueNode, bool initialize)
  {
    GetFieldSlot(cg).EmitSet(cg, valueNode, initialize);
  }

  public override void EmitSet(CodeGenerator cg, Slot valueSlot, bool initialize)
  {
    GetFieldSlot(cg).EmitSet(cg, valueSlot, initialize);
  }

  public override void EmitSet(CodeGenerator cg, ITypeInfo typeOnStack, bool initialize)
  {
    GetFieldSlot(cg).EmitSet(cg, typeOnStack, initialize);
  }

  public void EmitInitialization(CodeGenerator cg)
  {
    if(initialValueSlot != null)
    {
      GetFieldSlot(cg).EmitSet(cg, initialValueSlot, true);
    }
  }

  public override bool IsSameAs(Slot other)
  {
    throw new NotImplementedException(); // TODO: implement this
  }

  Slot GetFieldSlot(CodeGenerator cg)
  {
    if(fieldSlot == null)
    {
      Slot closureSlot = cg.GetClosureSlot(Depth);
      fieldSlot = new FieldSlot(closureSlot, closureSlot.Type.GetField(name));
    }

    return fieldSlot;
  }

  readonly ITypeInfo type;
  readonly Slot initialValueSlot;
  string name;
  Slot fieldSlot;
}
#endregion

#region InterpretedSlot
/// <summary>Represents a slot that only works in interpreted mode.</summary>
public abstract class InterpretedSlot : Slot
{
  public override bool CanGetAddr
  {
    get { return false; }
  }

  public override void EmitGet(CodeGenerator cg)
  {
    throw new NotSupportedException("This slot does not support code generation.");
  }

  public override void EmitGetAddr(CodeGenerator cg)
  {
    throw new NotSupportedException("This slot does not support code generation.");
  }

  public override void EmitSet(CodeGenerator cg, ITypeInfo typeOnStack, bool initialize)
  {
    throw new NotSupportedException("This slot does not support code generation.");
  }
}
#endregion

#region InterpretedLocalSlot
/// <summary>Represents a local variable or function parameter in interpreted mode.</summary>
public sealed class InterpretedLocalSlot : InterpretedSlot
{
  /// <param name="name">The name of the variable. This must be a non-empty string.</param>
  /// <param name="type">The type of the variable. This must not be null.</param>
  public InterpretedLocalSlot(string name, ITypeInfo type)
  {
    if(string.IsNullOrEmpty(name)) throw new ArgumentException();
    if(type == null) throw new ArgumentNullException();
    this.name = name;
    this.type = type;
  }

  public override bool CanRead
  {
    get { return true; }
  }

  public override bool CanWrite
  {
    get { return true; }
  }

  public override ITypeInfo Type
  {
    get { return type; }
  }

  public override object EvaluateGet()
  {
    return InterpreterEnvironment.Current.Get(name);
  }

  protected override void EvaluateTypedSet(object newValue, bool initialize)
  {
    InterpreterEnvironment.Current.Set(name, newValue);
  }

  public override bool IsSameAs(Slot other)
  {
    InterpretedLocalSlot otherSlot = other as InterpretedLocalSlot;
    return otherSlot != null && string.Equals(name, otherSlot.name, StringComparison.Ordinal);
  }

  readonly string name;
  readonly ITypeInfo type;
}
#endregion

#region FieldSlot
/// <summary>Represents a value stored in a .NET field.</summary>
public sealed class FieldSlot : Slot
{
  /// <summary>Initializes the slot with a static field.</summary>
  public FieldSlot(IFieldInfo staticField) : this(null, staticField) { }

  /// <summary>Initializes the slot with a field.</summary>
  /// <param name="field">The <see cref="IFieldInfo"/> describing the field.</param>
  /// <param name="instance">A <see cref="Slot"/> representing the field's object instance. For static fields, this
  /// must be null. For non-static fields, this must not be null.
  /// </param>
  public FieldSlot(Slot instance, IFieldInfo field)
  {
    if(field == null) throw new ArgumentNullException();

    if(instance == null && !field.IsStatic)
    {
      throw new ArgumentException("For non-static fields, an instance slot must be passed.");
    }
    else if(instance != null && field.IsStatic)
    {
      throw new ArgumentException("For static fields, an instance slot must not be passed.");
    }

    Instance = instance;
    Field    = field;
  }

  public override bool CanGetAddr
  {
    get { return true; }
  }

  public override bool CanRead
  {
    get { return true; }
  }

  public override bool CanWrite
  {
    get { return true; }
  }

  public override ITypeInfo Type
  {
    get { return Field.FieldType; }
  }

  public override void EmitGet(CodeGenerator cg)
  {
    if(Instance != null) Instance.EmitGet(cg);
    cg.EmitFieldGet(Field);
  }

  public override void EmitGetAddr(CodeGenerator cg)
  {
    if(Instance != null) Instance.EmitGet(cg);
    cg.EmitFieldGetAddr(Field);
  }

  public override void EmitSet(CodeGenerator cg, ITypeInfo typeOnStack, bool initialize)
  {
    cg.EmitConversion(typeOnStack, Type);
    if(Instance == null)
    {
      cg.EmitFieldSet(Field);
    }
    else
    {
      Slot temp = cg.AllocLocalTemp(Type);
      temp.EmitSet(cg, typeOnStack);
      EmitSet(cg, temp);
      cg.FreeLocalTemp(temp);
    }
  }

  public override void EmitSet(CodeGenerator cg, Slot valueSlot, bool initialize)
  {
    if(Instance != null) Instance.EmitGet(cg);
    cg.EmitTypedSlot(valueSlot, Type);
    cg.EmitFieldSet(Field);
  }

  public override void EmitSet(CodeGenerator cg, ASTNode valueNode, bool initialize)
  {
    if(Instance != null) Instance.EmitGet(cg);
    cg.EmitTypedNode(valueNode, Type);
    cg.EmitFieldSet(Field);
  }

  public override object EvaluateGet()
  {
    return Field.Field.GetValue(Instance == null ? null : Instance.EvaluateGet());
  }

  protected override void EvaluateTypedSet(object newValue, bool initialize)
  {
    Field.Field.SetValue(Instance == null ? null : Instance.EvaluateGet(), newValue);
  }

  public override bool IsSameAs(Slot other)
  {
    FieldSlot otherField = other as FieldSlot;
    return otherField != null && Instance.IsSameAs(otherField.Instance) && Field.Field == otherField.Field;
  }

  public readonly IFieldInfo Field;
  public readonly Slot Instance;
}
#endregion

#region LocalSlot
/// <summary>Represents a .NET local variable on the stack.</summary>
public sealed class LocalSlot : Slot
{ 
  public LocalSlot(LocalBuilder lb, ITypeInfo type)
  {
    if(lb == null) throw new ArgumentNullException();
    this.builder = lb;
    this.type    = type;
  }

  public LocalSlot(CodeGenerator cg, LocalBuilder lb, ITypeInfo type, string name) : this(lb, type)
  {
    if(cg == null || name == null) throw new ArgumentNullException();
    if(string.IsNullOrEmpty(name)) throw new ArgumentException("Name must not be empty.");
    if(!cg.IsDynamicMethod && cg.Assembly.IsDebug) lb.SetLocalSymInfo(name);
  }

  public LocalSlot(CodeGenerator cg, LocalBuilder lb, ITypeInfo type, string name, int scopeStart, int scopeEnd)
    : this(lb, type)
  {
    if(cg == null || name == null) throw new ArgumentNullException();
    if(string.IsNullOrEmpty(name)) throw new ArgumentException("Name must not be empty.");
    if(!cg.IsDynamicMethod && cg.Assembly.IsDebug) lb.SetLocalSymInfo(name, scopeStart, scopeEnd);
  }

  public override bool CanGetAddr
  {
    get { return true; }
  }

  public override bool CanRead
  {
    get { return true; }
  }

  public override bool CanWrite
  {
    get { return true; }
  }

  public override ITypeInfo Type
  {
    get { return type; }
  }

  public override void EmitGet(CodeGenerator cg)
  {
    cg.ILG.Emit(OpCodes.Ldloc, builder);
  }

  public override void EmitGetAddr(CodeGenerator cg)
  {
    cg.ILG.Emit(OpCodes.Ldloca, builder);
  }

  public override void EmitSet(CodeGenerator cg, ITypeInfo typeOnStack, bool initialize)
  {
    cg.EmitConversion(typeOnStack, Type);
    cg.ILG.Emit(OpCodes.Stloc, builder);
  }

  public override bool IsSameAs(Slot other)
  {
    LocalSlot otherSlot = other as LocalSlot;
    return otherSlot != null && builder == otherSlot.builder;
  }

  readonly LocalBuilder builder;
  readonly ITypeInfo type;
}
#endregion

#region LocalProxySlot
/// <summary>Represents a local slot that will be allocated when necessary.</summary>
/// <remarks>This class exists so that AST processors can create a local slot even though the code generator for the
/// local slot has not been created yet. The actual local slot will be allocated when it's needed.
/// </remarks>
public sealed class LocalProxySlot : ProxySlot
{
  public LocalProxySlot(string name, ITypeInfo type) : base(name, type) { }

  public override bool CanGetAddr
  {
    get { return true; }
  }

  public override bool CanRead
  {
    get { return true; }
  }

  public override bool CanWrite
  {
    get { return true; }
  }

  public override bool IsSameAs(Slot other)
  {
    LocalProxySlot otherSlot = other as LocalProxySlot;
    return otherSlot != null && string.Equals(Name, otherSlot.Name, StringComparison.Ordinal);
  }

  protected override Slot CreateSlot(CodeGenerator cg, string name, ITypeInfo type)
  {
    return cg.AllocLocalVariable(name, type);
  }
}
#endregion

#region ParameterSlot
/// <summary>Represents a function parameter in a non-interpreted mode.</summary>
public sealed class ParameterSlot : Slot
{
  /// <summary>Defines a new parameter in the given method, and references that parameter.</summary>
  /// <param name="mb">The method builder representing the method.</param>
  /// <param name="argIndex">The zero-based index of the parameter (ignoring any implicit 'this' pointer).</param>
  /// <param name="name">The name of the parameter.</param>
  /// <param name="type">The type of the parameter. Note that this is not actually associated with the new parameter
  /// and must match the type of the parameter used when generating the function.
  /// </param>
  /// <remarks>The parameter created will be created with <see cref="ParameterAttributes.None"/>.</remarks>
  public ParameterSlot(MethodBuilder mb, int argIndex, string name, ITypeInfo type)
    : this(mb.DefineParameter(argIndex+1, ParameterAttributes.None, name), type) { }

  /// <summary>References a parameter given a <see cref="ParameterBuilder"/> and its type.</summary>
  /// <param name="parameterBuilder">A <see cref="ParameterBuilder"/> referencing the new parameter.</param>
  /// <param name="type">The type of the parameter.</param>
  public ParameterSlot(ParameterBuilder parameterBuilder, ITypeInfo type)
  {
    ArgIndex  = parameterBuilder.Position - 1;
    this.type = type;
  }
  
  /// <summary>References a parameter given its index (ignoring any implicit 'this' pointer) and its type.</summary>
  public ParameterSlot(int index, ITypeInfo type)
  {
    ArgIndex  = index;
    this.type = type;
  }

  public override bool CanGetAddr
  {
    get { return true; }
  }

  public override bool CanRead
  {
    get { return true; }
  }

  public override bool CanWrite
  {
    get { return true; }
  }

  /// <summary>The type of the parameter's value. If the parameter type is by reference, this returns the type referred
  /// to by the reference.
  /// </summary>
  public override ITypeInfo Type
  {
    get { return IsByRef ? type.ElementType : type; }
  }

  /// <summary>Gets the value referred to by the parameter. If the parameter type is by reference, this will
  /// dereference the pointer passed to the method.
  /// </summary>
  public override void EmitGet(CodeGenerator cg)
  {
    cg.EmitArgGet(ArgIndex);

    if(IsByRef)
    {
      cg.EmitIndirectLoad(Type.DotNetType);
    }
  }

  public override void EmitGetAddr(CodeGenerator cg)
  {
    if(IsByRef)
    {
      cg.EmitArgGet(ArgIndex);
    }
    else
    {
      cg.EmitArgGetAddr(ArgIndex);
    }
  }

  /// <summary>Sets the value referred to by the parameter. If the parameter type is by reference, this will
  /// modify the original object and not the pointer.
  /// </summary>
  public override void EmitSet(CodeGenerator cg, ITypeInfo typeOnStack, bool initialize)
  {
    if(initialize) throw new ArgumentException("Parameters can only be initialized by the framework.");

    if(IsByRef)
    {
      Slot temp = cg.AllocLocalTemp(Type);
      cg.EmitConversion(typeOnStack, Type);
      temp.EmitSet(cg, Type);

      cg.EmitArgGet(ArgIndex);
      temp.EmitGet(cg);
      cg.EmitIndirectStore(Type.DotNetType);
      cg.FreeLocalTemp(temp);
    }
    else
    {
      cg.EmitArgSet(ArgIndex);
    }
  }

  public override bool IsSameAs(Slot other)
  {
    ParameterSlot otherSlot = other as ParameterSlot;
    return otherSlot != null && ArgIndex == otherSlot.ArgIndex;
  }

  /// <summary>Gets whether the parameter is passed by reference.</summary>
  bool IsByRef
  {
    get { return type.DotNetType.IsByRef; }
  }

  /// <summary>The zero-based parameter position, excluding any implicit 'this' pointer.</summary>
  readonly int ArgIndex;
  readonly ITypeInfo type;
}
#endregion

#region ProxySlot
/// <summary>Represents a slot that will be allocated when necessary.</summary>
/// <remarks>This class exists so that AST processors can create a slot even though the code generator for the
/// slot has not been created yet. The actual slot will be allocated when it's needed.
/// </remarks>
public abstract class ProxySlot : Slot
{
  public ProxySlot(string name, ITypeInfo type)
  {
    if(name == null || type == null) throw new ArgumentNullException();
    if(string.IsNullOrEmpty(name)) throw new ArgumentException("Name cannot be empty.");
    this.name = name;
    this.type = type;
  }

  public string Name
  {
    get { return name; }
  }

  public sealed override ITypeInfo Type
  {
    get { return type; }
  }

  public override void EmitGet(CodeGenerator cg)
  {
    if(!CanRead) throw new NotSupportedException();
    GetSlot(cg).EmitGet(cg);
  }

  public override void EmitGetAddr(CodeGenerator cg)
  {
    if(!CanGetAddr) throw new NotSupportedException();
    GetSlot(cg).EmitGetAddr(cg);
  }

  public override void EmitSet(CodeGenerator cg, ASTNode valueNode, bool initialize)
  {
    if(!CanWrite) throw new NotSupportedException();
    GetSlot(cg).EmitSet(cg, valueNode, initialize);
  }

  public override void EmitSet(CodeGenerator cg, Slot valueSlot, bool initialize)
  {
    if(!CanWrite) throw new NotSupportedException();
    GetSlot(cg).EmitSet(cg, valueSlot, initialize);
  }

  public override void EmitSet(CodeGenerator cg, ITypeInfo typeOnStack, bool initialize)
  {
    if(!CanWrite) throw new NotSupportedException();
    GetSlot(cg).EmitSet(cg, typeOnStack, initialize);
  }

  public void SetType(ITypeInfo type)
  {
    if(type == null) throw new ArgumentNullException();
    if(slot != null) throw new InvalidOperationException("The type cannot be changed once the slot has been created.");
    this.type = type;
  }

  protected abstract Slot CreateSlot(CodeGenerator cg, string name, ITypeInfo type);

  protected Slot GetSlot(CodeGenerator cg)
  {
    if(slot == null) slot = CreateSlot(cg, name, type);
    return slot;
  }

  readonly string name;
  ITypeInfo type;
  Slot slot;
}
#endregion

#region ThisSlot
/// <summary>Represents the implicit 'this' pointer of the current executing method.</summary>
public sealed class ThisSlot : Slot
{
  /// <summary>Initializes the slot with the type of the class containing the method.</summary>
  public ThisSlot(ITypeInfo classType)
  {
    if(classType == null) throw new ArgumentNullException();
    type = classType;
  }

  public override bool CanGetAddr
  {
    get { return true; }
  }

  public override bool CanRead
  {
    get { return true; }
  }

  public override bool CanWrite
  {
    get { return true; }
  }

  public override ITypeInfo Type
  {
    get { return type; }
  }

  public override void EmitGet(CodeGenerator cg)
  {
    cg.ILG.Emit(OpCodes.Ldarg_0);
  }

  public override void EmitGetAddr(CodeGenerator cg)
  {
    cg.ILG.Emit(OpCodes.Ldarga, 0);
  }

  public override void EmitSet(CodeGenerator cg, ITypeInfo typeOnStack, bool initialize)
  {
    if(initialize) throw new ArgumentException("The this pointer can only be initialized by the framework.");
    cg.EmitConversion(typeOnStack, Type);
    cg.ILG.Emit(OpCodes.Starg, 0);
  }

  public override bool IsSameAs(Slot other)
  {
    return other is ThisSlot;
  }

  readonly ITypeInfo type;
}
#endregion

#region TopLevelSlot
public sealed class TopLevelSlot : Slot
{
  public TopLevelSlot(string name) : this(name, TypeWrapper.Unknown) { }

  public TopLevelSlot(string name, ITypeInfo type)
  {
    if(name == null || type == null) throw new ArgumentNullException();
    this.Name = name;
    this.type = type;
  }

  public override bool CanGetAddr
  {
    get { return type.DotNetType == typeof(object); }
  }

  public override bool CanRead
  {
    get { return true; }
  }

  public override bool CanWrite
  {
    get { return true; }
  }

  public override ITypeInfo Type
  {
    get { return type; }
  }

  public override void EmitGet(CodeGenerator cg)
  {
    EmitBinding(cg);
    if(CompilerState.Current.Debug || !CompilerState.Current.Optimize) cg.EmitCall(typeof(Ops), "CheckBinding");
    cg.EmitFieldGet(valueField);
    cg.EmitUnsafeConversion(TypeWrapper.Object, type);
  }

  public override void EmitGetAddr(CodeGenerator cg)
  {
    if(type != typeof(object)) throw new NotSupportedException("Only the address of an Object slot can be retrieved.");
    if(CompilerState.Current.Debug || !CompilerState.Current.Optimize) cg.EmitCall(typeof(Ops), "CheckBinding");
    cg.EmitFieldGetAddr(valueField);
  }

  public override void EmitSet(CodeGenerator cg, ITypeInfo typeOnStack, bool initialize)
  {
    cg.EmitConversion(typeOnStack, type);
    cg.EmitSafeConversion(type, TypeWrapper.Object);
    Slot temp = cg.AllocLocalTemp(TypeWrapper.Object);
    temp.EmitSet(cg);
    EmitBinding(cg);
    if(!initialize) cg.EmitCall(typeof(Ops), "CheckBinding");
    temp.EmitGet(cg);
    cg.FreeLocalTemp(temp);
    cg.EmitFieldSet(valueField);
  }

  public override void EmitSet(CodeGenerator cg, ASTNode valueNode, bool initialize)
  {
    EmitBinding(cg);
    if(!initialize) cg.EmitCall(typeof(Ops), "CheckBinding");
    cg.EmitTypedNode(valueNode, type);
    cg.EmitSafeConversion(type, TypeWrapper.Object);
    cg.EmitFieldSet(valueField);
  }

  public override void EmitSet(CodeGenerator cg, Slot valueSlot, bool initialize)
  {
    EmitBinding(cg);
    if(!initialize) cg.EmitCall(typeof(Ops), "CheckBinding");
    cg.EmitTypedSlot(valueSlot, type);
    cg.EmitSafeConversion(type, TypeWrapper.Object);
    cg.EmitFieldSet(valueField);
  }

  public override bool IsSameAs(Slot other)
  {
    TopLevelSlot otherSlot = other as TopLevelSlot;
    return otherSlot != null && string.Equals(Name, otherSlot.Name, StringComparison.Ordinal);
  }

  public readonly string Name;

  protected override void EvaluateTypedSet(object newValue, bool initialize)
  {
    if(initialize) TopLevel.Current.Bind(Name, newValue);
    else TopLevel.Current.Set(Name, newValue);
  }

  void EmitBinding(CodeGenerator cg)
  {
    if(binding == null)
    {
      if(TopLevel.Current == null)
      {
        throw new CompileTimeException("A top-level environment is necessary to compile this code.");
      }
      binding = cg.GetCachedConstantSlot(TopLevel.Current.GetBinding(Name));
    }

    binding.EmitGet(cg);
  }

  readonly ITypeInfo type;
  Slot binding;
  
  static readonly FieldInfo valueField = typeof(Binding).GetField("Value");
}
#endregion

} // namespace Scripting.Emit