using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Scripting.AST;
using Scripting.Emit;
using Scripting.Runtime;
using NetLisp.Emit;
using NetLisp.Runtime;

namespace NetLisp.AST
{

#region VariableSlotResolver
sealed class VariableSlotResolver : PrefixVisitor
{
  public VariableSlotResolver(DecoratorType type) : base(type) { }

  public override Stage Stage
  {
    get { return Stage.Decorate; }
  }

  protected override bool Visit(ASTNode node)
  {
    if(node is VariableNode) // if it's a variable node, set its slot if necessary
    {
      VariableNode var = (VariableNode)node;
      if(var.Slot == null)
      {
        var.Slot = FindSlot(var.Name);
      }
    }
    else if(node is LocalBindingNode) // if it's a binding node, add the bound variables to the array
    {
      LocalBindingNode bind = (LocalBindingNode)node;

      // first visit the initial values, since they'll be executed before the binding takes place
      foreach(LocalBindingNode.Binding binding in bind.Bindings)
      {
        if(binding.InitialValue != null) RecursiveVisit(binding.InitialValue);
      }

      // now add the new bindings
      foreach(LocalBindingNode.Binding binding in bind.Bindings)
      {
        Slot slot = DecoratorType == DecoratorType.Compiled ?
          new LocalProxySlot(binding.Name, binding.Type) : (Slot)new InterpretedLocalSlot(binding.Name, binding.Type);
        slots.Add(new KeyValuePair<string,Slot>(binding.Name, slot));

        binding.Variable.Slot = slot;
      }

      RecursiveVisit(bind.Body);
      
      // remove the bindings we added
      slots.RemoveRange(slots.Count-bind.Bindings.Count, bind.Bindings.Count);
      return false;
    }
    else if(node is FunctionNode)
    {
      FunctionNode func = (FunctionNode)node;

      // first visit the default parameter values, since they'll be executed outside the function body
      foreach(ParameterNode param in func.Parameters)
      {
        if(param.DefaultValue != null) RecursiveVisit(param.DefaultValue);
      }

      // now add the new bindings
      foreach(ParameterNode param in func.Parameters)
      {
        Slot slot = DecoratorType == DecoratorType.Compiled ?
          new ParameterSlot(param.Index, param.Type) : (Slot)new InterpretedLocalSlot(param.Name, param.Type);
        slots.Add(new KeyValuePair<string, Slot>(param.Name, slot));
      }
      
      RecursiveVisit(func.Body);

      // remove the bindings we added
      slots.RemoveRange(slots.Count-func.Parameters.Count, func.Parameters.Count);
      return false;
    }

    return true;
  }

  Slot FindSlot(string name)
  {
    for(int i=slots.Count-1; i>=0; i--) // see if this variable has been defined before
    {
      if(string.Equals(name, slots[i].Key, StringComparison.Ordinal))
      {
        return slots[i].Value;
      }
    }

    if(string.IsNullOrEmpty(name)) throw new ArgumentException("Variable name cannot be empty.");

    // if not, assume it's a global variable reference and insert a slot so we don't keep returning new slot objects
    Slot global = new TopLevelSlot(name);
    slots.Insert(0, new KeyValuePair<string,Slot>(name, global));
    return global;
  }

  List<KeyValuePair<string,Slot>> slots = new List<KeyValuePair<string,Slot>>();
}
#endregion

#region CallNode
public sealed class CallNode : ASTNode
{
  public CallNode(ASTNode function, ASTNode[] arguments) : base(true)
  {
    Children.Add(function);
    Children.Add(new ContainerNode(arguments));
  }

  public ASTNodeCollection Arguments
  {
    get { return Children[1].Children; }
  }

  public ASTNode Function
  {
    get { return Children[0]; }
  }

  public override Type ValueType
  {
    get { return typeof(object); }
  }

  public override void Emit(CodeGenerator cg, ref Type desiredType)
  {
    bool doTailCall = IsTail && desiredType == ValueType;  // TODO: don't emit the tailcall if we're inside a try/catch block
    cg.EmitTypedNode(Function, typeof(ICallable));
    cg.EmitObjectArray(Arguments);
    if(doTailCall) cg.ILG.Emit(OpCodes.Tailcall);
    cg.EmitCall(typeof(ICallable), "Call");
    if(!doTailCall) cg.EmitRuntimeConversion(ValueType, desiredType);
    TailReturn(cg);
  }

  public override object Evaluate()
  {
    return Ops.ConvertToCallable(Function.Evaluate()).Call(ASTNode.EvaluateNodes(Arguments));
  }
}
#endregion

#region IfNode
public class IfNode : ASTNode
{
  public IfNode(ASTNode condition, ASTNode ifTrue, ASTNode ifFalse) : base(true)
  {
    Children.Add(condition);
    Children.Add(ifTrue);
    if(ifFalse != null) Children.Add(ifFalse);
  }

