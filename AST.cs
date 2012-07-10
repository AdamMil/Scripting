using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using Scripting.Emit;
using Scripting.Runtime;

namespace Scripting.AST
{

// TODO: take advantage of ASTNode.IsConstant
#region Index
public sealed class Index
{
  public long Next 
  {
    get { lock(this) return index++; } 
  }

  public string NextString
  {
    get { return Next.ToString(CultureInfo.InvariantCulture); }
  }

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
    AssertValidInsertNode(item);
    
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
    AssertValidInsertNode(item);
    BeforeRemovingItem(index);
    base.SetItem(index, item);
    AfterAddingItem(index);
  }

  void AssertValidInsertNode(ASTNode item)
  {
    if(item == null) throw new ArgumentNullException("Null nodes cannot be added as children.");
    if(item.ParentNode != null) throw new ArgumentException("This node already belongs to a parent.");
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

  /// <summary>Gets the context type in which this node will be emitted or evaluated. The context type is the type that
  /// the enclosing context expects this node to emit, although it is not absolutely necessary that the node emit that
  /// type unless this is a tail node. A context type of <see cref="TypeWrapper.Unknown"/> implies that any emission
  /// from the node is okay.
  /// </summary>
  public ITypeInfo ContextType
  {
    get { return contextType; }
  }

  /// <summary>Gets or sets whether this node's value is constant and can be safely evaluated at compile time.</summary>
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

  /// <summary>Gets or sets the namespace that defines the lexical scope in which this node is situated.</summary>
  public LexicalScope Scope
  {
    get
    {
      ASTNode node = this;
      do
      {
        if(node.scope != null) return node.scope;
        node = node.ParentNode;
      } while(node != null);

      return null;
    }
    set { scope = value; }
  }

  /// <summary>Gets the natural type of this node's value when emitted. If the value type is <see cref="Void"/>, this
  /// node normally emits nothing. If the value type is null, this node normally emits null. A node may emit a type
  /// other than its natural type only to satisfy the <see cref="ContextType"/>.
  /// </summary>
  public abstract ITypeInfo ValueType { get; }

  /// <summary>Gets or sets the name of the data source from which the node was parsed.</summary>
  public string SourceName;

  /// <summary>Gets or sets the start or end position where this node was parsed within the data source.</summary>
  public FilePosition StartPosition, EndPosition;

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

  /// <summary>Gets the nearest ancestor of this node with the given node type.</summary>
  /// <typeparam name="T">The type of node to retrieve.</typeparam>
  /// <returns>The nearest ancestor with type <typeparamref name="T"/>, or null if an ancestor with that type could
  /// not be found.
  /// </returns>
  public T GetAncestor<T>() where T : ASTNode
  {
    ASTNode ancestor = parent;
    while(ancestor != null)
    {
      T ret = ancestor as T;
      if(ret != null) return ret;
      ancestor = ancestor.parent;
    }
    return null;
  }

  /// <summary>Retrieves all descendants of the current node with the given node type.</summary>
  /// <typeparam name="T">The type of nodes to retrieve.</typeparam>
  /// <returns>Returns an array of <typeparamref name="T"/>, with the nodes given in depth-first order.</returns>
  public T[] GetDescendants<T>() where T : ASTNode
  {
    List<T> nodes = new List<T>();
    GetDescendants(nodes);
    return nodes.ToArray();
  }

  public void AddMessage(Diagnostic diagnostic, params object[] args)
  {
    CompilerState.Current.Messages.Add(
      diagnostic.ToMessage(CompilerState.Current.TreatWarningsAsErrors, SourceName, StartPosition, args));
  }

  /// <summary>Called to check the semantics of the parse tree. This method is called before the corresponding method
  /// on child nodes, while traveling down the parse tree.
  /// </summary>
  public virtual void CheckSemantics()
  {
    if(ContextType == null) throw new InvalidOperationException("ContextType has not been set.");
    if(Scope == null) throw new InvalidOperationException("Scope has not been set.");

    if((ContextType != TypeWrapper.Any || ValueType == TypeWrapper.Void) && !CG.IsConvertible(ValueType, ContextType))
    {
      AddMessage(CoreDiagnostics.CannotConvertType, CG.TypeName(ValueType), CG.TypeName(ContextType));
    }
  }

  /// <summary>Called to check the semantics of the parse tree. This method is called after the corresponding method
  /// on child nodes, while traveling up the parse tree.
  /// </summary>
  public virtual void CheckSemantics2()
  {
  }

  public virtual object Evaluate()
  {
    throw new NotSupportedException();
  }

  public abstract ITypeInfo Emit(CodeGenerator cg);

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

  public virtual void SetValueContext(ITypeInfo desiredType)
  {
    contextType = desiredType;
  }

  /// <summary>If this node is a tail node (<see cref="IsTail"/> is true), this method will emit a proper return from
  /// the function. It assumes that the type of the value on the stack (if any) is equal to <see cref="ContextType"/>.
  /// </summary>
  protected ITypeInfo TailReturn(CodeGenerator cg)
  {
    if(IsTail)
    {
      if(!IsInTry)
      {
        cg.EmitReturn();
      }
      else
      {
        throw new NotImplementedException(); // TODO: implement leave from try blocks
      }
    }

    if(ContextType == TypeWrapper.Any) throw new InvalidOperationException("Can't return the 'any' type.");
    return ContextType;
  }

  /// <summary>If this node is a tail node (<see cref="IsTail"/> is true), this method will emit a conversion
  /// from <paramref name="typeOnStack"/> to <see cref="ContextType"/>, and emit a proper return from the function.
  /// Otherwise, it will simply return <paramref name="typeOnStack"/>.
  /// </summary>
  /// <remarks>A call to this method should be placed in <see cref="Emit"/> implementations at the end of code paths
  /// where a child marked as a tail node might not have been emitted.
  /// </remarks>
  protected ITypeInfo TailReturn(CodeGenerator cg, ITypeInfo typeOnStack)
  {
    if(!IsTail)
    {
      return typeOnStack;
    }
    else if(ContextType == TypeWrapper.Any)
    {
      throw new InvalidOperationException("The 'any' type can't be used in a tail position.");
    }
    else
    {
      if(typeOnStack == TypeWrapper.Void) cg.EmitDefault(ContextType);
      else cg.EmitConversion(typeOnStack, ContextType);
      return TailReturn(cg);
    }
  }

  /// <summary>Returns true if all nodes are constant.</summary>
  public static bool AreConstant(params ASTNode[] nodes)
  {
    return AreConstant((IList<ASTNode>)nodes);
  }

  /// <summary>Returns true if all nodes are constant.</summary>
  public static bool AreConstant(IList<ASTNode> nodes)
  {
    foreach(ASTNode node in nodes)
    {
      if(!node.IsConstant) return false;
    }
    return true;
  }

  /// <summary>Evaluates the given nodes and returns the values in an array of <see cref="System.Object"/>.</summary>
  public static object[] EvaluateNodes(IList<ASTNode> nodes)
  {
    object[] array = new object[nodes.Count];
    for(int i=0; i<nodes.Count; i++) array[i] = nodes[i].Evaluate();
    return array;
  }

  /// <summary>Evaluates the given nodes, converting each one to the element type. Returns an array containing the
  /// resulting values.
  /// </summary>
  public static Array EvaluateNodes(IList<ASTNode> nodes, Type elementType)
  {
    Array array = Array.CreateInstance(elementType, nodes.Count);
    for(int i=0; i<nodes.Count; i++)
    {
      array.SetValue(Ops.ConvertTo(nodes[i].Evaluate(), elementType), i);
    }
    return array;
  }

  /// <summary>Gets the <see cref="ValueType"/> of each node passed and returns the types in an array.</summary>
  public static ITypeInfo[] GetNodeTypes(IList<ASTNode> nodes)
  {
    ITypeInfo[] types = new ITypeInfo[nodes.Count];
    for(int i=0; i<types.Length; i++)
    {
      types[i] = nodes[i].ValueType;
    }
    return types;
  }

  protected static bool IsVoid(ITypeInfo type)
  {
    return type == TypeWrapper.Void;
  }

  internal ASTNode parent, previousNode, nextNode;
  internal int index;

  [Flags]
  internal enum Flag
  {
    /// <summary>Indicates whether the node's value is constant and can be evaluated at compile-time.</summary>
    Constant = 1,
    /// <summary>Indicates whether the node is a tail node, meaning that the result of its evaluation will be returned
    /// from the function.
    /// </summary>
    Tail = 2,
    /// <summary>Indicates whether this node is contained within an exception handling block.</summary>
    InTry = 4,
    /// <summary>Indicates that this node's cannot be assigned to.</summary>
    ReadOnly = 8,
  }

  internal bool Is(Flag flag)
  {
    return (flags & flag) != 0;
  }

  internal void Set(Flag flag, bool on)
  {
    if(on) flags |= flag;
    else flags &= ~flag;
  }

  void GetDescendants<T>(List<T> nodes) where T : ASTNode
  {
    foreach(ASTNode child in Children)
    {
      T tChild = child as T;
      if(tChild != null)
      {
        nodes.Add(tChild);
      }
      child.GetDescendants(nodes);
    }
  }

  ASTNodeCollection children;
  LexicalScope scope;
  List<Attribute> attributes;
  ITypeInfo contextType;
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

  /// <summary>Gets or sets whether this variable's value is read-only and cannot be modified after it has been
  /// assigned.
  /// </summary>
  public bool IsReadOnly
  {
    get { return Is(Flag.ReadOnly); }
    set { Set(Flag.ReadOnly, value); }
  }

  public abstract bool IsSameSlotAs(ASTNode rhs);
  public abstract void EvaluateSet(object newValue, bool initialize);
  public abstract void EmitSet(CodeGenerator cg, ASTNode valueNode, bool initialize);
  public abstract void EmitSet(CodeGenerator cg, ITypeInfo typeOnStack, bool initialize);
}
#endregion

#region AssignNode
public class AssignNode : ASTNode
{
  public AssignNode(AssignableNode lhs, ASTNode rhs) : this(lhs, rhs, false) { }

