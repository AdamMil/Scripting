using System;
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
  public abstract Type Type { get; }

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
    EmitSet(cg, Type);
  }

  /// <summary>Emits code to set the value of the slot given a value of type <paramref name="typeOnStack"/> on the
  /// stack.
  /// </summary>
  /// <exception cref="NotSupportedException">Thrown if the slot is read-only or does not support code generation.</exception>
  public abstract void EmitSet(CodeGenerator cg, Type typeOnStack);

  /// <summary>Emits code to set the value of the slot given another slot that contains the new value.</summary>
  /// <remarks>The default implementation retrieves the value from the slot and calls
  /// <see cref="EmitSet(CodeGenerator,Type)"/>.
  /// </remarks>
  /// <exception cref="NotSupportedException">Thrown if the slot is read-only or does not support code generation.</exception>
  public virtual void EmitSet(CodeGenerator cg, Slot valueSlot)
  {
    valueSlot.EmitGet(cg);
    EmitSet(cg, valueSlot.Type);
  }

  /// <summary>Emits code to set the value of the slot given an <see cref="ASTNode"/> representing the new value.</summary>
  /// <remarks>The default implementation emits the node and calls <see cref="EmitSet(CodeGenerator,Type)"/>.</remarks>
  /// <exception cref="NotSupportedException">Thrown if the slot is read-only or does not support code generation.</exception>
  public virtual void EmitSet(CodeGenerator cg, ASTNode valueNode)
  {
    Type desiredType = Type;
    valueNode.Emit(cg, ref desiredType);
    EmitSet(cg, desiredType);
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
  public void EvaluateSet(object newValue)
  {
    EvaluateTypedSet(Ops.ConvertTo(newValue, Type));
  }

  /// <summary>In interpreted execution, evaluates the slot to set the value given an instance of type
  /// <see cref="Type"/> (or null, for nullable types).
  /// </summary>
  /// <remarks>The default implementation throws <see cref="NotSupportedException"/>.</remarks>
  /// <exception cref="NotSupportedException">
  /// Thrown if the slot is read-only or does not support interpreted execution.
  /// </exception>
  protected virtual void EvaluateTypedSet(object newValue)
  {
    throw new NotSupportedException("This slot does not support interpreted evaluation.");
  }
}
#endregion

#region ArraySlot
public sealed class ArraySlot : Slot
{
  public ArraySlot(Slot array, int index) : this(array,  index, null) { }

  public ArraySlot(Slot array, int index, Type desiredType)
  {
    if(array == null) throw new ArgumentNullException();
    if(!array.Type.IsArray) throw new ArgumentException("Slot does not reference an array.");

    this.Array       = array;
    this.elementType = array.Type.GetElementType();
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

  public override Type Type
  {
    get { return desiredType; }
  }

  public override void EmitGet(CodeGenerator cg)
  {
    Array.EmitGet(cg);
    cg.EmitInt(Index);
    cg.EmitArrayLoad(elementType);
    if(elementType != desiredType) cg.EmitUnsafeConversion(elementType, desiredType);
  }

  public override void EmitGetAddr(CodeGenerator cg)
  {
    if(elementType != desiredType) throw new NotSupportedException();
    Array.EmitGet(cg);
    cg.EmitInt(Index);
    cg.ILG.Emit(OpCodes.Ldelema, elementType);
  }

  public override void EmitSet(CodeGenerator cg, ASTNode valueNode)
  {
    Array.EmitGet(cg);
    cg.EmitInt(Index);
    cg.EmitTypedNode(valueNode, elementType);
    cg.EmitArrayStore(elementType);
  }

  public override void EmitSet(CodeGenerator cg, Slot valueSlot)
  {
    Array.EmitGet(cg);
    cg.EmitInt(Index);
    cg.EmitTypedSlot(valueSlot, elementType);
    cg.EmitArrayStore(elementType);
  }

  public override void EmitSet(CodeGenerator cg, Type typeOnStack)
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

  protected override void EvaluateTypedSet(object newValue)
  {
    Array array = (Array)Array.EvaluateGet();
    array.SetValue(newValue, Index);
  }

  public readonly Slot Array;
  public readonly int Index;
  readonly Type elementType, desiredType;
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

  public override void EmitSet(CodeGenerator cg, Type typeOnStack)
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
  public InterpretedLocalSlot(string name, Type type)
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

  public override Type Type
  {
    get { return type; }
  }

  public override object EvaluateGet()
  {
    return InterpreterEnvironment.Current.Get(name);
  }

  protected override void EvaluateTypedSet(object newValue)
  {
    InterpreterEnvironment.Current.Set(name, newValue);
  }
  
  readonly string name;
  readonly Type type;
}
#endregion

#region FieldSlot
/// <summary>Represents a value stored in a .NET field.</summary>
public sealed class FieldSlot : Slot
{
  /// <summary>Initializes the slot with a static field.</summary>
  public FieldSlot(FieldInfo staticField) : this(null, staticField) { }

