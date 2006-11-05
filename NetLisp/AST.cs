using System.Reflection;
using System.Reflection.Emit;
using Scripting.AST;
using Scripting.Emit;

namespace NetLisp.AST
{

public class IfNode : ASTNode
{
  public IfNode(ASTNode condition, ASTNode ifTrue, ASTNode ifFalse) : base(true)
  {
    Children.Add(condition);
    Children.Add(ifTrue);
    if(ifFalse != null) Children.Add(ifFalse);
  }
}

} // namespace NetLisp.AST