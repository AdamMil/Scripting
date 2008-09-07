using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Scripting.AST
{

#region IASTProcessor
public interface IASTProcessor
{
  Stage Stage { get; }
  void Process(ref ASTNode rootNode);
}
#endregion

#region ProcessorBase
public abstract class ProcessorBase : IASTProcessor
{
  public ProcessorBase(DecoratorType type)
  {
    decoratorType = type;
  }

  public abstract Stage Stage { get; }
  public abstract void Process(ref ASTNode rootNode);

  /// <summary>Gets whether the processor is being run on an AST that will end up being compiled.</summary>
  /// <remarks>If false, the AST will end up being interpreted.</remarks>
  protected bool IsCompiled
  {
    get { return decoratorType == DecoratorType.Compiled; }
  }

  /// <summary>Adds a new error message using the given node's source name and position.</summary>
  protected void AddMessage(Diagnostic diagnostic, ASTNode node, params object[] args)
  {
    CompilerState.Current.Messages.Add(
      diagnostic.ToMessage(CompilerState.Current.TreatWarningsAsErrors, node.SourceName, node.StartPosition, args));
  }

  readonly DecoratorType decoratorType;
}
#endregion

#region PrefixProcessor
/// <summary>Provides the base class for a processor that walks the tree in a prefix order and allows nodes to be
/// replaced or removed.
/// </summary>
public abstract class PrefixProcessor : ProcessorBase
{
  protected PrefixProcessor(DecoratorType type) : base(type) { }

  public override void Process(ref ASTNode rootNode)
  {
    options = CompilerState.Current;
    RecursiveVisit(ref rootNode);
    options = null;
  }

  protected CompilerState CurrentOptions
  {
    get { return options; }
  }

  protected abstract bool Visit(ref ASTNode node);
  protected virtual void EndVisit(ASTNode node) { }

  protected void RecursiveVisit(ref ASTNode node)
  {
    if(Visit(ref node))
    {
      OptionsNode optionsNode = node as OptionsNode;
      if(optionsNode != null)
      {
        if(optionsStack == null) optionsStack = new Stack<CompilerState>();
        optionsStack.Push(options);
        options = optionsNode.CompilerState;
      }

      for(int i=0; i<node.Children.Count; i++)
      {
        ASTNode child = node.Children[i];
        RecursiveVisit(ref child);

        if(child == null)
        {
          node.Children.RemoveAt(i--);
        }
        else if(child != node.Children[i])
        {
          node.Children[i] = child;
        }
      }

      if(optionsNode != null) options = optionsStack.Pop();
    }

    if(node != null) EndVisit(node); // Visit may have set the node to null (removing it)
  }

  CompilerState options;
  Stack<CompilerState> optionsStack;
}
#endregion

#region PrefixVisitor
/// <summary>Provides the base class for a simple visitor that walks the tree in a prefix order.</summary>
public abstract class PrefixVisitor : ProcessorBase
{
  protected PrefixVisitor(DecoratorType type) : base(type) { }

  public override void Process(ref ASTNode rootNode)
  {
    options = CompilerState.Current;
    RecursiveVisit(rootNode);
    options = null;
  }
  
  protected CompilerState CurrentOptions
  {
    get { return options; }
  }

  /// <returns>Returns true if the children of the node should be visited.</returns>
  protected abstract bool Visit(ASTNode node);

  protected virtual void EndVisit(ASTNode node) { }

  protected void RecursiveVisit(ASTNode node)
  {
    if(Visit(node))
    {
      OptionsNode optionsNode = node as OptionsNode;
      if(optionsNode != null)
      {
        if(optionsStack == null) optionsStack = new Stack<CompilerState>();
        optionsStack.Push(options);
        options = optionsNode.CompilerState;
      }

      foreach(ASTNode child in node.Children)
      {
        RecursiveVisit(child);
      }

      if(optionsNode != null) options = optionsStack.Pop();
    }
    
    EndVisit(node);
  }

  CompilerState options;
  Stack<CompilerState> optionsStack;
}
#endregion

#region ASTProcessorCollection
public sealed class ASTProcessorCollection : Collection<IASTProcessor>
{
  public void Process(ref ASTNode rootNode)
  {
    foreach(IASTProcessor stage in this)
    {
      stage.Process(ref rootNode);
    }
  }

  protected override void InsertItem(int index, IASTProcessor item)
  {
    if(item == null) throw new ArgumentNullException("Stage cannot be null.");
    base.InsertItem(index, item);
  }
}
#endregion

#region Stage
public enum Stage
{
  /// <summary>This stage operates upon a parse tree that is not yet decorated or even correct. Its responsibilities
  /// are to replace nodes with other nodes as necessary to ovecome deficiencies in the parser, and get the tree into
  /// a form that correctly represents the source code.
  /// </summary>
  PreDecorate,
  /// <summary>This stage operates upon a parse tree that is true to the source code, but not decorated. Its
  /// responsibilies are to check the semantics of the tree to ensure that the user's source code is valid, and to
  /// decorate the tree (ensure that every <see cref="VariableNode"/> has the correct slot, ensure that
  /// tails are marked, etc).
  /// </summary>
  Decorate,
  /// <summary>This stage operates upon a parse tree that is fully decorated and semantically valid, but not yet
  /// optimized. Its responsibilities are to optimize the tree while maintaining correctness and proper decoration.
  /// </summary>
  Optimize,
  /// <summary>This stage operates upon a parse tree that is fully decorated and optimized.</summary>
  Optimized
}
#endregion

#region ASTDecorator
public sealed class ASTDecorator
{
  public void AddToBeginningOfStage(IASTProcessor processor)
  {
    GetProcessors(processor.Stage).Insert(0, processor);
  }

  public void AddToEndOfStage(IASTProcessor processor)
  {
    GetProcessors(processor.Stage).Add(processor);
  }

  public ASTProcessorCollection GetProcessors(Stage stage)
  {
    ASTProcessorCollection stageCollection = stages[(int)stage];
    if(stageCollection == null)
    {
      stages[(int)stage] = stageCollection = new ASTProcessorCollection();
    }
    return stageCollection;
  }

  public void Process(ref ASTNode rootNode)
  {
    foreach(ASTProcessorCollection stage in stages)
    {
      if(stage != null) stage.Process(ref rootNode);
    }
  }

  readonly ASTProcessorCollection[] stages = new ASTProcessorCollection[4]; // there are 4 Stage enum values
}
#endregion

#region DecoratorType
public enum DecoratorType
{
  Compiled, Interpreted
}
#endregion

#region CoreSemanticChecker
/// <summary>This processor calls <see cref="ASTNode.MarkTail"/> and <see cref="ASTNode.SetValueContext"/> on the
/// root node, and then visits each node in prefix order, calling <see cref="ASTNode.CheckSemantics"/> once on the way
/// down the tree and once more on the way back up.
/// </summary>
public class CoreSemanticChecker : PrefixVisitor
{
  /// <summary>Creates a tail marker stage with an initial tail value of true.</summary>
  public CoreSemanticChecker(DecoratorType type) : this(type, Emit.TypeWrapper.Unknown, true) { }

  /// <summary>Creates a tail marker stage with the given initial tail value.</summary>
  public CoreSemanticChecker(DecoratorType type, Emit.ITypeInfo initialContext, bool initialTail) : base(type)
  {
    this.initialContext = initialContext;
    this.initialTail    = initialTail;
  }

  public override Stage Stage
  {
    get { return Stage.Decorate; }
  }

  public override void Process(ref ASTNode rootNode)
  {
    rootNode.MarkTail(initialTail);
    rootNode.SetValueContext(initialContext);
    base.Process(ref rootNode);
  }

  protected override bool Visit(ASTNode node)
  {
    node.CheckSemantics();
    return true;
  }

  protected override void EndVisit(ASTNode node)
  {
    node.CheckSemantics2();
  }

  readonly Emit.ITypeInfo initialContext;
  readonly bool initialTail;
}
#endregion

} // namespace Scripting.AST