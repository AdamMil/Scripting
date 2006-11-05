using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;
using Scripting.Emit;
using Scripting.Runtime;

namespace Scripting.AST
{

#region Index
public sealed class Index
{
  public long Next { get { lock(this) return index++; } }
  long index;
}
#endregion

#region ASTNodeCollection
/// <summary>Represents a collection of <see cref="ASTNode"/> objects.</summary>
public class ASTNodeCollection : Collection<ASTNode>
{
  internal ASTNodeCollection(ASTNode parent)
  {
    this.parent = parent;
  }

  /// <summary>Adds a list of items to the collection.</summary>
  public void AddRange(params ASTNode[] items)
  {
    AddRange((IEnumerable<ASTNode>)items);
  }

  /// <summary>Adds a list of items to the collection.</summary>
  public void AddRange(IEnumerable<ASTNode> items)
  {
    if(items == null) throw new ArgumentNullException();

    foreach(ASTNode item in items)
    {
      Add(item);
    }
  }

  /// <summary>Copies nodes into an array.</summary>
  /// <param name="srcIndex">The starting position within the collection.</param>
  /// <param name="destArray">The array into which the nodes will be copied.</param>
  /// <param name="destIndex">The starting position within <paramref name="destArray"/>.</param>
  /// <param name="count">The number of nodes to copy.</param>
  public void CopyTo(int srcIndex, ASTNode[] destArray, int destIndex, int count)
  {
    for(int i=0; i<count; i++)
    {
      destArray[destIndex+i] = this[srcIndex+i];
    }
  }

  /// <summary>Replaces a given item with another item.</summary>
  public void Replace(ASTNode item, ASTNode replacement)
  {
    if(item == null || replacement == null) throw new ArgumentNullException();
    if(parent == null || item.ParentNode != parent) throw new ArgumentException("Item not found in collection.");
    this[item.Index] = replacement;
  }

  protected override void ClearItems()
  {
    foreach(ASTNode node in this)
    {
      node.parent = node.previousNode = node.nextNode = null;
    }

    base.ClearItems();
  }

  protected override void InsertItem(int index, ASTNode item)
  {
    AssertWriteable();
    AssertNotNull(item);
    
    if(item.ParentNode != null)
    {
      throw new ArgumentException("This node already belongs to a parent.");
    }

    base.InsertItem(index, item);
    AfterAddingItem(index);

    for(int i=index+1; i<Count; i++) // increment indices for nodes after the inserted item
    {
      this[i].index++;
    }
  }

  protected override void RemoveItem(int index)
  {
    BeforeRemovingItem(index);
    base.RemoveItem(index);

    for(int i=index; i<Count; i++) // decrement indices for nodes after the inserted item
    {
      this[i].index--;
    }
  }

  protected override void SetItem(int index, ASTNode item)
  {
    AssertNotNull(item);
    BeforeRemovingItem(index);
    base.SetItem(index, item);
    AfterAddingItem(index);
  }

  void AssertNotNull(ASTNode item)
  {
    if(item == null) throw new ArgumentNullException("Null nodes cannot be added as children.");
  }

  void AssertWriteable()
  {
    if(parent == null)
    {
      throw new InvalidOperationException("This collection is read-only because the owning node is a leaf node.");
    }
  }

  void AfterAddingItem(int index)
  {
    ASTNode item = this[index];
    item.index  = index;
    item.parent = parent;

    if(index != 0)
    {
      this[index-1].nextNode = item;
      item.previousNode = this[index-1];
    }
    if(index < Count-1)
    {
      this[index+1].previousNode = item;
      item.nextNode = this[index+1];
    }
  }
  
  void BeforeRemovingItem(int index)
  {
    ASTNode item = this[index];
    item.parent = null;

    if(index != 0)
    {
      this[index-1].nextNode = index == Count-1 ? null : this[index+1];
    }
    if(index != Count-1)
    {
      this[index+1].previousNode = index == 0 ? null : this[index-1];
    }
  }

