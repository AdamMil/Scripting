using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Scripting;
using Scripting.AST;
using Scripting.Emit;
using Scripting.Runtime;
using NetLisp.Emit;
using NetLisp.Runtime;

namespace NetLisp.AST
{

#region LispTypes
public static class LispTypes
{
  public static readonly TypeWrapper Pair = TypeWrapper.Get(typeof(Pair));
}
#endregion

#region CoreSemanticChecker
public class CoreSemanticChecker : PrefixVisitor
{
  public CoreSemanticChecker(DecoratorType type) : base(type) { }

  public override Stage Stage
  {
    get { return Stage.Decorate; }
  }

  public override void Process(ref ASTNode rootNode)
  {
    base.Process(ref rootNode);
  }

  protected override bool Visit(ASTNode node)
  {
    node.CheckSemantics();
    return true;
  }
}
#endregion

#region VariableSlotResolver
public class VariableSlotResolver : PrefixVisitor
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
      Binding binding = bindings[UpdateBinding((VariableNode)node)].Value;
      binding.Usage |= Usage.Read; // mark that this variable has been read
      // if it hasn't been written or initialized, mark that it was read before it had a value
      if((binding.Usage & (Usage.Initialized|Usage.Written)) == 0)
      {
        binding.Usage |= Usage.ReadBeforeInitialized;
      }
    }
    else if(node is AssignNode)
    {
      AssignNode assign = (AssignNode)node;
      VariableNode var = assign.LHS as VariableNode;

      if(var != null) // if assigning to a variable, mark the variable as having been written to
      {
        RecursiveVisit(assign.RHS); // visit the right side first to so we can mark ReadBeforeInitialized for "var a=a"
        bindings[UpdateBinding(var)].Value.Usage |= Usage.Written;
        return false;
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
        Slot slot = IsCompiled ? new LocalProxySlot(binding.Name, binding.Type)
                               : (Slot)new InterpretedLocalSlot(binding.Name, binding.Type);
        bindings.Add(new KeyValuePair<string,Binding>(binding.Name, new Binding(slot, false)));
        UpdateBinding(binding.Variable);
      }

      RecursiveVisit(bind.Body); // process the body of the 'let'
      
      // then remove the bindings we added
      bindings.RemoveRange(bindings.Count-bind.Bindings.Count, bind.Bindings.Count);
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

      // in compiled mode, we need to keep track of where the function declarations start, so we can tell if a variable
      // is used outside of its function and needs to be included in a closure.
      if(IsCompiled)
      {
        if(functions == null) functions = new List<Function>();
        functions.Add(new Function(bindings.Count));
      }

      // now add the new bindings
      foreach(ParameterNode param in func.Parameters)
      {
        Slot slot = IsCompiled ? new ParameterSlot(param.Index, param.Type)
                               : (Slot)new InterpretedLocalSlot(param.Name, param.Type);
        bindings.Add(new KeyValuePair<string,Binding>(param.Name, new Binding(slot, true)));
      }

      RecursiveVisit(func.Body); // process the body of the function

      if(IsCompiled) // in compiled mode, we need to tell the function what closures it has so it can create a
      {              // closure class if necessary. we also need to calculate closure depth for each reference.
        Function function = functions[functions.Count-1];
        functions.RemoveAt(functions.Count-1);

        if(function.Closures != null) // the function defines a closure
        {
          System.Collections.Specialized.ListDictionary depths = new System.Collections.Specialized.ListDictionary();
          List<string> names = new List<string>();

          func.Closures = new ClosureSlot[function.Closures.Count];
          for(int i=0; i<function.Closures.Count; i++)
          {
            Binding binding     = function.Closures[i];
            ClosureSlot closure = (ClosureSlot)binding.Slot;
            func.Closures[i]    = closure;

            // uniquify slot names if necessary
            if(names.Contains(closure.Name))
            {
              int suffix = 2;
              while(!names.Contains(closure.Name + suffix))
              {
                suffix++;
              }
              closure.Name += suffix;
            }
            names.Add(closure.Name);

            // for each reference to the binding, calculate the depth. if the variables are defined within the current
            // function, they'll have a depth of zero. in nested functions, they'll have a depth of one, although any
            // additional closures created by nested functions increases the depth further by one.
            foreach(VariableNode var in binding.References)
            {
              FunctionNode referencingFunction = var.GetAncestor<FunctionNode>();
              if(referencingFunction != func) // if the variable defining this reference is not the current function,
              {                               // the slot does not have a depth of zero. calculate the depth.
                FunctionNode funcNode = referencingFunction;
                int depth = 1;
                while(funcNode != node)
                {
                  if(funcNode.CreatesClosure) depth++;
                  funcNode = funcNode.GetAncestor<FunctionNode>();
                }

                // create a new slot with the right depth if necessary, or retrieve a cached one.
                ClosureSlot slot = (ClosureSlot)depths[depth];
                if(slot == null) depths[depth] = slot = closure.CloneWithDepth(depth);
                var.Slot = slot;
              }
            }
            
            depths.Clear(); // clear the dictionary of cached slots
          }
        }
      }

      // then remove the bindings we added
      bindings.RemoveRange(bindings.Count-func.Parameters.Count, func.Parameters.Count);
      return false;
    }

    return true;
  }

  sealed class Binding
  {
    public Binding(Slot slot, bool preInitialized)
    {
      this.Slot  = slot;
      this.Usage = preInitialized ? Usage.Initialized : Usage.None;
    }

    public void AddReference(VariableNode var)
    {
      if(References == null) References = new List<VariableNode>();
      References.Add(var);
    }

    public Slot Slot;
    public List<VariableNode> References;
    public Usage Usage;
    public bool  InClosure;
  }

  sealed class Function
  {
    public Function(int bindingStart)
    {
      BindingStart = bindingStart;
    }

    public void AddClosure(Binding closureBinding)
    {
      if(Closures == null) Closures = new List<Binding>();
      Closures.Add(closureBinding);
    }

    public List<Binding> Closures;
    public int BindingStart;
  }

  /// <summary>This enum describes the way in which a <see cref="Binding"/> has been used.</summary>
  [Flags]
  enum Usage
  {
    /// <summary>The binding was unused.</summary>
    None=0,
    /// <summary>The binding was read from during its lifespan.</summary>
    Read=1,
    /// <summary>The binding was assigned to during its lifespan.</summary>
    Written=2,
    /// <summary>The binding was initialized from the very beginning of its lifespan. A function parameter is an
    /// example of this kind of binding.
    /// </summary>
    Initialized=4,
    /// <summary>The binding was read before it was written to or initialized.</summary>
    ReadBeforeInitialized=8
  }

  int FindBinding(VariableNode var)
  {
    for(int i=bindings.Count-1; i>=0; i--)
    {
      if(string.Equals(var.Name, bindings[i].Key, StringComparison.Ordinal))
      {
        return i;
      }
    }
    return -1;
  }

  Function FindFunction(int index)
  {
    for(int i=functions.Count-1; i>=0; i--)
    {
      if(index >= functions[i].BindingStart) return functions[i];
    }
    return null;
  }

  int UpdateBinding(VariableNode var)
  {
    int index = FindBinding(var); // see if this variable has been defined before
    if(index != -1)
    {
      Binding binding = bindings[index].Value;

      if(var.Slot == null) var.Slot = binding.Slot; // update the variable slot to use the binding slot
      binding.AddReference(var);

      // if the slot is defined in a function other than the current function, and it's not already in a closure,
      // we need to make it into a closure slot. this doesn't take into account closure depth. that's done after
      // the processing for the function is complete.
      if(!binding.InClosure && functions != null && functions.Count > 1 && !(binding.Slot is TopLevelSlot) &&
         index >= functions[0].BindingStart && index < functions[functions.Count-1].BindingStart)
      {
        Slot initializeSlot = (binding.Usage & Usage.Initialized) == 0 ? null : binding.Slot;

        ClosureSlot slot = new ClosureSlot(var.Name, binding.Slot.Type, initializeSlot);
        foreach(VariableNode node in binding.References) // update all the references to the binding
        {
          node.Slot = slot;
        }

        binding.Slot      = slot;
        binding.InClosure = true; // mark that this binding is in a closure
        
        FindFunction(index).AddClosure(binding); // add this closure to the function definition
      }
      
      return index;
    }

    if(string.IsNullOrEmpty(var.Name)) throw new ArgumentException("Variable name cannot be empty.");

    // if not, assume it's a global variable reference and insert a binding for it.
    var.Slot = new TopLevelSlot(var.Name);

    if(functions != null) // since we'll be inserting a value, we may need to update the function binding indices
    {
      foreach(Function func in functions)
      {
        func.BindingStart++;
      }
    }

    Binding globalBinding = new Binding(var.Slot, false);
    globalBinding.AddReference(var);
    bindings.Insert(0, new KeyValuePair<string,Binding>(var.Name, globalBinding));
    return 0;
  }

  List<KeyValuePair<string,Binding>> bindings = new List<KeyValuePair<string,Binding>>();
  List<Function> functions;
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

  public override ITypeInfo ValueType
  {
    get
    {
      VariableNode functionVar = Function as VariableNode;
      if(functionVar != null && ShouldInline(functionVar.Name))
      {
        return GetBuiltinFunctionType(functionVar.Name);
      }
      else
      {
        return TypeWrapper.Unknown;
      }
    }
  }

  public override void CheckSemantics()
  {
    VariableNode functionVar = Function as VariableNode;
    if(functionVar != null && ShouldInline(functionVar.Name))
    {
      GetBuiltinFunctionOperator(functionVar.Name).CheckSemantics(ContextType, Arguments);
    }
  }

  public override ITypeInfo Emit(CodeGenerator cg)
  {
    VariableNode functionVar = Function as VariableNode;
    if(functionVar != null && ShouldInline(functionVar.Name))
    {
      return EmitBuiltinFunction(cg, functionVar.Name);
    }
    else
    {
      bool doTailCall = IsTail && ContextType == ValueType;
      cg.EmitTypedNode(Function, TypeWrapper.ICallable);
      cg.EmitObjectArray(Arguments);
      if(doTailCall) cg.ILG.Emit(OpCodes.Tailcall);
      cg.EmitCall(typeof(ICallable), "Call");
      return TailReturn(cg, ValueType);
    }
  }

  public override object Evaluate()
  {
    return Ops.ConvertToICallable(Function.Evaluate()).Call(ASTNode.EvaluateNodes(Arguments));
  }

  public override void SetValueContext(ITypeInfo desiredType)
  {
    base.SetValueContext(desiredType);

    Function.SetValueContext(TypeWrapper.ICallable);

    VariableNode functionVar = Function as VariableNode;
    if(functionVar != null && ShouldInline(functionVar.Name))
    {
      GetBuiltinFunctionOperator(functionVar.Name).SetValueContext(desiredType, Arguments);
    }
    else
    {
      foreach(ASTNode node in Arguments) node.SetValueContext(TypeWrapper.Object);
    }
  }

  ITypeInfo EmitBuiltinFunction(CodeGenerator cg, string name)
  {
    return TailReturn(cg, GetBuiltinFunctionOperator(name).Emit(cg, ContextType, Arguments));
  }

  ITypeInfo GetBuiltinFunctionType(string name)
  {
    return GetBuiltinFunctionOperator(name).GetValueType(Arguments);
  }

  NumericOperator GetBuiltinFunctionOperator(string name)
  {
    switch(name)
    {
      case "+": return Operator.Add;
      case "-": return Operator.Subtract;
      case "*": return Operator.Multiply;
      case "/": return Operator.Divide;
      case "modulo": return Operator.Add;
      default: throw new NotImplementedException();
    }
  }

  static bool IsBuiltinFunction(string name)
  {
    switch(name)
    {
      case "+": case "-": case "*": case "/": case "modulo": return true;
      default: return false;
    }
  }

  static bool IsOverriddenInThisScope(string name)
  {
    return false; // TODO: Implement this
  }

  static bool ShouldInline(string name)
  {
    return ((NetLispCompilerState)CompilerState.Current).OptimisticOperatorInlining &&
           IsBuiltinFunction(name) && !IsOverriddenInThisScope(name);
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

  public override ITypeInfo ValueType
  {
    get { return LispTypes.Pair; }
  }

  public override ITypeInfo Emit(CodeGenerator cg)
  {
    ITypeInfo typeOnStack;
    if(ListItems.Count == 0)
    {
      cg.EmitNull();
      typeOnStack = null;
    }
    else if(IsVoid(ContextType))
    {
      cg.EmitVoids(ListItems);
      if(DotItem != null) cg.EmitVoid(DotItem);
      typeOnStack = TypeWrapper.Void;
    }
    else
    {
      ConstructorInfo cons = typeof(Pair).GetConstructor(new Type[] { typeof(object), typeof(object) });

      // TODO: this method pushes every item into the stack. should we have a less stack-heavy method for long lists?
      foreach(ASTNode node in ListItems) cg.EmitTypedNode(node, TypeWrapper.Object);

      if(DotItem == null) cg.EmitNull();
      else cg.EmitTypedNode(DotItem, TypeWrapper.Object);

      for(int i=0; i<ListItems.Count; i++) cg.EmitNew(cons);
      typeOnStack = LispTypes.Pair;
    }
    
    return TailReturn(cg, typeOnStack);
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

  public override void SetValueContext(ITypeInfo desiredType)
  {
    base.SetValueContext(desiredType);
    foreach(ASTNode node in ListItems) node.SetValueContext(TypeWrapper.Object);
  }
}
#endregion

#region LocalBindingNode
public sealed class LocalBindingNode : ASTNode
{
  public LocalBindingNode(string name, ASTNode init, ASTNode body) 
    : this(new string[] { name }, new ASTNode[] { init }, null, body) { }
  public LocalBindingNode(string name, ASTNode init, ITypeInfo type, ASTNode body)
    : this(new string[] { name }, new ASTNode[] { init }, new ITypeInfo[] { type }, body) { }
  public LocalBindingNode(string[] names, ASTNode[] inits, ASTNode body) : this(names, inits, null, body) { }

  public LocalBindingNode(string[] names, ASTNode[] inits, ITypeInfo[] types, ASTNode body) : base(true)
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
    public Binding(string name, ASTNode initialValue, ITypeInfo type) : base(true)
    {
      if(name == null) throw new ArgumentNullException();
      Children.Add(new VariableNode(name));
      if(initialValue != null) Children.Add(initialValue);
      this.type = type == null ? TypeWrapper.Object : type;
    }

    public ASTNode InitialValue
    {
      get { return Children.Count >= 2 ? Children[1] : null; }
    }
    
    public string Name
    {
      get { return Variable.Name; }
    }

    public ITypeInfo Type
    {
      get { return type; }
    }

    public VariableNode Variable
    {
      get { return (VariableNode)Children[0]; }
    }

    public override void SetValueContext(ITypeInfo desiredType)
    {
      base.SetValueContext(desiredType);
      Variable.SetValueContext(TypeWrapper.Unknown); // the variable will never be retrieved, only set
      if(InitialValue != null) InitialValue.SetValueContext(Variable.ValueType);
    }

    readonly ITypeInfo type;
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

  public override ITypeInfo ValueType
  {
    get { return Body.ValueType; }
  }

  public override ITypeInfo Emit(CodeGenerator cg)
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
    
    ITypeInfo emittedType = Body.Emit(cg);
    cg.EndScope();
    return emittedType;
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
            initialValue = Activator.CreateInstance(binding.Type.DotNetType);
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
            initialValue = Ops.ConvertTo(initialValue, binding.Type.DotNetType);
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

  public override void SetValueContext(ITypeInfo desiredType)
  {
    base.SetValueContext(desiredType);
    foreach(ASTNode node in Bindings) node.SetValueContext(TypeWrapper.Unknown);
    Body.SetValueContext(desiredType);
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

  public override ITypeInfo ValueType
  {
    get { return ElementType.MakeArrayType(); }
  }

  public override ITypeInfo Emit(CodeGenerator cg)
  {
    if(IsVoid(ContextType))
    {
      cg.EmitVoids(Children);
      return TailReturn(cg);
    }
    else
    {
      cg.EmitArray(Children, ElementType);
      return TailReturn(cg, ValueType);
    }
  }

  public override object Evaluate()
  {
    Type type = ElementType.DotNetType;
    Array array = Array.CreateInstance(type, Children.Count);
    for(int i=0; i<array.Length; i++)
    {
      array.SetValue(Ops.ConvertTo(Children[i].Evaluate(), type), i);
    }
    return array;
  }

  public override void SetValueContext(ITypeInfo desiredType)
  {
    base.SetValueContext(desiredType);
    foreach(ASTNode node in Children) node.SetValueContext(ElementType);
  }

  ITypeInfo ElementType
  {
    get
    {
      if(elementType == null)
      {
        elementType = CG.GetCommonBaseType(Children);
      }
      return elementType;
    }
  }

  ITypeInfo elementType;
}
#endregion

} // namespace NetLisp.AST