  public AssignNode(AssignableNode lhs, ASTNode rhs, bool initialize) : base(true)
  {
    if(lhs == null || rhs == null) throw new ArgumentNullException();
    Children.AddRange(lhs, rhs);
    this.initialize = initialize;
  }

  /// <summary>Gets whether this assignment is initializing the value for the first time.</summary>
  public bool Initializing
  {
    get { return initialize; }
  }

  public AssignableNode LHS
  {
    get { return (AssignableNode)Children[0]; }
  }

  public ASTNode RHS
  {
    get { return Children[1]; }
  }

  public override ITypeInfo ValueType
  {
    get { return RHS.ValueType; }
  }

  public override void CheckSemantics()
  {
    base.CheckSemantics();
    if(LHS.IsSameSlotAs(RHS)) AddMessage(CoreDiagnostics.VariableAssignedToSelf);
  }

  public override void CheckSemantics2()
  {
    base.CheckSemantics2();
    if(!Initializing && LHS.IsReadOnly) AddMessage(CoreDiagnostics.ReadOnlyVariableAssigned, LHS);
  }

  public override ITypeInfo Emit(CodeGenerator cg)
  {
    if(IsVoid(ContextType))
    {
      LHS.EmitSet(cg, RHS, initialize);
      return TailReturn(cg, ContextType);
    }
    else
    {
      cg.EmitTypedNode(RHS, LHS.ValueType);
      cg.EmitDup();
      LHS.EmitSet(cg, LHS.ValueType, initialize);
      return TailReturn(cg, LHS.ValueType);
    }
  }