  /// <summary>Initializes the slot with a field.</summary>
  /// <param name="field">The <see cref="FieldInfo"/> describing the field.</param>
  /// <param name="instance">A <see cref="Slot"/> representing the field's object instance. For static fields, this
  /// must be null. For non-static fields, this must not be null.
  /// </param>
  public FieldSlot(Slot instance, FieldInfo field)
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

  public override Type Type
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

  public override void EmitSet(CodeGenerator cg, Type typeOnStack)
  {
    cg.EmitRuntimeConversion(typeOnStack, Type);
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

  public override void EmitSet(CodeGenerator cg, Slot valueSlot)
  {
    if(Instance != null) Instance.EmitGet(cg);
    cg.EmitTypedSlot(valueSlot, Type);
    cg.EmitFieldSet(Field);
  }

  public override void EmitSet(CodeGenerator cg, ASTNode valueNode)
  {
    if(Instance != null) Instance.EmitGet(cg);
    cg.EmitTypedNode(valueNode, Type);
    cg.EmitFieldSet(Field);
  }

  public override object EvaluateGet()
  {
    return Field.GetValue(Instance == null ? null : Instance.EvaluateGet());
  }

  protected override void EvaluateTypedSet(object newValue)
  {
    Field.SetValue(Instance == null ? null : Instance.EvaluateGet(), newValue);
  }

  public readonly FieldInfo Field;
  public readonly Slot Instance;
}
#endregion

#region LocalSlot
/// <summary>Represents a .NET local variable on the stack.</summary>
public sealed class LocalSlot : Slot
{ 
  public LocalSlot(LocalBuilder lb)
  {
    if(lb == null) throw new ArgumentNullException();
    builder = lb;
  }

  public LocalSlot(CodeGenerator cg, LocalBuilder lb, string name) : this(lb)
  {
    if(cg == null || name == null) throw new ArgumentNullException();
    if(string.IsNullOrEmpty(name)) throw new ArgumentException("Name must not be empty.");
    if(!cg.IsDynamicMethod && cg.Assembly.IsDebug) lb.SetLocalSymInfo(name);
  }

  public LocalSlot(CodeGenerator cg, LocalBuilder lb, string name, int scopeStart, int scopeEnd) : this(lb)
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

  public override Type Type
  {
    get { return builder.LocalType; }
  }

  public override void EmitGet(CodeGenerator cg)
  {
    cg.ILG.Emit(OpCodes.Ldloc, builder);
  }

  public override void EmitGetAddr(CodeGenerator cg)
  {
    cg.ILG.Emit(OpCodes.Ldloca, builder);
  }

  public override void EmitSet(CodeGenerator cg, Type typeOnStack)
  {
    cg.EmitRuntimeConversion(typeOnStack, Type);
    cg.ILG.Emit(OpCodes.Stloc, builder);
  }

  readonly LocalBuilder builder;
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
  public ParameterSlot(MethodBuilder mb, int argIndex, string name, Type type)
    : this(mb.DefineParameter(argIndex+1, ParameterAttributes.None, name), type) { }

  /// <summary>References a parameter given a <see cref="ParameterBuilder"/> and its type.</summary>
  /// <param name="parameterBuilder">A <see cref="ParameterBuilder"/> referencing the new parameter.</param>
  /// <param name="type">The type of the parameter.</param>
  public ParameterSlot(ParameterBuilder parameterBuilder, Type type)
  {
    ArgIndex  = parameterBuilder.Position - 1;
    this.type = type;
  }
  
  /// <summary>References a parameter given its index (ignoring any implicit 'this' pointer) and its type.</summary>
  public ParameterSlot(int index, Type type)
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
  public override Type Type
  {
    get { return IsByRef ? type.GetElementType() : type; }
  }

