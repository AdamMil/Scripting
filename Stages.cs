using System;
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
    DecoratorType = type;
  }

  public abstract Stage Stage { get; }
  public abstract void Process(ref ASTNode rootNode);

  protected readonly DecoratorType DecoratorType;

  /// <summary>Adds an output message to <see cref="CompilerState"/>.</summary>
  protected virtual void AddMessage(OutputMessage message)
  {
    CompilerState.Current.Messages.Add(message);
  }

  /// <summary>Adds a new error message using the given node's source name and position.</summary>
  protected void AddErrorMessage(ASTNode node, string message)
  {
    AddMessage(new OutputMessage(OutputMessageType.Error, message, node.SourceName, node.StartPosition));
  }

  /// <summary>Adds a new error message using the given node's source name and position.</summary>
  protected void AddErrorMessage(ASTNode node, string format, params object[] args)
  {
    AddErrorMessage(node, string.Format(format, args));
  }

  /// <summary>Adds a new warning message using the given node's source name and position.</summary>
  protected void AddWarningMessage(ASTNode node, string message)
  {
    AddMessage(new OutputMessage(OutputMessageType.Warning, message, node.SourceName, node.StartPosition));
  }

  /// <summary>Adds a new warning message using the given node's source name and position.</summary>
  protected void AddWarningMessage(ASTNode node, string format, params object[] args)
  {
    AddWarningMessage(node, string.Format(format, args));
  }
}
#endregion

#region PrefixVisitor
public abstract class PrefixVisitor : ProcessorBase
{
  protected PrefixVisitor(DecoratorType type) : base(type) { }

  public override void Process(ref ASTNode rootNode)
  {
    RecursiveVisit(rootNode);
  }
  
  protected abstract bool Visit(ASTNode node);
  protected virtual void EndVisit(ASTNode node) { }

  protected void RecursiveVisit(ASTNode node)
  {
    if(Visit(node))
    {
      foreach(ASTNode child in node.Children)
      {
        RecursiveVisit(child);
      }
    }
    
    EndVisit(node);
  }
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

#region TailMarker
/// <summary>This processor simply calls <see cref="ASTNode.MarkTail"/> on the root node.</summary>
public class TailMarkerStage : IASTProcessor
{
  /// <summary>Creates a tail marker stage with an initial tail value of true.</summary>
  public TailMarkerStage() : this(true) { }

  /// <summary>Creates a tail marker stage with the given initial tail value.</summary>
  public TailMarkerStage(bool initialTail)
  {
    this.initialTail = initialTail;
  }

  public Stage Stage
  {
    get { return Stage.Decorate; }
  }

  public void Process(ref ASTNode rootNode)
  {
    rootNode.MarkTail(initialTail);
  }
  
  readonly bool initialTail;
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

} // namespace Scripting.AST