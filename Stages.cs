using System;
using System.Collections.ObjectModel;

namespace Scripting.AST
{

#region IASTProcessor
public interface IASTProcessor
{
  Stage Stage { get; }
  void Process(CompilerState state, ref ASTNode rootNode);
}
#endregion

#region ASTProcessorCollection
public sealed class ASTProcessorCollection : Collection<IASTProcessor>
{
  public void Process(CompilerState state, ref ASTNode rootNode)
  {
    foreach(IASTProcessor stage in this)
    {
      stage.Process(state, ref rootNode);
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
  /// a form that correctly represents the program.
  /// </summary>
  PreDecorate,
  /// <summary>This stage operates upon a parse tree that is correct, but not decorated. Its responsibilies are to
  /// decorate the tree (ensure that every <see cref="VariableNode"/> has the correct slot, ensure that
  /// <see cref="ASTNode.MarkTail">tails are marked</see>, etc).
  /// </summary>
  Decorate,
  /// <summary>This stage operates upon a parse tree that is fully decorated, but not yet optimized. Its
  /// responsibilities are to optimize the tree while maintaining correctness and proper decoration.
  /// </summary>
  Decorated,
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

  public void Process(CompilerState state, ref ASTNode rootNode)
  {
    rootNode.MarkTail(initialTail);
  }
  
  readonly bool initialTail;
}
#endregion

#region ASTDecorator
public sealed class ASTDecorator
{
  public void AddStageToBeginning(IASTProcessor stage)
  {
    GetStageList(stage.Stage).Insert(0, stage);
  }

  public void AddStageToEnd(IASTProcessor stage)
  {
    GetStageList(stage.Stage).Add(stage);
  }

  public ASTProcessorCollection GetStageList(Stage stage)
  {
    ASTProcessorCollection stageCollection = stages[(int)stage];
    if(stageCollection == null)
    {
      stages[(int)stage] = stageCollection = new ASTProcessorCollection();
    }
    return stageCollection;
  }

  public void Process(CompilerState state, ref ASTNode rootNode)
  {
    foreach(ASTProcessorCollection stage in stages)
    {
      if(stage != null) stage.Process(state, ref rootNode);
    }
  }

  readonly ASTProcessorCollection[] stages = new ASTProcessorCollection[4]; // there are 4 Stage enum values
}
#endregion

} // namespace Scripting.AST