  public override object Evaluate()
  {
    object newValue = RHS.Evaluate();
    LHS.EvaluateSet(newValue, initialize);
    return newValue;
  }

  public override void SetValueContext(ITypeInfo desiredType)
  {
    base.SetValueContext(desiredType);
    LHS.SetValueContext(TypeWrapper.Unknown); // the LHS won't be read, only written
    RHS.SetValueContext(LHS.ValueType);
  }

  bool initialize;
}
#endregion

#region BlockNode
public class BlockNode : ASTNode
{
  public BlockNode() : base(true) { }
  public BlockNode(params ASTNode[] nodes) : base(true)
  {
    if(nodes != null) Children.AddRange(nodes);
  }

  public override ITypeInfo ValueType
  {
    get
    {
      ASTNode lastChild = LastChild;
      return lastChild == null ? TypeWrapper.Void : LastChild.ValueType;
    }
  }

  public override void CheckSemantics2()
  {
    base.CheckSemantics2();
    IsConstant = AreConstant(Children);
  }

  public override ITypeInfo Emit(CodeGenerator cg)
  {
    if(Children.Count == 0) return TailReturn(cg, TypeWrapper.Void);

    for(int i=0; i<Children.Count-1; i++)
    {
      cg.EmitVoid(Children[i]);
    }
    return LastChild.Emit(cg);
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
      for(int i=0; i<Children.Count-1; i++) Children[i].MarkTail(false);
      LastChild.MarkTail(tail);
    }
  }