  public ASTNode Condition
  {
    get { return Children[0]; }
    set { Children[0] = value; }
  }
  
  public ASTNode IfTrue
  {
    get { return Children[1]; }
    set { Children[1] = value; }
  }
  
  public ASTNode IfFalse
  {
    get { return Children.Count >= 3 ? Children[2] : null; }
    set
    {
      if(value == null && Children.Count >= 3)
      {
        Children.RemoveAt(2);
      }
      else if(value != null && Children.Count < 3)
      {
        Children.Add(value);
      }
      else
      {
        Children[2] = value;
      }
    }
  }

  public override Type ValueType
  {
    get { return IfFalse == null ? IfTrue.ValueType : CG.GetCommonBaseType(IfTrue.ValueType, IfFalse.ValueType); }
  }

  public override void Emit(CodeGenerator cg, ref Type desiredType)
  {
    cg.EmitTypedOperator(Operator.LogicalTruth, typeof(bool), Condition);

    Label end = cg.ILG.DefineLabel();
    Label falseLabel = desiredType == typeof(void) && IfFalse == null ? end : cg.ILG.DefineLabel();
    Type valueType   = IfFalse == null ? desiredType : ValueType;

    cg.ILG.Emit(OpCodes.Brfalse, falseLabel); // jump to the false branch (or end) if the condition is false
    cg.EmitTypedNode(IfTrue, valueType);      // emit the true branch

    // emit the false branch
    if(desiredType != typeof(void) || IfFalse != null)
    {
      cg.ILG.Emit(OpCodes.Br, end);
      cg.ILG.MarkLabel(falseLabel);
      if(IfFalse != null)
      {
        cg.EmitTypedNode(IfFalse, valueType);
      }
      else if(desiredType != typeof(void))
      {
        cg.EmitDefault(valueType);
      }
    }

    cg.ILG.MarkLabel(end);
    cg.EmitRuntimeConversion(valueType, desiredType);
    TailReturn(cg);
  }

  public override object Evaluate()
  {
    if((bool)Operator.LogicalTruth.Evaluate(Condition.Evaluate()))
    {
      return IfTrue.Evaluate();
    }
    else
    {
      return IfFalse == null ? null : IfFalse.Evaluate();
    }
  }
}
#endregion

#region ListNode
public sealed class ListNode : ASTNode
{
  public ListNode() : this((ASTNode[])null, null) { }
  public ListNode(params ASTNode[] items) : this(items, null) { }

  public ListNode(ASTNode[] items, ASTNode dotItem) : base(true)
  {
    if((items == null || items.Length == 0) && dotItem != null)
    {
      throw new ArgumentException("If there is a dot item, there must be list items.");
    }

    Children.Add(new ContainerNode(items));
    if(dotItem != null) Children.Add(dotItem);
  }
  
  public ASTNodeCollection ListItems
  {
    get { return Children[0].Children; }
  }
  
  public ASTNode DotItem
  {
    get { return Children.Count >= 2 ? Children[1] : null; }
  }

  public override Type ValueType
  {
    get { return typeof(Pair); }
  }

  public override void Emit(CodeGenerator cg, ref Type desiredType)
  {
    if(ListItems.Count == 0)
    {
      cg.EmitNull();
      cg.EmitRuntimeConversion(null, desiredType);
    }
    else if(desiredType == typeof(void))
    {
      cg.EmitVoids(ListItems);
      if(DotItem != null) cg.EmitVoid(DotItem);
    }
    else
    {
      ConstructorInfo cons = typeof(Pair).GetConstructor(new Type[] { typeof(object), typeof(object) });

      foreach(ASTNode node in ListItems) cg.EmitTypedNode(node, typeof(object));

      if(DotItem == null) cg.EmitNull();
      else cg.EmitTypedNode(DotItem, typeof(object));

      for(int i=0; i<ListItems.Count; i++) cg.EmitNew(cons);

      cg.EmitRuntimeConversion(typeof(Pair), desiredType);
    }
    
    TailReturn(cg);
  }

  public override object Evaluate()
  {
    object obj = DotItem == null ? null : DotItem.Evaluate();
    for(int i=ListItems.Count-1; i>=0; i--)
    {
      obj = new Pair(ListItems[i].Evaluate(), obj);
    }
    return obj;
  }
}
#endregion

#region LocalBindingNode
public sealed class LocalBindingNode : ASTNode
{
  public LocalBindingNode(string name, ASTNode init, ASTNode body) 
    : this(new string[] { name }, new ASTNode[] { init }, null, body) { }
  public LocalBindingNode(string name, ASTNode init, Type type, ASTNode body)
    : this(new string[] { name }, new ASTNode[] { init }, new Type[] { type }, body) { }
  public LocalBindingNode(string[] names, ASTNode[] inits, ASTNode body) : this(names, inits, null, body) { }

