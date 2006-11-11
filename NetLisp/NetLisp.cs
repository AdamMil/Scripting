using System;
using System.Reflection.Emit;
using Scripting;
using Scripting.AST;
using Scripting.Emit;
using NetLisp.AST;
using NetLisp.Emit;
using NetLisp.Runtime;

namespace NetLisp
{

#region NetLispLanguage
public sealed class NetLispLanguage : Language
{
  NetLispLanguage() : base("NetLisp") { }

  public override Type FunctionTemplateType
  {
    get { return typeof(LispFunctionTemplate); }
  }

  public override CodeGenerator CreateCodeGenerator(TypeGenerator typeGen, IMethodBase method, ILGenerator ilGen)
  {
    return new LispCodeGenerator(typeGen, method, ilGen);
  }

  public override ASTDecorator CreateDecorator(DecoratorType type)
  {
    ASTDecorator decorator = new ASTDecorator();

    if(type == DecoratorType.Compiled)
    {
      decorator.AddToEndOfStage(new VariableSlotResolver(type));
      decorator.AddToEndOfStage(new TailMarkerStage());
    }

    return decorator;
  }

  public override IParser CreateParser(IScanner scanner)
  {
    return new Parser(scanner);
  }

  public override IScanner CreateScanner(params string[] sourceNames)
  {
    return new Scanner(sourceNames);
  }

  public override IScanner CreateScanner(params System.IO.TextReader[] sources)
  {
    return new Scanner(sources);
  }

  public override IScanner CreateScanner(System.IO.TextReader[] sources, string[] sourceNames)
  {
    return new Scanner(sources, sourceNames);
  }

  public static readonly NetLispLanguage Instance = new NetLispLanguage();
}
#endregion

} // namespace NetLisp