  public override void SetValueContext(ITypeInfo desiredType)
  {
    base.SetValueContext(desiredType);

    if(Children.Count != 0)
    {
      for(int i=0; i<Children.Count-1; i++) Children[i].SetValueContext(TypeWrapper.Void);
      LastChild.SetValueContext(desiredType);
    }
  }
}
#endregion

#region CastNode
public abstract class CastNode : ASTNode
{
  public CastNode(ITypeInfo type, ASTNode value) : base(true)
  {
    if(value == null || type == null) throw new ArgumentNullException();
    Children.Add(value);
    this.type = type;
  }

  public ASTNode Value
  {
    get { return Children[0]; }
  }

  public sealed override ITypeInfo ValueType
  {
    get { return type; }
  }

  public override void CheckSemantics2()
  {
    base.CheckSemantics2();
    IsConstant = Value.IsConstant;
  }

  public sealed override ITypeInfo Emit(CodeGenerator cg)
  {
    EmitCast(cg, Value.Emit(cg));
    return TailReturn(cg);
  }

  public sealed override object Evaluate()
  {
    return Ops.ConvertTo(Value.Evaluate(), type.DotNetType);
  }

  public override void SetValueContext(ITypeInfo desiredType)
  {
    base.SetValueContext(desiredType);
    Value.SetValueContext(ValueType);
  }

  protected abstract void EmitCast(CodeGenerator cg, ITypeInfo typeOnStack);

  readonly ITypeInfo type;
}
#endregion

#region ContainerNode
public class ContainerNode : NonExecutableNode
{
  public ContainerNode() : base(true) { }
  public ContainerNode(params ASTNode[] nodes) : base(true)
  {
    if(nodes != null) Children.AddRange(nodes);
  }
}
#endregion

#region FunctionNode
public abstract class FunctionNode : ASTNode
{
  public FunctionNode(string name, ITypeInfo returnType, ParameterNode[] parameters, ASTNode body) : base(true)
  {
    if(parameters == null || body == null) throw new ArgumentNullException();
    ParameterNode.Validate(parameters, out RequiredParameterCount, out OptionalParameterCount,
                           out HasListParameter, out HasDictParameter);

    this.Name       = name;
    this.returnType = returnType;
    
    Children.Add(body);
    Children.Add(new ContainerNode(parameters));
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

  public bool CreatesClosure
  {
    get { return Closures != null && Closures.Length != 0; }
  }

  public readonly string Name;

  /// <summary>Gets the closure slots defined by this function. If set to a non-empty array, this method will create a
  /// new closure.
  /// </summary>
  public ClosureSlot[] Closures;
  /// <summary>The maximum number of levels of ancestor closures that need to be accessible from this function's
  /// closure, if the function creates a closure.
  /// </summary>
  public int MaxClosureReferenceDepth;
  public readonly int RequiredParameterCount, OptionalParameterCount;
  public readonly bool HasListParameter, HasDictParameter;

  public ITypeInfo ReturnType
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

  public override void SetValueContext(ITypeInfo desiredType)
  {
    base.SetValueContext(desiredType);

    foreach(ASTNode parameter in Parameters) parameter.SetValueContext(TypeWrapper.Unknown);
    Body.SetValueContext(ReturnType);
  }

  ITypeInfo returnType;
}
#endregion

#region IfNode
public class IfNode : ASTNode
{
  public IfNode(ASTNode condition, ASTNode ifTrue, ASTNode ifFalse)
    : this(Operator.LogicalTruth, condition, ifTrue, ifFalse) { }

