/*
NetLisp is the reference implementation for a language similar to
Scheme, also called NetLisp. This implementation is both interpreted
and compiled, targetting the Microsoft .NET Framework.

http://www.adammil.net/
Copyright (C) 2007-2008 Adam Milazzo

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.
This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
*/

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

/* Basic Scheme, minus continuations, plus the following changes:
 * 
 * Optional strong typing:
 *    (lambda (.type int) (a b ([.type float] c))
 *      ...)
 *    (let ((a 0) (b 1) ([.type float] c 1))
 *      ...)
 *
 * Lambda default values:
 *    (lambda (a b (c 1)) (+ a b c))
 *    (lambda (a b ([.type int] c 1)) (+ a b c))
 *
 * For the above, built-in type names are the same as in C#.
 * 
 * Simplified libraries:
 *   list-ref, vector-ref, hash-ref, etc. just become index
 *   vector-set!, hash-set!, etc. just become index-set!
 *   char=?, string=?, fx=?, fl=?, etc. just become =
 *   fx+, fl+, string-append, etc. just become +
 *   etc...
 */

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
    CompilerState state = new CompilerState(this);
    state.Checked = state.PromoteOnOverflow = true; // we promote by default
    return state;
  }

  public override ASTDecorator CreateDecorator(DecoratorType type)
  {
    ASTDecorator decorator = new ASTDecorator();

    if(type == DecoratorType.Compiled)
    {
      decorator.AddToEndOfStage(new TopLevelScopeDecorator(type));
      decorator.AddToEndOfStage(new ScopeDecorator(type));
    }
    decorator.AddToEndOfStage(new CoreSemanticChecker(type));

    return decorator;
  }

  public override IASTParser CreateParser(IScanner scanner)
  {
    return new ASTParser(new LispParser(scanner));
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

#region NetLispDiagnostic
public static class NetLispDiagnostics
{
  // scanner
  public static readonly Diagnostic DivisionByZero        = Error(501, "Invalid number '{0}' due to division by zero");
  public static readonly Diagnostic UnknownCharacterName  = Error(502, "Invalid character name '{0}'");
  public static readonly Diagnostic EncounteredUnreadable = Error(503, "Unable to read: #<...");
  public static readonly Diagnostic UnknownNotation       = Error(504, "Unknown notation #{0}");
  public static readonly Diagnostic InvalidHexCharacter   = Error(505, "The hex string '{0}' is not a valid hex value from 0 to 10FFFF, excluding D800-DFFF.");
  public static readonly Diagnostic InvalidHexEscape      = Error(506, "The hex string '{0}' is not a valid semicolon-terminated hex value from 0 to 10FFFF, excluding D800-DFFF.");
  public static readonly Diagnostic MultipleRadixFlags    = Error(507, "The number {0} contained multiple radix flags.");
  public static readonly Diagnostic MultipleExactnessFlags = Error(508, "The number {0} contained multiple exactness flags.");
  // parser
  public static readonly Diagnostic OptionExpects         = Error(551, "Option '{0}' expects {1} value");
  public static readonly Diagnostic UnknownOption         = Error(552, "Unknown option '{0}'");
  public static readonly Diagnostic ExpectedLibraryOrImport = Error(553, "Expected (library) or (import) form");
  public static readonly Diagnostic UnexpectedDefine      = Error(554, "(define) cannot occur in an expression context");
  public static readonly Diagnostic IllegalEmptyList      = Error(555, "An empty list (ie, ()) was found. Empty lists must be quoted, like '()");
  public static readonly Diagnostic MalformedDottedList   = Error(556, "Malformed dotted list");
  public static readonly Diagnostic ExpectedSyntax        = Error(557, "Expected {0}");
  public static readonly Diagnostic UnexpectedSyntax      = Error(558, "Unexpected {0}");
  public static readonly Diagnostic SyntaxError           = Error(559, "Syntax error. {0}");

  static Diagnostic Error(int code, string format)
  {
    return Diagnostic.MakeError("NL", code, format);
  }

  static Diagnostic Warning(int code, int level, string format)
  {
    return Diagnostic.MakeWarning("NL", code, level, format);
  }
}
#endregion

} // namespace NetLisp