  readonly ASTNode parent;
}
#endregion

#region ASTNode
public abstract class ASTNode
{
  protected ASTNode(bool isContainerNode)
  {
    children = isContainerNode ? new ASTNodeCollection(this) : EmptyNodeCollection;
  }

  /// <summary>Gets a read-only collection of the attributes that have been added to this node.</summary>
  public ReadOnlyCollection<Attribute> Attributes
  {
    get { return attributes == null ? EmptyAttributeCollection : new ReadOnlyCollection<Attribute>(attributes); }
  }

  /// <summary>Gets a collection of this node's child nodes.</summary>
  public ASTNodeCollection Children
  {
    get { return children; }
  }

  /// <summary>Gets this node's index within its parent's <see cref="Children"/> collection.</summary>
  public int Index
  {
    get
    {
      if(parent == null) throw new InvalidOperationException("This node is not part of a node collection.");
      return index;
    }
  }

  /// <summary>Gets this node's first child node.</summary>
  public ASTNode FirstChild
  {
    get { return Children.Count == 0 ? null : Children[0]; }
  }
  
  /// <summary>Gets this node's last child node.</summary>
  public ASTNode LastChild
  {
    get { return Children.Count == 0 ? null : Children[Children.Count-1]; }
  }

  /// <summary>Gets this node's next sibling node. This is the node after this one in the parent's
  /// <see cref="Children"/> collection.
  /// </summary>
  public ASTNode NextSibling
  {
    get { return nextNode; }
  }

  /// <summary>Gets this node's previous sibling node. This is the node before this one in the parent's
  /// <see cref="Children"/> collection.</summary>
  public ASTNode PreviousSibling
  {
    get { return previousNode; }
  }

  /// <summary>Gets this node's parent node.</summary>
  public ASTNode ParentNode
  {
    get { return parent; }
  }

  /// <summary>Gets the root node of the tree.</summary>
  public ASTNode RootNode
  {
    get
    {
      ASTNode node = this;
      while(node.parent != null) node = node.parent;
      return node;
    }
  }

  /// <summary>Gets or sets whether this node's value is constant and can be safely evaluated at compilet time.</summary>
  public bool IsConstant
  {
    get { return Is(Flag.Constant); }
    set { Set(Flag.Constant, value); }
  }

  /// <summary>Gets or sets whether this node is contained within an exception handling block.</summary>
  public bool IsInTry
  {
    get { return Is(Flag.InTry); }
    set { Set(Flag.InTry, value); }
  }

  /// <summary>Gets or sets whether this node is a tail of it's function, meaning that its value will be returned
  /// from the function.
  /// </summary>
  public bool IsTail
  {
    get { return Is(Flag.Tail); }
    set { Set(Flag.Tail, value); }
  }

  /// <summary>Gets the natural type of this node's value when emitted. If the value type is <see cref="Void"/>, this
  /// node normally emits nothing. If the value type is null, this node normally emits null. A node may emit a type
  /// other than its value type depending on the <c>desiredType</c> parameter passed to the <see cref="Emit"/> method.
  /// </summary>
  public abstract Type ValueType { get; }

  public void AddAttribute(Attribute attribute)
  {
    if(attribute == null) throw new ArgumentNullException();
    if(attributes == null)
    {
      attributes = new List<Attribute>();
    }
    attributes.Add(attribute);
  }

  public Attribute[] GetAttributes(Type attributeType)
  {
    if(attributeType == null) throw new ArgumentNullException();

    List<Attribute> list = new List<Attribute>();
    if(attributes != null)
    {
      foreach(Attribute attribute in attributes)
      {
        if(attributeType.IsAssignableFrom(attribute.GetType()))
        {
          list.Add(attribute);
        }
      }
    }
    return list.ToArray();
  }
  
  public bool HasAttribute(Type attributeType)
  {
    return GetAttributes(attributeType).Length > 0;
  }