  public IfNode(UnaryOperator truthOperator, ASTNode condition, ASTNode ifTrue, ASTNode ifFalse) : base(true)
  {
    if(truthOperator == null || condition == null || ifTrue == null) throw new ArgumentNullException();
    TruthOperator = truthOperator;
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

  public override ITypeInfo ValueType
  {
    get { return IfFalse == null ? IfTrue.ValueType : CG.GetCommonBaseType(IfTrue.ValueType, IfFalse.ValueType); }
  }

  public readonly UnaryOperator TruthOperator;

  public override void CheckSemantics2()
  {
    base.CheckSemantics2();

    if(Condition.IsConstant)
    {
      IsConstant = (bool)TruthOperator.Evaluate(Condition.Evaluate()) ?
        IfTrue.IsConstant : IfFalse == null || IfFalse.IsConstant;
    }
    else
    {
      IsConstant = false;
    }
  }

  public override ITypeInfo Emit(CodeGenerator cg)
  {
    cg.EmitTypedOperator(TruthOperator, TypeWrapper.Bool, Condition);

    Label end = cg.ILG.DefineLabel();
    Label falseLabel = IsVoid(ContextType) && IfFalse == null ? end : cg.ILG.DefineLabel();
    ITypeInfo valueType = IfFalse == null ? ContextType : ValueType;

    cg.ILG.Emit(OpCodes.Brfalse, falseLabel); // jump to the false branch (or end) if the condition is false
    cg.EmitTypedNode(IfTrue, valueType);      // emit the true branch

    // emit the false branch
    if(!IsVoid(ContextType) || IfFalse != null)
    {
      cg.ILG.Emit(OpCodes.Br, end);
      cg.ILG.MarkLabel(falseLabel);
      if(IfFalse != null)
      {
        cg.EmitTypedNode(IfFalse, valueType);
      }
      else if(!IsVoid(ContextType))
      {
        cg.EmitDefault(valueType);
      }
    }

    cg.ILG.MarkLabel(end);
    return TailReturn(cg, valueType);
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

  public override void SetValueContext(ITypeInfo desiredType)
  {
    base.SetValueContext(desiredType);
    TruthOperator.SetValueContext(TypeWrapper.Bool, Condition);
    IfTrue.SetValueContext(ValueType);
    if(IfFalse != null) IfFalse.SetValueContext(ValueType);
  }
}
#endregion

#region LiteralNode
public class LiteralNode : ASTNode
{
  public LiteralNode(object value) : this(value, false) { }

  public LiteralNode(object value, bool emitCachedLiteral) : base(false)
  {
    Value      = value;
    IsConstant = true;
    cached     = emitCachedLiteral;
  }

  public override ITypeInfo ValueType
  {
    get { return CG.GetTypeInfo(Value); }
  }

  public override ITypeInfo Emit(CodeGenerator cg)
  {
    if(IsVoid(ContextType)) return TailReturn(cg);

    if(cached)
    {
      cg.EmitCachedConstant(Value);
      return TailReturn(cg, ValueType);
    }
    else
    {
      return TailReturn(cg, cg.EmitConstant(Value, ContextType));
    }
  }

  public override object Evaluate()
  {
    return Value;
  }

  public readonly object Value;
  readonly bool cached;
}
#endregion

#region NonExecutableNode
public abstract class NonExecutableNode : ASTNode
{
  protected NonExecutableNode(bool isContainerNode) : base(isContainerNode) { }

  public override ITypeInfo ValueType
  {
    get { throw new NotSupportedException(); }
  }

  public override void CheckSemantics() { }
  public override void CheckSemantics2() { }

  public override ITypeInfo Emit(CodeGenerator cg)
  {
    throw new NotSupportedException();
  }

  public override object Evaluate()
  {
    throw new NotSupportedException();
  }
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

  public override ITypeInfo ValueType
  {
    get { return Operator.GetValueType(Children); }
  }

  public override void CheckSemantics()
  {
    base.CheckSemantics();
    Operator.CheckSemantics(ContextType, Children);
  }

  public override void CheckSemantics2()
  {
    base.CheckSemantics2();
    IsConstant = AreConstant(Children);
  }

  public override ITypeInfo Emit(CodeGenerator cg)
  {
    return TailReturn(cg, Operator.Emit(cg, ContextType, Children));
  }

  public override object Evaluate()
  {
    return Operator.Evaluate(Children);
  }

  public override void SetValueContext(ITypeInfo desiredType)
  {
    base.SetValueContext(desiredType);
    Operator.SetValueContext(desiredType, Children);
  }

  public readonly Operator Operator;
}
#endregion

// TODO: perhaps a processor should be created to remove the options nodes, and push the option changes into a
// decoration on the rest of the AST?
#region OptionsNode
public class OptionsNode : ASTNode
{
  public OptionsNode(CompilerState state, ASTNode body) : base(true)
  {
    if(state == null || body == null) throw new ArgumentNullException();
    this.state = state;
    Children.Add(body);
  }

  public ASTNode Body
  {
    get { return Children[0]; }
  }

  public CompilerState CompilerState
  {
    get { return state; }
  }

  public override ITypeInfo ValueType
  {
    get
    {
      CompilerState.Push(state);
      try { return Body.ValueType; }
      finally { CompilerState.Pop(); }
    }
  }

  public override void CheckSemantics2()
  {
    base.CheckSemantics2();
    IsConstant = Body.IsConstant;
  }

  public override ITypeInfo Emit(CodeGenerator cg)
  {
    CompilerState.Push(state);
    try { return Body.Emit(cg); }
    finally { CompilerState.Pop(); }
  }

  public override object Evaluate()
  {
    CompilerState.Push(state);
    try { return Body.Evaluate(); }
    finally { CompilerState.Pop(); }
  }

  public override void MarkTail(bool tail)
  {
    CompilerState.Push(state);
    try { Body.MarkTail(tail); }
    finally { CompilerState.Pop(); }
  }

  public override void SetValueContext(ITypeInfo desiredType)
  {
    CompilerState.Push(state);
    try
    {
      base.SetValueContext(desiredType);
      Body.SetValueContext(desiredType);
    }
    finally { CompilerState.Pop(); }
  }

  CompilerState state;
}
#endregion

#region ParameterNode
public enum ParameterType
{
  Normal, List, Dict
}

public class ParameterNode : NonExecutableNode
{
  public ParameterNode(string name) : this(name, TypeWrapper.Unknown) { }
  public ParameterNode(string name, ParameterType paramType) : this(name, TypeWrapper.Unknown, paramType) { }
  public ParameterNode(string name, ITypeInfo valueType) : this(name, valueType, ParameterType.Normal, null) { }
  public ParameterNode(string name, ITypeInfo valueType, ParameterType paramType)
    : this(name, valueType, paramType, null) { }

  public ParameterNode(string name, ITypeInfo valueType, ParameterType paramType, ASTNode defaultValue) : base(true)
  {
    if(name == null || valueType == null) throw new ArgumentNullException();
    Variable      = new VariableNode(name);
    Type          = valueType;
    ParameterType = paramType;
    DefaultValue  = defaultValue;

    if(paramType != ParameterType.Normal)
    {
      if(defaultValue != null) throw new ArgumentException("List and Dict parameters cannot have default values.");
      if(paramType == ParameterType.List)
      {
        Type = TypeWrapper.Get(CompilerState.Current.Language.ParameterListType);
      }
      else if(paramType == ParameterType.Dict)
      {
        Type = TypeWrapper.Get(CompilerState.Current.Language.ParameterDictionaryType);
      }
    }
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

  public FunctionNode Function
  {
    get { return parent == null ? null : (FunctionNode)parent.parent; }
  }

  public string Name
  {
    get { return Variable.Name; }
  }

  public override void SetValueContext(ITypeInfo desiredType)
  {
    base.SetValueContext(desiredType);
    if(DefaultValue != null) DefaultValue.SetValueContext(Type);
  }

  public readonly VariableNode Variable;
  public readonly ParameterType ParameterType;
  public readonly ITypeInfo Type;
  
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

  public static ITypeInfo[] GetTypes(ParameterNode[] parameters)
  {
    ITypeInfo[] types = new ITypeInfo[parameters.Length];
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
          hasDict = true;
          break;
      }
    }
  }
}
#endregion

#region RuntimeCastNode
public sealed class RuntimeCastNode : CastNode
{
  public RuntimeCastNode(ITypeInfo type, ASTNode value) : base(type, value) { }

