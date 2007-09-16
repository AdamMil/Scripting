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

/* Basic Scheme, minus continuations, plus the following extensions:
 * 
 * Strong typing:
 *    (lambda (.returns int) (a b ([int] c))
 *      ...)
 *    (let (a (b 1) ([int] c 1))
 *      ...)
 *
 * Lambda default values:
 *    (lambda (a b (c 1)) (+ a b c))
 *
 * Type casting:
 *    (let ((a 1))
 *      (.cast double a)) -> 1.0
 *    (let ((a 1.6))
 *      (.cast int a)) -> 1
 * 
 * For the above, built-in type names are the same as in C#.
 * 
 * Setting compiler options:
 *    (lambda (([int] a) ([int] b))
 *      (.option ((optimisticOperatorInlining #f))
 *        (+ a b))) ; this + will not be inlined, because we're assuming that the user may have overridden it
 *    
 *    (define addWithOverflow
 *      (lambda (a b) (.option ((checked #f)) (+ a b)))) ; this addition can overflow silently
 * 
 *    (.option ((allowRedefinition #f) (checked #f) (debug #f) (optimisticOperatorInlining #t))
 *      (define 1+ (lambda (a) (+ a 1)))  ; these functions will be as fast as possible
 *      (define 1- (lambda (a) (- a 1))))
 */

#region NetLispCompilerState
public sealed class NetLispCompilerState : CompilerState
{
  public NetLispCompilerState(NetLispLanguage language) : base(language)
  {
    // emit checked arithmetic that automatically promotes by default
    Checked = PromoteOnOverflow = true;
  }

  public NetLispCompilerState(NetLispCompilerState template) : base(template)
  {
    AllowRedefinition          = template.AllowRedefinition;
    OptimisticOperatorInlining = template.OptimisticOperatorInlining;
  }

  /// <summary>Whether the compiler allows definitions made using (define symbol value) to be changed. The compiler
  /// will not allow those declarations to be changed, either by a subsequent define, or by a set! operation, although
  /// they can be rebound or redefined in a nested scope. The benefit is that the compiler can emit strongly-typed and
  /// highly-efficient code to access those declarations, especially in the case of functions defined via
  /// (define symbol (lambda ...)), where preventing redeclaration allows the compiler to inline functions.
  /// </summary>
  public bool AllowRedefinition = true;

  /// <summary>Controls the behavior of the compiler when emitting core operators, such as +, -, eq?, etc., in a
  /// context where it is unknown whether the user has redefined them or not. If true, the compiler will optimistically
  /// assume that the user has not overridden them, and so highly-efficient type-specific versions can be emitted. If
  /// false, the compiler will assume that the user may have overridden them and emit the operator as a loosely-bound
  /// function call. In contexts where the compiler can prove that an operator has or has not been overridden, this
  /// option has no effect.
  /// </summary>
  public bool OptimisticOperatorInlining = true;
}
#endregion

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

  public override CompilerState CreateCompilerState()
  {
    return new NetLispCompilerState(this);
  }

  public override CompilerState CreateCompilerState(CompilerState currentState)
  {
    return new NetLispCompilerState((NetLispCompilerState)currentState);
  }

  public override ASTDecorator CreateDecorator(DecoratorType type)
  {
    ASTDecorator decorator = new ASTDecorator();

    if(type == DecoratorType.Compiled)
    {
      decorator.AddToEndOfStage(new VariableSlotResolver(type));
      decorator.AddToEndOfStage(new ContextMarkerStage());
    }
    decorator.AddToEndOfStage(new CoreSemanticChecker(type));

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