  public virtual object Evaluate()
  {
    throw new NotSupportedException();
  }

  public abstract void Emit(CodeGenerator cg, ref Type desiredType);

  /// <summary>Marks this node and its child nodes based on the <paramref name="tail"/> parameter.</summary>
  /// <param name="tail">If true, this node's value will be returned from the function in which it's emitted.</param>
  /// <remarks>The default implementation sets <see cref="IsTail"/> to the value of <paramref name="tail"/> and calls
  /// <see cref="MarkTail"/> with the <c>false</c> value on all the children. Derived classes can override this method
  /// if child nodes should be handled differently. For instance, if a child node will be emitted as the value of this
  /// node, the child node should be marked as a tail.
  /// </remarks>
  public virtual void MarkTail(bool tail)
  {
    IsTail = tail;

    foreach(ASTNode node in Children)
    {
      node.MarkTail(false);
    }
  }

  /// <summary>If this node is a tail node (<see cref="IsTail"/> is true), this method will emit a proper return from
  /// the function.
  /// </summary>
  /// <remarks>A call to this method should be placed in <see cref="Emit"/> implementations at the end of code paths
  /// where a child marked as a tail node might not have been emitted.
  /// </remarks>
  protected void TailReturn(CodeGenerator cg)
  {
    if(IsTail)
    {
      if(!IsInTry)
      {
        cg.EmitReturn();
      }
      else
      {
        throw new NotImplementedException();
      }
    }
  }

  /// <summary>Returns true if all nodes are constant.</summary>
  public static bool AreConstant(params ASTNode[] nodes)
  {
    foreach(ASTNode node in nodes)
    {
      if(!node.IsConstant) return false;
    }
    return true;
  }
  
  /// <summary>Evaluates the given nodes, converting each one to the element type. Returns an array containing the
  /// resulting values.
  /// </summary>
  public static Array EvaluateNodes(ASTNode[] nodes, Type elementType)
  {
    Array array = Array.CreateInstance(elementType, nodes.Length);
    for(int i=0; i<nodes.Length; i++)
    {
      array.SetValue(Ops.ConvertTo(nodes[i].Evaluate(), elementType), i);
    }
    return array;
  }

  /// <summary>Gets the <see cref="ValueType"/> of each node passed and returns the types in an array.</summary>
  public static Type[] GetNodeTypes(ASTNode[] nodes)
  {
    Type[] types = new Type[nodes.Length];
    for(int i=0; i<types.Length; i++)
    {
      types[i] = nodes[i].ValueType;
    }
    return types;
  }

  internal ASTNode parent, previousNode, nextNode;
  internal int index;

  [Flags]
  enum Flag
  {
    /// <summary>Indicates whether the node's value is constant and can be evaluated at compile-time.</summary>
    Constant = 1,
    /// <summary>Indicates whether the node is a tail node, meaning that the result of its evaluation will be returned
    /// from the function.
    /// </summary>
    Tail = 2,
    /// <summary>Indicates whether this node is contained within an exception handling block.</summary>
    InTry = 4,
  }

  bool Is(Flag flag)
  {
    return (flags & flag) != 0;
  }
  
  void Set(Flag flag, bool on)
  {
    if(on) flags |= flag;
    else flags &= ~flag;
  }

  ASTNodeCollection children;
  List<Attribute> attributes;
  Flag flags;

  static readonly ASTNodeCollection EmptyNodeCollection = new ASTNodeCollection(null);
  static readonly ReadOnlyCollection<Attribute> EmptyAttributeCollection =
    new ReadOnlyCollection<Attribute>(new Attribute[0]);
}
#endregion

#region AssignableNode
public abstract class AssignableNode : ASTNode
{
  public AssignableNode(bool isContainerNode) : base(isContainerNode) { }
  public abstract void EvaluateSet(object newValue);
  public abstract void EmitSet(CodeGenerator cg, ASTNode valueNode);
  public abstract void EmitSet(CodeGenerator cg, Type typeOnStack);
}
#endregion

#region AssignNode
public class AssignNode : ASTNode
{
  public AssignNode(AssignableNode lhs, ASTNode rhs) : base(true)
  {
    if(lhs == null || rhs == null) throw new ArgumentNullException();
    Children.AddRange(lhs, rhs);
  }