  protected override void EmitCast(CodeGenerator cg, ITypeInfo typeOnStack)
  {
    cg.EmitRuntimeConversion(typeOnStack, ContextType);
  }
}
#endregion

#region SafeCastNode
public sealed class SafeCastNode : CastNode
{
  public SafeCastNode(ITypeInfo type, ASTNode value) : base(type, value) { }

  protected override void EmitCast(CodeGenerator cg, ITypeInfo typeOnStack)
  {
    cg.EmitSafeConversion(typeOnStack, ContextType);
  }
}
#endregion

#region ScriptFunctionNode
public class ScriptFunctionNode : FunctionNode
{
  public ScriptFunctionNode(string name, ParameterNode[] parameters, ASTNode body)
    : this(name, TypeWrapper.Unknown, parameters, body) { }

  public ScriptFunctionNode(string name, ITypeInfo returnType, ParameterNode[] parameters, ASTNode body)
    : base(name, returnType, parameters, body)
  {
    Language = CompilerState.Current.Language;
  }

  public readonly Language Language;

  public override ITypeInfo ValueType
  {
    get { return TypeWrapper.Get(typeof(ICallableWithKeywords)); }
  }

  public override ITypeInfo Emit(CodeGenerator cg)
  {
    currentIndex = functionIndex.Next;

    // create the method in the private class
    IMethodInfo method = GetMethod(cg.Assembly);

    if(HasListParameter || HasDictParameter) throw new NotImplementedException(); // method wrappers won't handle this properly

    if(CompilerState.Current.Optimize && IsVoid(ContextType))
    {
      return TailReturn(cg);
    }
    else
    {
      // create and instantiate the wrapper class
      ITypeInfo wrapperType = cg.Assembly.GetMethodWrapper(method);
      cg.ILG.Emit(OpCodes.Ldftn, method.Method);
      if(method.IsStatic)
      {
        cg.EmitNew(wrapperType, TypeWrapper.IntPtr);
      }
      else
      {
        cg.ClosureSlot.EmitGet(cg);
        cg.EmitNew(wrapperType, TypeWrapper.IntPtr, cg.ClosureSlot.Type);
      }
      return TailReturn(cg, wrapperType);
    }
  }

