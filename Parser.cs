using System;
using System.Collections.Generic;
using System.IO;
using Scripting;

namespace Scripting.AST
{

#region IParser
/// <summary>An interface that represents a parser that creates an abstract syntax tree.</summary>
public interface IASTParser
{
  /// <summary>Parses the entire input stream into a syntax tree.</summary>
  /// <returns>A syntax tree if there is any input, or null if there is no input.</returns>
  /// <remarks>This is called to parse the entire input at once.</remarks>
  ASTNode ParseProgram();
  /// <summary>Parses a single top-level sentence from the input into a syntax tree.</summary>
  /// <returns>A syntax tree if there is any input, or null if there is no input.</returns>
  ASTNode ParseOne();
  /// <summary>Parses a single expression into a syntax tree.</summary>
  /// <returns>A syntax tree if there is any input, or null if there is no input.</returns>
  /// <remarks>An expression does not typically allow statements such as variable assignment or
  /// declarations/definitions of functions, classes, etc, and as such, this method would refuse to parse them.
  /// However in some languages, everything is an expression, which makes this method equivalent to
  /// <see cref="ParseOne"/>.
  /// </remarks>
  ASTNode ParseExpression();
}
#endregion

#region ASTParserBase
/// <summary>A simple base class for parsers that produce abstract syntax trees.</summary>
public abstract class ASTParserBase<CompilerStateType> : ParserBase<CompilerStateType>, IASTParser
  where CompilerStateType : CompilerState
{
  protected ASTParserBase(IScanner scanner) : base(scanner) { }

  public abstract ASTNode ParseProgram();
  public abstract ASTNode ParseOne();
  public abstract ASTNode ParseExpression();
}
#endregion

#region ParserBase
/// <summary>A simple base class for parsers.</summary>
public abstract class ParserBase<CompilerStateType> where CompilerStateType : CompilerState
{
  protected ParserBase(IScanner scanner)
  {
    if(scanner == null) throw new ArgumentNullException();
    this.scanner = scanner;
  }

  protected CompilerStateType CompilerState
  {
    get { return (CompilerStateType)Scripting.CompilerState.Current; }
  }

  protected IScanner Scanner
  {
    get { return scanner; }
  }

  /// <summary>Adds a new error message using the given source name and position.</summary>
  protected void AddMessage(Diagnostic diagnostic, string sourceName, FilePosition position, params object[] args)
  {
    CompilerState.Messages.Add(
      diagnostic.ToMessage(CompilerState.TreatWarningsAsErrors, sourceName, position, args));
  }

  readonly IScanner scanner;
}
#endregion

} // namespace Scripting.Parsing