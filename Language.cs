using System;
using System.IO;
using Scripting.Runtime;

namespace Scripting.AST
{

public enum DecoratorType
{
  Compiled, Interpreted
}

/// <summary>The base class of all languages built on top of the scripting platform.</summary>
public abstract class Language
{
  public virtual CompilerState CreateCompilerState()
  {
    return new CompilerState(this);
  }

  public abstract ASTDecorator CreateDecorator(DecoratorType type);

  public virtual FunctionTemplate CreateFunctionTemplate(IntPtr funcPtr, string name, string[] parameterNames,
    Type[] parameterTypes, int numRequired, bool hasListParam, bool hasDictParam, bool hasArgClosure)
  {
    return new FunctionTemplate(funcPtr, name, parameterNames, parameterTypes, numRequired, hasListParam, hasDictParam,
                                hasArgClosure);
  }

  public abstract IParser CreateParser(CompilerState state, IScanner scanner);

  public abstract IScanner CreateScanner(CompilerState state, params string[] sourceNames);
  public abstract IScanner CreateScanner(CompilerState state, params TextReader[] sources);
  public abstract IScanner CreateScanner(CompilerState state, TextReader[] sources, string[] sourceNames);
}

} // namespace Scripting.AST