  public override object Evaluate()
  {
    return new InterpretedFunction(Language, Name, GetParameterArray(), Body);
  }
  
  public IMethodInfo GetMethod(AssemblyGenerator ag)
  {
    if(generatedMethod == null) generatedMethod = MakeDotNetMethod(ag);
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
        cg.EmitObjectArray(nodes);
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

  ITypeInfo[] GetParameterTypes()
  {
    bool allObject = true;
    foreach(ParameterNode param in Parameters)
    {
      if(param.Type.DotNetType != typeof(object))
      {
        allObject = false;
        break;
      }
    }

    return allObject ? null : ParameterNode.GetTypes(GetParameterArray()); // return null if all are of type object
  }

  IMethodInfo MakeDotNetMethod(AssemblyGenerator ag)
  {
    MethodAttributes attributes = MethodAttributes.Public;

    TypeGenerator containingClass = null;
    ScriptFunctionNode parentFunc = GetAncestor<ScriptFunctionNode>();
    if(parentFunc != null) containingClass = parentFunc.closureClass;

    if(containingClass == null) // TODO: we should only put functions on a closure object if they actually need to access closed variables
    {
      containingClass = ag.GetPrivateClass();
      attributes |= MethodAttributes.Static;
    }

    CodeGenerator methodCg = containingClass.DefineMethod(attributes,
                                               "lambda$" + currentIndex.ToString(CultureInfo.InvariantCulture) + Name,
                                               ReturnType, ParameterNode.GetTypes(GetParameterArray()));

    // add names for the parameters
    MethodBuilderWrapper mb = (MethodBuilderWrapper)methodCg.Method;
    for(int i=0; i<Parameters.Count; i++)
    {
      mb.DefineParameter(i, ParameterAttributes.In, ((ParameterNode)Parameters[i]).Name);
    }

    if(CreatesClosure)
    {
      closureClass = methodCg.SetupClosure(Closures, MaxClosureReferenceDepth);
    }

    methodCg.EmitTypedNode(Body, ReturnType);
    methodCg.Finish();
    return (IMethodInfo)methodCg.Method;
  }

  IMethodInfo generatedMethod;
  TypeGenerator closureClass;
  long currentIndex;

  static Index functionIndex = new Index();
}
#endregion

#region UnsafeCastNode
public sealed class UnsafeCastNode : CastNode
{
  public UnsafeCastNode(ITypeInfo type, ASTNode value) : base(type, value) { }

