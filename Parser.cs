using System;
using System.Collections.Generic;
using System.IO;
using Scripting;

namespace Scripting.AST
{

#region IParser
/// <summary>An interface that represents a parser.</summary>
public interface IParser
{
  /// <summary>Parses an entire input into a syntax tree.</summary>
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
  /// However in some languages like LISP, everything is an expression, which makes this method equivalent to
  /// <see cref="ParseOne"/>.
  /// </remarks>
  ASTNode ParseExpression();
}
#endregion

#region ParserBase
/// <summary>A simple base class for parsers.</summary>
public abstract class ParserBase : IParser
{
  protected ParserBase(CompilerState state, IScanner scanner)
  {
    if(state == null || scanner == null) throw new ArgumentNullException();
    this.state   = state;
    this.scanner = scanner;
  }

  public abstract ASTNode ParseProgram();
  public abstract ASTNode ParseOne();
  public abstract ASTNode ParseExpression();

  protected CompilerState CompilerState
  {
    get { return state; }
  }

  protected IScanner Scanner
  {
    get { return scanner; }
  }

  /// <summary>Adds an output message to <see cref="CompilerState"/>.</summary>
  protected virtual void AddMessage(OutputMessage message)
  {
    if(message == null) throw new ArgumentNullException();
    if(CompilerState != null)
    {
      CompilerState.Messages.Add(message);
    }
    // otherwise if we have no compiler state, we can't add the message to anything, but we'll throw on error messages
    else if(message.Type == OutputMessageType.Error)
    {
      throw new SyntaxErrorException(message);
    }
  }

  /// <summary>Adds a new error message using the given source name and position.</summary>
  protected void AddErrorMessage(string sourceName, FilePosition position, string message)
  {
    AddMessage(new OutputMessage(OutputMessageType.Error, message, sourceName, position));
  }

  readonly CompilerState state;
  readonly IScanner scanner;
}
#endregion

} // namespace Scripting.Parsing