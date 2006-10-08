using System;
using System.Collections.Generic;
using System.IO;
using Scripting;

namespace Scripting.AST
{

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

} // namespace Scripting.Parsing