  protected override void EmitCast(CodeGenerator cg, ITypeInfo typeOnStack)
  {
    cg.EmitUnsafeConversion(typeOnStack, ContextType);
  }
}
#endregion

#region VariableNode
public class VariableNode : AssignableNode
{
  public VariableNode(string name) : this(name, null) { }

  public VariableNode(string name, Slot slot) : base(false)
  {
    if(name == null) throw new ArgumentNullException();
    Name = name;
    Slot = slot;
  }

  public readonly string Name;
  public Slot Slot;

  public override ITypeInfo ValueType
  {
    get
    {
      AssertValidSlot();
      return Slot.Type;
    }
  }

  public override void CheckSemantics2()
  {
    base.CheckSemantics2();

    if(Scope != null)
    {
      Symbol symbol = Scope.Get(Name);
      if(symbol != null) IsReadOnly = symbol.DeclaringVariable.IsReadOnly;
    }
  }

  public override ITypeInfo Emit(CodeGenerator cg)
  {
    AssertValidSlot();
    if(IsVoid(ContextType))
    {
      return TailReturn(cg);
    }
    else
    {
      Slot.EmitGet(cg);
      return TailReturn(cg, Slot.Type);
    }
  }

  public override void EmitSet(CodeGenerator cg, ASTNode valueNode, bool initialize)
  {
    AssertValidSlot();
    Slot.EmitSet(cg, valueNode, initialize);
  }

  public override void EmitSet(CodeGenerator cg, ITypeInfo typeOnStack, bool initialize)
  {
    AssertValidSlot();
    Slot.EmitSet(cg, typeOnStack, initialize);
  }

  public override object Evaluate()
  {
    AssertValidSlot();  
    return Slot.EvaluateGet();
  }

  public override void EvaluateSet(object newValue, bool initialize)
  {
    AssertValidSlot();
    Slot.EvaluateSet(newValue, initialize);
  }

  public override bool IsSameSlotAs(ASTNode rhs)
  {
    AssertValidSlot();
    VariableNode rhsVar = rhs as VariableNode;
    return rhsVar != null && Slot.IsSameAs(rhsVar.Slot);
  }

  public override string ToString()
  {
    return Name;
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