  public AssignableNode LHS
  {
    get { return (AssignableNode)Children[0]; }
  }

  public ASTNode RHS
  {
    get { return Children[1]; }
  }

  public override Type ValueType
  {
    get { return RHS.ValueType; }
  }

  public override void Emit(CodeGenerator cg, ref Type desiredType)
  {
    if(desiredType == typeof(void))
    {
      LHS.EmitSet(cg, RHS);
    }
    else
    {
      Type rhsType = LHS.ValueType;
      RHS.Emit(cg, ref rhsType);
      cg.EmitDup();
      LHS.EmitSet(cg, rhsType);
      cg.EmitSafeConversion(rhsType, desiredType);
    }

    TailReturn(cg);
  }

  public override object Evaluate()
  {
    object newValue = RHS.Evaluate();
    LHS.EvaluateSet(newValue);
    return newValue;
  }
}
#endregion

#region BlockNode
public class BlockNode : ASTNode
{
  public BlockNode() : base(true) { }
  public BlockNode(params ASTNode[] nodes) : base(true)
  {
    Children.AddRange(nodes);
  }

  public override Type ValueType
  {
    get
    {
      ASTNode lastChild = LastChild;
      return lastChild == null ? typeof(void) : LastChild.ValueType;
    }
  }

  public override void Emit(CodeGenerator cg, ref Type desiredType)
  {
    if(Children.Count != 0)
    {
      for(int i=0; i<Children.Count-1; i++)
      {
        cg.EmitVoid(Children[i]);
      }
      
      LastChild.Emit(cg, ref desiredType);
    }
    else if(desiredType != typeof(void))
    {
      cg.EmitDefault(desiredType);
      TailReturn(cg);
    }
  }

  public override object Evaluate()
  {
    object value = null;
    foreach(ASTNode node in Children)
    {
      value = node.Evaluate();
    }
    return value;
  }

  public override void MarkTail(bool tail)
  {
    IsTail = tail;

    if(Children.Count != 0) // mark all children false except the last one, which becomes the same as us
    {
      for(int i=0; i<Children.Count-1; i++)
      {
        Children[i].MarkTail(false);
      }
      
      LastChild.MarkTail(tail);
    }
  }
}
#endregion

#region FunctionBaseNode
public abstract class FunctionNode : ASTNode
{
  public FunctionNode(string name, Type returnType, ParameterNode[] parameters, ASTNode body) : base(true)
  {
    if(parameters == null || body == null) throw new ArgumentNullException();
    ParameterNode.Validate(parameters, out RequiredParameterCount, out OptionalParameterCount,
                           out HasListParameter, out HasDictParameter);

    this.Name       = name;
    this.returnType = returnType;
    
    Children.Add(body);
    Children.Add(new BlockNode(parameters));
  }
  
  public ASTNode Body
  {
    get { return Children[0]; }
    set { Children[0] = value; }
  }
  
  public ASTNodeCollection Parameters
  {
    get { return Children[1].Children; }
  }

  public readonly string Name;
  public readonly int RequiredParameterCount, OptionalParameterCount;
  public readonly bool HasListParameter, HasDictParameter;

  public Type ReturnType
  {
    get { return returnType == null ? Body.ValueType : returnType; }
    set { returnType = value; }
  }

  public override void MarkTail(bool tail)
  {
    IsTail = tail;

    foreach(ASTNode parameter in Parameters)
    {
      parameter.MarkTail(false);
    }
    
    Body.MarkTail(true); // the body starts in a new function, so it always has a new tail
  }

  Type returnType;
}
#endregion

#region LiteralNode
public class LiteralNode : ASTNode
{
  public LiteralNode(object value) : base(false)
  {
    Value      = value;
    IsConstant = true;
  }