  /// <summary>Gets the value referred to by the parameter. If the parameter type is by reference, this will
  /// dereference the pointer passed to the method.
  /// </summary>
  public override void EmitGet(CodeGenerator cg)
  {
    cg.EmitArgGet(ArgIndex);

    if(IsByRef)
    {
      cg.EmitIndirectLoad(Type);
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
  public override void EmitSet(CodeGenerator cg, Type typeOnStack)
  {
    if(IsByRef)
    {
      Slot temp = cg.AllocLocalTemp(Type);
      cg.EmitRuntimeConversion(typeOnStack, Type);
      temp.EmitSet(cg, Type);

      cg.EmitArgGet(ArgIndex);
      temp.EmitGet(cg);
      cg.EmitIndirectStore(Type);
      cg.FreeLocalTemp(temp);
    }
    else
    {
      cg.EmitArgSet(ArgIndex);
    }
  }

  /// <summary>Gets whether the parameter is passed by reference.</summary>
  bool IsByRef
  {
    get { return type.IsByRef; }
  }

  /// <summary>The zero-based parameter position, excluding any implicit 'this' pointer.</summary>
  readonly int ArgIndex;
  readonly Type type;
}
#endregion

#region ThisSlot
/// <summary>Represents the implicit 'this' pointer of the current executing method.</summary>
public sealed class ThisSlot : Slot
{
  /// <summary>Initializes the slot with the type of the class containing the method.</summary>
  public ThisSlot(Type classType)
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

  public override Type Type
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

  public override void EmitSet(CodeGenerator cg, Type typeOnStack)
  {
    cg.EmitRuntimeConversion(typeOnStack, Type);
    cg.ILG.Emit(OpCodes.Starg, 0);
  }

  Type type;
}
#endregion

#region TopLevelSlot
public sealed class TopLevelSlot : Slot
{
  public TopLevelSlot(string name) : this(name, typeof(object)) { }
  public TopLevelSlot(string name, Type type)
  {
    if(name == null || type == null) throw new ArgumentNullException();
    this.Name = name;
    this.type = type;
  }

  public override bool CanGetAddr
  {
    get { return type == typeof(object); }
  }

  public override bool CanRead
  {
    get { return true; }
  }

  public override bool CanWrite
  {
    get { return true; }
  }

  public override Type Type
  {
    get { return type; }
  }

  public override void EmitGet(CodeGenerator cg)
  {
    EmitBinding(cg);
    if(cg.TypeGen.Assembly.IsDebug) cg.EmitCall(typeof(Ops), "CheckBinding");
    cg.EmitFieldGet(valueField);
    cg.EmitUnsafeConversion(typeof(object), type);
  }

  public override void EmitGetAddr(CodeGenerator cg)
  {
    if(type != typeof(object)) throw new NotSupportedException("Only the address of an Object slot can be retrieved.");
    if(cg.TypeGen.Assembly.IsDebug) cg.EmitCall(typeof(Ops), "CheckBinding");
    cg.EmitFieldGetAddr(valueField);
  }

  public override void EmitSet(CodeGenerator cg, Type typeOnStack)
  {
    cg.EmitRuntimeConversion(typeOnStack, type);
    cg.EmitSafeConversion(type, typeof(object));
    Slot temp = cg.AllocLocalTemp(typeof(object));
    temp.EmitSet(cg);
    EmitBinding(cg);
    temp.EmitGet(cg);
    cg.FreeLocalTemp(temp);
    cg.EmitFieldSet(valueField);
  }

  public override void EmitSet(CodeGenerator cg, ASTNode valueNode)
  {
    EmitBinding(cg);
    cg.EmitTypedNode(valueNode, type);
    cg.EmitSafeConversion(type, typeof(object));
    cg.EmitFieldSet(valueField);
  }

  public override void EmitSet(CodeGenerator cg, Slot valueSlot)
  {
    EmitBinding(cg);
    cg.EmitTypedSlot(valueSlot, type);
    cg.EmitSafeConversion(type, typeof(object));
    cg.EmitFieldSet(valueField);
  }

  public readonly string Name;
  
  void EmitBinding(CodeGenerator cg)
  {
    if(binding == null)
    {
      if(TopLevel.Current == null)
      {
        throw new CompileTimeException("A top-lvele environment is necessary to compile this code.");
      }
      binding = cg.GetCachedConstantSlot(TopLevel.Current.GetBinding(Name));
    }

    binding.EmitGet(cg);
  }

  readonly Type type;
  Slot binding;
  
  static readonly FieldInfo valueField = typeof(Binding).GetField("Value");
}
#endregion

} // namespace Scripting.Emit