  public LocalBindingNode(string[] names, ASTNode[] inits, Type[] types, ASTNode body) : base(true)
  {
    if(names == null || body == null) throw new ArgumentNullException();
    if(inits != null && inits.Length != names.Length || types != null && types.Length != names.Length)
    {
      throw new ArgumentException("Binding array lengths do not match.");
    }

    Children.Add(new ContainerNode());
    Children.Add(body);
    
    for(int i=0; i<names.Length; i++)
    {
      Bindings.Add(new Binding(names[i], inits == null ? null : inits[i], types == null ? null : types[i]));
    }
  }

  #region Binding
  public sealed class Binding : NonExecutableNode
  {
    public Binding(string name, ASTNode initialValue, Type type) : base(true)
    {
      Children.Add(new VariableNode(name, new TopLevelSlot(name, type == null ? typeof(object) : type)));
      if(initialValue != null) Children.Add(initialValue);
    }

    public ASTNode InitialValue
    {
      get { return Children.Count >= 2 ? Children[1] : null; }
    }
    
    public string Name
    {
      get { return Variable.Name; }
    }

    public Type Type
    {
      get { return Variable.ValueType; }
    }

    public VariableNode Variable
    {
      get { return (VariableNode)Children[0]; }
    }
  }
  #endregion

  public ASTNodeCollection Bindings
  {
    get { return Children[0].Children; }
  }
  
  public ASTNode Body
  {
    get { return Children[1]; }
  }

  public override Type ValueType
  {
    get { return Body.ValueType; }
  }

  public override void Emit(CodeGenerator cg, ref Type desiredType)
  {
    cg.BeginScope();
    foreach(Binding binding in Bindings)
    {
      if(binding.InitialValue != null)
      {
        binding.Variable.EmitSet(cg, binding.InitialValue);
      }
      else
      {
        cg.EmitDefault(binding.Type);
        binding.Variable.EmitSet(cg, binding.Type);
      }
    }
    
    Body.Emit(cg, ref desiredType);
    cg.EndScope();
  }

  public override object Evaluate()
  {
    InterpreterEnvironment env = InterpreterEnvironment.PushNew();
    try
    {
      foreach(Binding binding in Bindings)
      {
        object initialValue;
        if(binding.InitialValue == null)
        {
          if(binding.Type.IsValueType)
          {
            initialValue = Activator.CreateInstance(binding.Type);
          }
          else
          {
            initialValue = null;
          }
        }
        else
        {
          initialValue = binding.InitialValue == null ? null : binding.InitialValue.Evaluate();
          if(binding.Type != typeof(object))
          {
            initialValue = Ops.ConvertTo(initialValue, binding.Type);
          }
        }

        env.Bind(binding.Name, initialValue);
      }

      return Body.Evaluate();
    }
    finally { InterpreterEnvironment.Pop(); }
  }

  public override void MarkTail(bool tail)
  {
    IsTail = tail;
    foreach(ASTNode node in Bindings) node.MarkTail(false);
    Body.MarkTail(tail);
  }
}
#endregion

#region SymbolNode
public sealed class SymbolNode : LiteralNode
{
  public SymbolNode(string name) : base(Symbol.Get(name), true) { }
}
#endregion

#region VectorNode
public sealed class VectorNode : ASTNode
{
  public VectorNode() : this(null) { }
  public VectorNode(params ASTNode[] items) : base(true)
  {
    if(items != null)
    {
      Children.AddRange(items);
    }
  }

  public override Type ValueType
  {
    get { return GetElementType().MakeArrayType(); }
  }

  public override void Emit(CodeGenerator cg, ref Type desiredType)
  {
    if(desiredType == typeof(void))
    {
      cg.EmitVoids(Children);
    }
    else
    {
      cg.EmitArray(Children, GetElementType());
    }

    TailReturn(cg);
  }

  public override object Evaluate()
  {
    Type type = GetElementType();
    Array array = Array.CreateInstance(type, Children.Count);
    for(int i=0; i<array.Length; i++)
    {
      array.SetValue(Ops.ConvertTo(Children[i].Evaluate(), type), i);
    }
    return array;
  }

  Type GetElementType()
  {
    if(elementType == null)
    {
      elementType = CG.GetCommonBaseType(Children);
    }
    return elementType;
  }

  Type elementType;
}
#endregion

} // namespace NetLisp.AST