  public override Type ValueType
  {
    get { return Value == null ? null : Value.GetType(); }
  }

  public override void Emit(CodeGenerator cg, ref Type desiredType)
  {
    cg.EmitConstant(Value, desiredType);
    TailReturn(cg);
  }

  public override object Evaluate()
  {
    return Value;
  }
  
  public readonly object Value;
}
#endregion

#region OpNode
public class OpNode : ASTNode
{
  public OpNode(Operator op, params ASTNode[] values) : base(true)
  {
    if(op == null) throw new ArgumentNullException();
    Operator = op;
    Children.AddRange(values);
  }

  public override Type ValueType
  {
    get { return Operator.GetValueType(Children); }
  }

  public override void Emit(CodeGenerator cg, ref Type desiredType)
  {
    Operator.Emit(cg, Children, ref desiredType);
    TailReturn(cg);
  }

  public override object Evaluate()
  {
    return Operator.Evaluate(Children);
  }

  public readonly Operator Operator;
}
#endregion

#region NonExecutableNode
public abstract class NonExecutableNode : ASTNode
{
  protected NonExecutableNode(bool isContainerNode) : base(isContainerNode) { }

  public override Type ValueType
  {
    get { throw new NotSupportedException(); }
  }

  public override void Emit(CodeGenerator cg, ref Type desiredType)
  {
    throw new NotSupportedException();
  }

  public override object Evaluate()
  {
    throw new NotSupportedException();
  }
}
#endregion

#region ParameterNode
public enum ParameterType
{
  Normal, List, Dict
}

public class ParameterNode : NonExecutableNode
{
  public ParameterNode(string name) : this(name, typeof(object)) { }
  public ParameterNode(string name, Type valueType) : this(name, valueType, ParameterType.Normal, null) { }
  public ParameterNode(string name, Type valueType, ParameterType paramType, ASTNode defaultValue)
    : base(true)
  {
    if(name == null || valueType == null) throw new ArgumentNullException();
    Name          = name;
    Type          = valueType;
    ParameterType = paramType;
    DefaultValue  = defaultValue;
  }
  
  public ASTNode DefaultValue
  {
    get { return Children.Count == 0 ? null : Children[0]; }
    set
    {
      if(value != null)
      {
        if(Children.Count == 0) Children.Add(value);
        else Children[0] = value;
      }
      else if(Children.Count != 0)
      {
        Children.Clear();
      }
    }
  }

  public readonly string Name;
  public readonly ParameterType ParameterType;
  public readonly Type Type;
  
  public static string[] GetNames(ParameterNode[] parameters)
  {
    string[] parameterNames = new string[parameters.Length];
    for(int i=0; i<parameterNames.Length; i++)
    {
      parameterNames[i] = parameters[i].Name;
    }
    return parameterNames;
  }

  public static ParameterNode[] GetParameters(string[] parameterNames)
  {
    ParameterNode[] parameters = new ParameterNode[parameterNames.Length];
    for(int i=0; i<parameters.Length; i++)
    {
      parameters[i] = new ParameterNode(parameterNames[i]);
    }
    return parameters;
  }

  public static Type[] GetTypes(ParameterNode[] parameters)
  {
    Type[] types = new Type[parameters.Length];
    for(int i=0; i<types.Length; i++)
    {
      types[i] = parameters[i].Type;
    }
    return types;
  }

  public static void Validate(ParameterNode[] parameters, out int requiredCount, out int optionalCount,
                              out bool hasList, out bool hasDict)
  {
    List<string> names = new List<string>();
    requiredCount = optionalCount = 0;
    hasList = hasDict = false;
    
    for(int i=0; i<parameters.Length; i++)
    {
      ParameterNode param = parameters[i];
      if(names.Contains(param.Name)) // check for duplicate parameters
      {
        throw new ArgumentException("Duplicate parameter: "+param.Name);
      }
      names.Add(param.Name);
      
      switch(param.ParameterType)
      {
        case ParameterType.Normal:
          if(hasList || hasDict) throw new ArgumentException("List and dictionary arguments must come last.");

          if(param.DefaultValue == null) // required argument
          {
            if(optionalCount != 0)
            {
              throw new ArgumentException("Required parameters must precede optional parameters.");
            }
            requiredCount++;
          }
          else // optional argument
          {
            optionalCount++;
          }
          break;
        
        case ParameterType.List:
          if(hasList) throw new ArgumentException("Multiple list arguments are not allowed.");
          if(hasDict) throw new ArgumentException("Dictionary argument must come last.");
          hasList = true;
          break;

        case ParameterType.Dict:
          if(hasDict) throw new ArgumentException("Multiple dictionary arguments are not allowed.");
          break;
      }
    }
  }
}
#endregion

#region ScriptFunctionNode
public class ScriptFunctionNode : FunctionNode
{
  public ScriptFunctionNode(string name, ParameterNode[] parameters, ASTNode body)
    : base(name, typeof(object), parameters, body) { }

  public override Type ValueType
  {
    get { return typeof(ICallableWithKeywords); }
  }

  public override void Emit(CodeGenerator cg, ref Type desiredType)
  {
    currentIndex = functionIndex.Next;

    // create the method in the private class
    IMethodInfo method = GetMethod(cg.Assembly);

    if(desiredType != typeof(void))
    {
      // create the function wrapper
      ITypeInfo wrapperType = cg.Assembly.GetMethodWrapper(method);
      // instantiate it in the private class and store a reference to it
      cg.ILG.Emit(OpCodes.Ldftn, method.Method);
      cg.EmitBool(true); // isStatic
      cg.EmitNew(wrapperType, typeof(IntPtr), typeof(bool));
      cg.EmitSafeConversion(ValueType, desiredType);
    }

    TailReturn(cg);
  }

  public override object Evaluate()
  {
    return new InterpretedFunction(Name, GetParameterArray(), Body);
  }
  
  public IMethodInfo GetMethod(AssemblyGenerator ag)
  {
    if(generatedMethod == null)
    {
      generatedMethod = MakeDotNetMethod(ag.GetPrivateClass());
    }
    return generatedMethod;
  }

  void EmitDefaultParameterValues(CodeGenerator cg)
  {
    if(OptionalParameterCount == 0)
    {
      cg.EmitNull();
    }
    else
    {
      bool allConstant = true; // determine whether all optional parameters have constant values
      foreach(ParameterNode param in GetOptionalParameters())
      {
        if(!param.DefaultValue.IsConstant)
        {
          allConstant = false;
          break;
        }
      }
      
      if(allConstant) // if so, we can create the array once and simply emit a reference to it
      {
        List<object> values = new List<object>();
        foreach(ParameterNode param in GetOptionalParameters())
        {
          values.Add(param.DefaultValue.Evaluate());
        }
        cg.EmitCachedConstant(values.ToArray());
      }
      else // otherwise we'll have to emit the nodes each time
      {
        List<ASTNode> nodes = new List<ASTNode>();
        foreach(ParameterNode param in GetOptionalParameters())
        {
          nodes.Add(param.DefaultValue);
        }
        cg.EmitObjectArray(nodes.ToArray());
      }
    }
  }

  IEnumerable<ParameterNode> GetOptionalParameters()
  {
    if(OptionalParameterCount != 0)
    {
      for(int i=0; i<OptionalParameterCount; i++)
      {
        yield return (ParameterNode)Parameters[i+RequiredParameterCount];
      }
    }
  }

  ParameterNode[] GetParameterArray()
  {
    ParameterNode[] parameters = new ParameterNode[Parameters.Count];
    Parameters.CopyTo(parameters, 0);
    return parameters;
  }
  
  string[] GetParameterNames()
  {
    return Parameters.Count == 0 ? null : ParameterNode.GetNames(GetParameterArray());
  }

  Type[] GetParameterTypes()
  {
    bool allObject = true; // see if all parameters are object values
    foreach(ParameterNode param in Parameters)
    {
      if(param.Type != typeof(object))
      {
        allObject = false;
        break;
      }
    }

    return allObject ? null : ParameterNode.GetTypes(GetParameterArray()); // return null if all are of type object
  }

  IMethodInfo MakeDotNetMethod(TypeGenerator containingClass)
  {
    string name = "lambda$" + currentIndex + Name;
    CodeGenerator methodCg = containingClass.DefineStaticMethod(name, ReturnType, GetParameterTypes());

    // add names for the parameters
    MethodBuilderWrapper mb = (MethodBuilderWrapper)methodCg.Method;
    for(int i=0; i<Parameters.Count; i++)
    {
      mb.DefineParameter(i, ParameterAttributes.In, ((ParameterNode)Parameters[i]).Name);
    }

    methodCg.EmitTypedNode(Body, ReturnType);
    methodCg.Finish();
    return (IMethodInfo)methodCg.Method;
  }

  DynamicMethodClosure MakeDynamicMethod(CodeGenerator cg)
  {
    throw new NotImplementedException();
    /*DynamicMethod method =
      new DynamicMethod(Name == null ? "function" : Name, ReturnType, new Type[] {
                          typeof(DynamicMethodClosure), typeof(DynamicMethodEnvironment), typeof(object[]) },
                        typeof(DynamicMethodClosure));

    method.DefineParameter(1, ParameterAttributes.In, "this"); // add names for the parameters
    method.DefineParameter(2, ParameterAttributes.In, "ENV");
    method.DefineParameter(3, ParameterAttributes.In, "ARGS");
    
    CodeGenerator methodCg = new CodeGenerator(cg.Assembly, method, typeof(DynamicMethodClosure));
    methodCg.EmitTypedNode(Body, ReturnType);
    Binding[] bindings  = methodCg.GetCachedBindings();
    object[]  constants = methodCg.GetCachedNonBindings();
    methodCg.Finish();
    
    FunctionTemplate template = new FunctionTemplate(IntPtr.Zero, Name, GetParameterNames(), GetParameterTypes(),
                                                     RequiredParameterCount, false, false, false);
    return new DynamicMethodClosure(method, template, bindings, constants);*/
  }

  IMethodInfo generatedMethod;
  long currentIndex;

  static Index functionIndex = new Index();
}
#endregion

#region VariableNode
public class VariableNode : AssignableNode
{
  public VariableNode(string name) : base(false)
  {
    Name = name;
  }

  public VariableNode(string name, Slot slot) : base(false)
  {
    Name = name;
    Slot = slot;
  }

  public readonly string Name;
  public Slot Slot;

  public override Type ValueType
  {
    get
    {
      AssertValidSlot();
      return Slot.Type;
    }
  }

  public override void Emit(CodeGenerator cg, ref Type desiredType)
  {
    AssertValidSlot();
    Slot.EmitGet(cg);
    cg.EmitSafeConversion(Slot.Type, desiredType);
    TailReturn(cg);
  }

  public override void EmitSet(CodeGenerator cg, ASTNode valueNode)
  {
    AssertValidSlot();
    Slot.EmitSet(cg, valueNode);
  }

  public override void EmitSet(CodeGenerator cg, Type typeOnStack)
  {
    AssertValidSlot();
    Slot.EmitSet(cg, typeOnStack);
  }

  public override object Evaluate()
  {
    AssertValidSlot();  
    return Slot.EvaluateGet();
  }

  public override void EvaluateSet(object newValue)
  {
    AssertValidSlot();
    Slot.EvaluateSet(newValue);
  }
  
  void AssertValidSlot()
  {
    if(Slot == null)
    {
      throw new InvalidOperationException("This variable node is not decorated with a valid slot.");
    }
  }
}
#endregion

} // namespace Scripting.AST