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
using System.Collections.Generic;
using System.Diagnostics;
using Scripting;
using Scripting.AST;
using Scripting.Emit;
using NetLisp.Runtime;

namespace NetLisp.AST
{

enum TokenType
{
  Literal, Symbol, Vector, LParen, RParen, LBracket, RBracket, LCurly, RCurly, Quote, BackQuote, Period, Comma, Splice,
  DatumComment, EOF
}

public interface ILispParser
{
  object ParseDatum();
  SyntaxObject ParseSyntax();
}

/* The core language of NetLisp:
 * 
 * top-level-form := <general-top-level-form>
 *                 | (#%expression <expression>)
 *                 | (begin <top-level-form>+)
 *                 | (module <identifier> <identifier> (#%plain-module-begin <module-level-form>+))
 * 
 * module-level-form := <general-top-level-form>
 *                    | (#%provide <raw-provide-spec>+)
 * 
 * general-top-level-form := <expression>
 *                         | (define-values (<identifier>+) <expression>)
 *                         | (define-syntaxes (<identifier>+) <expression>)
 *                         | (define-values-for-syntax (<identifier>+) <expression>)
 *                         | (#%require <raw-require-spec>+)
 * 
 * expression := <identifier>
 *             | <literal>
 *             | (#%lambda (.type <identifier>)? <formals> <expression>+)
 *             | (if <expression> <expression> <expression>)
 *             | (begin <expression>*)
 *             | (let-values (<binding>+) <expression>+)
 *             | (letrec-values (<binding>+) <expression>+)
 *             | (set! <identifier> <expression>)
 *             | (quote <datum>)
 *             | (quote-syntax <datum>)
 *             | (%apply <expression>+)
 *             | (%top . <identifier>)
 *             | (%try (<expression>+) <try-handlers>)
 * 
 * formals := <identifier>
 *          | (<formal-id>+)
 *          | (<formal-id>+ . <identifier>)
 * 
 * formal-id := <identifier>
 *            | ((.type <identifier>)? <identifier> <expression>?)
 * 
 * binding := ((<binding-id>+) <expression>)
 * 
 * binding-id := <identifier>
 *             | (.type <identifier> <identifier>)
 * 
 * try-handlers := (catch <identifier> <expression>*)+ (finally <expression>+)?
 *               | (finally <expression>+)
 */

#region ASTParser
public sealed class ASTParser : IASTParser
{
  public ASTParser(ILispParser parser)
  {
    if(parser == null) throw new ArgumentNullException();
    this.parser = parser;
  }

  public ASTNode ParseExpression()
  {
    return ParseExpression(Parser.ParseSyntax());
  }

  public ASTNode ParseOne()
  {
    object originalValue = Parser.ParseSyntax(), value;
    SyntaxObject syntax = GetSyntax(originalValue, out value);

    Pair pair = value as Pair;
    if(pair != null)
    {
      SyntaxObject pairSyntax = GetSyntax(syntax, pair.Car, out value);
      LispSymbol symbol = value as LispSymbol;

      if(symbol != null)
      {
        value = pair.Cdr;
        pair  = value as Pair;

        switch(symbol.Name)
        {
          case "define-values": // (define-values (<identifier>+) <expression>)
          {
            VariableNode[] variables;
            ASTNode expression;
            if(!ParseDefinition("values", pairSyntax, pair, out variables, out expression)) goto errorReturn;
            return new DefineValuesNode(variables, expression);
          }
        }
      }
    }

    return ParseExpression(originalValue);

    errorReturn: return new LiteralNode(null);
  }

  public ASTNode ParseProgram()
  {
    return ParseOne();
    throw new NotImplementedException();
  }

  ILispParser Parser
  {
    get { return parser; }
  }

  bool ParseDefinition(string defType, SyntaxObject syntax, Pair pair, out VariableNode[] variables,
                       out ASTNode expression)
  {
    variables  = null;
    expression = null;

    if(pair == null) goto syntaxError;

    object value;
    SyntaxObject varsSyntax = GetSyntax(syntax, pair.Car, out value);

    Pair varsPair = value as Pair;
    if(varsPair == null) goto syntaxError;

    variables = new VariableNode[Length(varsPair)];
    if(variables.Length == 0) AddExpectedMessage(varsSyntax, "list of variable names");
    
    for(int i=0; i<variables.Length; varsPair = (Pair)varsPair.Cdr, i++)
    {
      SyntaxObject varSyntax = GetSyntax(varsSyntax, varsPair.Car, out value);
      LispSymbol symbol = value as LispSymbol;
      string name = "missing";
      if(symbol == null) AddExpectedMessage(varSyntax, "variable name");
      else name = symbol.Name;
      variables[i] = new VariableNode(name);
    }

    pair = pair.Cdr as Pair;
    if(pair == null) goto syntaxError;

    expression = ParseExpression(pair.Car);
    return true;

    syntaxError:
    AddSyntaxError(syntax, "define-" + defType + ": must be of the form (define-" + defType +
                           " (<identifier>+) <expression>)");
    return false;
  }

  ASTNode ParseExpression(object value)
  {
    SyntaxObject syntax = GetSyntax(value, out value);

    if(value == LispOps.EOF)
    {
      AddMessage(null, CoreDiagnostics.UnexpectedEOF);
    }
    else if(value is LispSymbol) // the expression is a variable reference
    {
      LispSymbol sym = (LispSymbol)value;
      return Decorate(syntax, new VariableNode(sym.Name));
    }
    else if(value is Pair) // the expression is a basic form
    {
      Pair pair = (Pair)value;

      SyntaxObject symSyntax = GetSyntax(syntax, pair.Car, out value);

      LispSymbol symbol = value as LispSymbol;
      if(symbol == null)
      {
        AddExpectedMessage(symSyntax, "symbol");
        goto errorReturn;
      }

      value = pair.Cdr;
      pair  = value as Pair;

      switch(symbol.Name)
      {
        #region %apply
        case "%apply": // (%apply <expression>+)
        {
          if(pair == null)
          {
            AddExpectedMessage(syntax, "%apply: must be of the form (%apply <expression> <expression>*)");
            goto errorReturn;
          }

          ASTNode method = ParseExpression(pair.Car);
          pair = pair.Cdr as Pair;

          ASTNode[] arguments = new ASTNode[Length(pair)];
          for(int i=0; i<arguments.Length; pair = (Pair)pair.Cdr, i++) arguments[i] = ParseExpression(pair.Car);
          return Decorate(syntax, new CallNode(method, arguments));
        }
        #endregion

        #region quote
        case "quote": // (quote <datum>)
          if(pair == null || pair.Cdr != null)
          {
            AddSyntaxError(syntax, "quote: must be of the form (quote form)");
            goto errorReturn;
          }
          return Decorate(syntax, Quote(pair.Car));
        #endregion

        #region if
        case "if": // (if <expression> <expression> <expression>)
        {
          ASTNode condition, ifTrue, ifFalse;

          if(pair == null) goto wrongArity;
          condition = ParseExpression(pair.Car);

          pair = pair.Cdr as Pair;
          if(pair == null) goto wrongArity;
          ifTrue = ParseExpression(pair.Car);

          pair = pair.Cdr as Pair;
          if(pair == null) ifFalse = null;
          else
          {
            ifFalse = ParseExpression(pair.Car);
            if(pair.Cdr != null) goto wrongArity;
          }

          return new IfNode(condition, ifTrue, ifFalse);

          wrongArity:
          AddSyntaxError(syntax, "if: must be of the form (if <conditional> <true-expr> <false-expr>)");
          goto errorReturn;
        }
        #endregion

        #region let-values
        case "let-values": // (let-values (<binding>+) <expression>+)
        {
          LetValuesNode.Binding[] bindingList;
          ASTNode body;
          if(ParseLetBindings(pair, syntax, out bindingList, out body))
          {
            return new LetValuesNode(bindingList, body);
          }
          else
          {
            AddSyntaxError(syntax, "let-values: must be of the form (let-values (binding ...) form ...) where binding "+
                                   "is ((bindingvar ...) form) and bindingvar is a symbol or (.type symbol symbol)");
            goto errorReturn;
          }
        }
        #endregion

        #region begin
        case "begin": // (begin <expression>*)
          if(pair == null) return Decorate(syntax, new VoidNode());
          else if(pair.Cdr == null) return Decorate(syntax, ParseExpression(pair.Car));
          else return Decorate(syntax, ParseBody(pair));
        #endregion

        #region #%lambda
        case "#%lambda": // (#%lambda (.type <identifier>)? <formals> <expression>+)
        {
          ITypeInfo returnType = TypeWrapper.Unknown;
          if(pair == null) goto syntaxError;

          // process the return type, if it exists
          SyntaxObject typeSyntax = GetSyntax(syntax, pair.Car, out value);
          Pair typePair = value as Pair;
          if(typePair != null)
          {
            // if the return type is specified, extract it
            symbol = GetValue(typePair.Car) as LispSymbol;

            if(symbol != null && string.Equals(symbol.Name, ".type", StringComparison.Ordinal))
            {
              typePair = typePair.Cdr as Pair;
              if(typePair == null)
              {
                AddExpectedMessage(typeSyntax, "type name");
                goto errorReturn;
              }

              typeSyntax = GetSyntax(typeSyntax, typePair.Car, out value);
              symbol = value as LispSymbol;
              if(symbol == null || !ParseType(symbol.Name, out returnType))
              {
                AddExpectedMessage(typeSyntax, "valid type name");
                goto errorReturn;
              }

              pair = pair.Cdr as Pair; // advance to the formals
            }
          }

          // process the list of formals
          SyntaxObject formalsSyntax = GetSyntax(syntax, pair.Car, out value);
          ParameterNode[] parameters;
          if(value is LispSymbol) // if a single variable is to receive all parameters...
          {
            parameters = new ParameterNode[]
            {
              new ParameterNode(((LispSymbol)value).Name, LispTypes.Pair, ParameterType.List)
            };
          }
          else if(value is Pair)
          {
            Dictionary<string,object> nameDict = new Dictionary<string,object>(StringComparer.Ordinal);
            Pair formalPair = (Pair)value;
            parameters = new ParameterNode[Length(formalPair, true)];
            object formalValue = formalPair.Car;
            bool isDotted = false;
            for(int i=0; i<parameters.Length; i++)
            {
              SyntaxObject formalSyntax = GetSyntax(syntax, formalValue, out value);
              ParameterNode parameter;

              symbol = value as LispSymbol;
              if(symbol != null) // if the formal is just an identifier name...
              {
                if(nameDict.ContainsKey(symbol.Name))
                {
                  AddMessage(formalSyntax, CoreDiagnostics.ParameterRedefined, symbol.Name);
                }
                else nameDict.Add(symbol.Name, null);

                parameter = isDotted ? new ParameterNode(symbol.Name, TypeWrapper.Unknown, ParameterType.List, null)
                                     : new ParameterNode(symbol.Name);
                parameters[i] = Decorate(formalSyntax, parameter);
              }
              else if(value is Pair) // it's (hopefully) of the form ((.type <identifier>)? <identifier> <expression>?)
              {
                Pair paramPair = (Pair)value;
                SyntaxObject paramSyntax = GetSyntax(formalSyntax, paramPair.Car, out value);
                ITypeInfo paramType = TypeWrapper.Unknown;
                string paramName = "missing";
                ASTNode defaultValue = null;

                if(value is Pair)
                {
                  typePair = (Pair)value;
                  typeSyntax = GetSyntax(paramSyntax, typePair.Car, out value);
                  symbol = value as LispSymbol;
                  if(symbol == null || !string.Equals(symbol.Name, ".type", StringComparison.Ordinal))
                  {
                    AddExpectedMessage(typeSyntax, ".type");
                  }
                  else
                  {
                    typePair = typePair.Cdr as Pair;
                    if(typePair == null) AddExpectedMessage(typeSyntax, "type name");
                    else
                    {
                      typeSyntax = GetSyntax(paramSyntax, typePair.Car, out value);
                      symbol = value as LispSymbol;
                      if(symbol == null) AddExpectedMessage(typeSyntax, "type name");
                      else if(!ParseType(symbol.Name, out paramType))
                      {
                        AddExpectedMessage(typeSyntax, "valid type name");
                      }
                    }
                  }

                  paramPair = paramPair.Cdr as Pair;
                }

                SyntaxObject nameSyntax = paramSyntax;
                if(paramPair == null) AddExpectedMessage(paramSyntax, "parameter name");
                else
                {
                  nameSyntax = GetSyntax(paramSyntax, paramPair.Car, out value);
                  symbol = value as LispSymbol;
                  if(symbol == null) AddExpectedMessage(nameSyntax, "parameter name");
                  else paramName = symbol.Name;

                  paramPair = paramPair.Cdr as Pair;
                  if(paramPair != null)
                  {
                    defaultValue = ParseExpression(paramPair.Car);
                    paramPair = paramPair.Cdr as Pair;
                    if(paramPair != null) AddExpectedMessage(GetSyntax(paramPair.Car, out value), ")");
                  }
                }

                if(nameDict.ContainsKey(paramName))
                {
                  AddMessage(nameSyntax, CoreDiagnostics.ParameterRedefined, paramName);
                }
                else nameDict.Add(paramName, null);

                parameters[i] = Decorate(paramSyntax,
                                         new ParameterNode(paramName, paramType, ParameterType.Normal, defaultValue));
              }
              else
              {
                AddExpectedMessage(formalSyntax, isDotted ? "lambda list parameter" : "lambda parameter");
                parameters[i] = new ParameterNode("missing");
              }
              
              if(formalPair.Cdr is Pair)
              {
                formalPair = (Pair)formalPair.Cdr;
                formalValue = formalPair.Car;
              }
              else
              {
                formalValue = formalPair.Cdr;
                isDotted = true;
              }
            }
          }
          else goto syntaxError;

          // process the lambda body
          pair = pair.Cdr as Pair;
          if(pair == null) goto syntaxError;
          return Decorate(syntax, new ScriptFunctionNode(null, returnType, parameters, ParseBody(pair)));

          syntaxError:
          AddSyntaxError(syntax, "lambda: must be of the form (lambda (.type <identifier>)? <formals> <expression>+)");
          goto errorReturn;
        }
        #endregion

        #region set!
        case "set!": // (set! <identifier> <expression>)
        {
          if(pair == null) goto syntaxError;

          SyntaxObject nameSyntax = GetSyntax(syntax, pair.Car, out value);
          LispSymbol name = value as LispSymbol;
          if(name == null) goto syntaxError;

          pair = pair.Cdr as Pair;
          if(pair == null) goto syntaxError;

          AssignableNode varNode = Decorate(nameSyntax, new VariableNode(name.Name));
          return Decorate(syntax, new AssignNode(varNode, ParseExpression(pair.Car)));
          
          syntaxError:
          AddSyntaxError(syntax, "set!: must be of the form (set! <symbol> <expression>)");
          goto errorReturn;
        }
        #endregion

        #region letrec-values
        case "letrec-values": // (letrec-values (<binding>+) <expression>+)
        {
          LetValuesNode.Binding[] bindingList;
          ASTNode body;
          if(ParseLetBindings(pair, syntax, out bindingList, out body))
          {
            return new LetRecValuesNode(bindingList, body);
          }
          else
          {
            AddSyntaxError(syntax, "letrec-values: must be of the form (letrec-values (binding ...) form ...) where "+
                                   "binding is ((bindingvar ...) form) and bindingvar is a symbol or "+
                                   "(.type symbol symbol)");
            goto errorReturn;
          }
        }
        #endregion

        default:
          AddUnexpectedMessage(symSyntax, "symbol '" + symbol.Name + "'. Expected core NetLisp expression symbol.");
          goto errorReturn;
      }
    }
    else
    {
      return new LiteralNode(syntax.Value);
    }

    errorReturn:
    return new LiteralNode(null);
  }

  ASTNode ParseBody(Pair pair)
  {
    if(pair.Cdr == null) return ParseExpression(pair.Car);

    BlockNode block = new BlockNode();
    do
    {
      block.Children.Add(ParseExpression(pair.Car));

      Pair nextPair = pair.Cdr as Pair;
      if(nextPair == null && pair.Cdr != null)
      {
        AddUnexpectedMessage(pair.Cdr as SyntaxObject, "dotted list item");
      }
      pair = nextPair;
    } while(pair != null);

    return block;
  }

  bool ParseLetBindings(Pair pair, SyntaxObject syntax, out LetValuesNode.Binding[] bindingList, out ASTNode body)
  {
    bindingList = null;
    body = null;

    if(pair == null) return false;

    Pair bindings = GetValue(pair.Car) as Pair;
    if(bindings == null) return false;

    Dictionary<string,object> nameDict = new Dictionary<string,object>(StringComparer.Ordinal);
    VariableNode[] vars = null;
    bindingList = new LetValuesNode.Binding[Length(bindings)];

    for(int i=0; i<bindingList.Length; bindings=(Pair)bindings.Cdr, i++)
    {
      Pair binding = GetValue(bindings.Car) as Pair;
      if(binding == null) return false;

      object value;
      SyntaxObject bindSyntax = GetSyntax(syntax, binding.Car, out value);

      Pair namePair = value as Pair;
      if(namePair == null)
      {
        AddExpectedMessage(bindSyntax, "list of variable names");
        return false;
      }

      int varCount = Length(namePair);
      if(vars == null || vars.Length != varCount) vars = new VariableNode[varCount];

      for(int j=0; j < vars.Length; namePair=(Pair)namePair.Cdr, j++)
      {
        SyntaxObject nameSyntax = GetSyntax(bindSyntax, namePair.Car, out value);
        LispSymbol name;
        ITypeInfo type;

        Pair typePair = value as Pair;
        if(typePair == null)
        {
          name = value as LispSymbol;
          type = TypeWrapper.Unknown;
        }
        else
        {
          SyntaxObject typeSyntax = GetSyntax(nameSyntax, typePair.Car, out value);

          LispSymbol typeSym = value as LispSymbol;
          if(typeSym == null || !string.Equals(typeSym.Name, ".type", StringComparison.Ordinal))
          {
            AddExpectedMessage(typeSyntax, ".type");
            return false;
          }

          typePair = typePair.Cdr as Pair;
          if(typePair == null)
          {
            AddExpectedMessage(typeSyntax, "type name");
            return false;
          }

          bindSyntax = GetSyntax(typeSyntax, typePair.Car, out value);
          typeSym = value as LispSymbol;
          if(typeSym == null)
          {
            AddExpectedMessage(typeSyntax, "type name");
            return false;
          }

          if(!ParseType(typeSym.Name, out type))
          {
            AddSyntaxError(typeSyntax, "invalid type name: " + typeSym.Name);
            return false;
          }

          typePair = typePair.Cdr as Pair;
          if(typePair == null) return false;

          nameSyntax = GetSyntax(typeSyntax, typePair.Car, out value);
          name = value as LispSymbol;
        }

        if(name == null)
        {
          AddExpectedMessage(nameSyntax, "variable name");
          return false;
        }

        if(nameDict.ContainsKey(name.Name))
        {
          AddMessage(nameSyntax, CoreDiagnostics.VariableRedefined, name.Name);
        }
        else
        {
          nameDict.Add(name.Name, null);
        }

        vars[j] = Decorate(nameSyntax, new VariableNode(name.Name, new LocalProxySlot(name.Name, type)));
      }

      binding = binding.Cdr as Pair;
      if(binding == null)
      {
        AddExpectedMessage(bindSyntax, "initialization form");
        return false;
      }
      else if(binding.Cdr != null)
      {
        AddUnexpectedMessage(bindSyntax, "too many forms in initialization");
        return false;
      }

      bindingList[i] = new LetValuesNode.Binding(new MultipleVariableNode(vars), ParseExpression(binding.Car));
    }

    if(bindings != null) return false;

    pair = pair.Cdr as Pair;
    if(pair == null)
    {
      AddExpectedMessage(syntax, "body forms");
      return false;
    }

    body = ParseBody(pair);
    return true;
  }

  /*  ASTNode ParseOptions()
    {
      FilePosition start = token.Start;

      NextToken();
      Consume(TokenType.LParen); // start of the options
      //NetLispCompilerState state = (NetLispCompilerState)CompilerState.Language.CreateCompilerState(CompilerState);

      while(!TryConsume(TokenType.RParen))
      {
        if(!TryConsume(TokenType.LParen))
        {
          AddExpectedMessage("option pair");
          if(!TokenIs(TokenType.RParen)) NextToken(); // attempt a lame recovery
        }
        else // it's an option/value pair
        {
          string optionName = ParseSymbolName();
          LiteralNode value = ParseOne() as LiteralNode;
          if(value == null) AddExpectedMessage("literal option value");

          switch(optionName)
          {
            case "checked":
              if(value.Value is bool) state.Checked = state.PromoteOnOverflow = (bool)value.Value;
              else AddMessage(NetLispDiagnostics.OptionExpects, optionName, "boolean");
              break;
            case "debug":
              if(value.Value is bool) state.Debug = (bool)value.Value;
              else AddMessage(NetLispDiagnostics.OptionExpects, optionName, "boolean");
              break;
            case "optimize":
              if(value.Value is bool) state.Optimize = (bool)value.Value;
              else AddMessage(NetLispDiagnostics.OptionExpects, optionName, "boolean");
              break;
            case "allowRedefinition":
              if(value.Value is bool) state.AllowRedefinition = (bool)value.Value;
              else AddMessage(NetLispDiagnostics.OptionExpects, optionName, "boolean");
              break;
            case "optimisticInlining":
              if(value.Value is bool) state.OptimisticInlining = (bool)value.Value;
              else AddMessage(NetLispDiagnostics.OptionExpects, optionName, "boolean");
              break;
            default:
              AddMessage(NetLispDiagnostics.UnknownOption, optionName);
              break;
          }

          Consume(TokenType.RParen);
        }
      } 

      return new OptionsNode(state, ParseBody());
    }

    ParameterNode[] ParseLambdaList(out ITypeInfo returnType)
    {
      returnType = TypeWrapper.Unknown;
    
      retry:
      if(TryConsume(TokenType.LParen)) // there's either a return type or a list of parameters
      {
        if(returnType == TypeWrapper.Unknown && TokenIs(TokenType.Symbol) && // it's a return type and we don't already
           (string)token.Value == ".returns")                                // have one, read it and restart the check
        {
          NextToken();
          returnType = ParseType();
          Consume(TokenType.RParen);
          goto retry;
        }

        // it's a list of parameters
        List<ParameterNode> parameters = new List<ParameterNode>();
        bool hasListParam = false;
        while(!TryConsume(TokenType.RParen) && !hasListParam)
        {
          if(TryConsume(TokenType.Period)) // a dotted list
          {
            parameters.Add(new ParameterNode(ParseSymbolName(), LispTypes.Pair, ParameterType.List));
            hasListParam = true;
          }
          else if(TryConsume(TokenType.LParen)) // a type and name
          {
            ITypeInfo valueType = ParseType();
            parameters.Add(new ParameterNode(ParseSymbolName(), valueType, ParameterType.Normal));
            Consume(TokenType.RParen);
          }
          else // just a name
          {
            parameters.Add(new ParameterNode(ParseSymbolName(), ParameterType.Normal));
          }
        }
        return parameters.ToArray();
      }
      else if(TokenIs(TokenType.Symbol)) // a single symbol that receives a list of arguments
      {
        return new ParameterNode[] { new ParameterNode(ParseSymbolName(), ParameterType.List) };
      }
      else
      {
        AddExpectedMessage("return type or parameter list");
        return new ParameterNode[0];
      }
    }

    string ParseSymbolName()
    {
      Expect(TokenType.Symbol);
      string name = (string)token.Value;
      NextToken();
      return name;
    }*/

  void AddMessage(SyntaxObject syntax, Diagnostic diagnostic, params object[] args)
  {
    CompilerState.Current.Messages.Add(syntax == null ?
      diagnostic.ToMessage(CompilerState.Current.TreatWarningsAsErrors, args) :
      diagnostic.ToMessage(CompilerState.Current.TreatWarningsAsErrors, syntax.SourceFile, syntax.Start, args));
  }

  void AddExpectedMessage(SyntaxObject syntax, string expected)
  {
    AddMessage(syntax, NetLispDiagnostics.ExpectedSyntax, expected);
  }

  void AddUnexpectedMessage(SyntaxObject syntax, string unexpected)
  {
    AddMessage(syntax, NetLispDiagnostics.UnexpectedSyntax, unexpected);
  }

  void AddSyntaxError(SyntaxObject syntax, string error)
  {
    AddMessage(syntax, NetLispDiagnostics.SyntaxError, error);
  }

  readonly ILispParser parser;

  static T Decorate<T>(SyntaxObject syntax, T node) where T : ASTNode
  {
    if(syntax != null)
    {
      node.StartPosition = syntax.Start;
      node.EndPosition   = syntax.End;
      node.SourceName    = syntax.SourceFile;
    }

    return node;
  }

  static object GetValue(object item)
  {
    SyntaxObject syntax = item as SyntaxObject;
    return syntax != null ? syntax.Value : item;
  }

  static SyntaxObject GetSyntax(SyntaxObject parentSyntax, object item, out object value)
  {
    SyntaxObject syntax = item as SyntaxObject;
    if(syntax == null)
    {
      value = item;
      return parentSyntax;
    }
    else
    {
      value = syntax.Value;
      return syntax;
    }
  }

  static SyntaxObject GetSyntax(object item, out object value)
  {
    SyntaxObject syntax = item as SyntaxObject;
    value = syntax == null ? item : syntax.Value;
    return syntax;
  }

  static int Length(Pair list)
  {
    return Length(list, false);
  }

  static int Length(Pair list, bool includeDotted)
  {
    int length = 0;
    while(list != null)
    {
      length++;
      Pair nextPair = list.Cdr as Pair;
      if(nextPair == null && includeDotted && list.Cdr != null) length++;
      list = nextPair;
    }
    return length;
  }

  static object ParseQuotedForm(object item)
  {
    item = GetValue(item);

    Pair pair = item as Pair;
    if(pair == null) return item;

    List<object> items = new List<object>();
    object dottedItem = null;

    do
    {
      items.Add(ParseQuotedForm(pair.Car));

      object next = pair.Cdr;
      pair = next as Pair;

      if(pair == null && next != null) dottedItem = ParseQuotedForm(next);
    } while(pair != null);

    return LispOps.List(items, dottedItem);
  }

  static bool ParseType(string typeName, out ITypeInfo type)
  {
    switch(typeName)
    {
      case "bool": type = TypeWrapper.Bool; break;
      case "byte": type = TypeWrapper.Byte; break;
      case "char": type = TypeWrapper.Char; break;
      case "complex": type = TypeWrapper.Complex; break;
      case "double": type = TypeWrapper.Double; break;
      case "function": type = TypeWrapper.ICallable; break;
      case "int": type = TypeWrapper.Int; break;
      case "integer": type = TypeWrapper.Integer; break;
      case "list": type = LispTypes.Pair; break;
      case "long": type = TypeWrapper.Long; break;
      case "object": type = TypeWrapper.Object; break;
      case "sbyte": type = TypeWrapper.SByte; break;
      case "short": type = TypeWrapper.Short; break;
      case "string": type = TypeWrapper.String; break;
      case "float": type = TypeWrapper.Single; break;
      case "uint": type = TypeWrapper.UInt; break;
      case "ulong": type = TypeWrapper.ULong; break;
      case "ushort": type = TypeWrapper.UShort; break;
      default:
        type = null; // TODO: figure out a way to allow references to imported types
        return false;
    }

    return true;
  }

  static ASTNode Quote(object item)
  {
    SyntaxObject syntax = GetSyntax(item, out item);
    return Decorate(syntax, new LiteralNode(ParseQuotedForm(item)));
  }
}
#endregion

#region LispParser
public sealed class LispParser : ParserBase<CompilerState>, ILispParser
{
  public LispParser(IScanner scanner) : base(scanner)
  {
    NextToken();
  }

  static LispParser()
  {
    string[] names = Enum.GetNames(typeof(TokenType));
    TokenType[] types = (TokenType[])Enum.GetValues(typeof(TokenType));

    tokenMap = new Dictionary<string, TokenType>(names.Length);
    for(int i=0; i<names.Length; i++) tokenMap[names[i].ToUpperInvariant()] = types[i];
  }

  public object ParseDatum()
  {
    return ParseDatum(false);
  }

  public SyntaxObject ParseSyntax()
  {
    object value = ParseDatum(true);
    return value == LispOps.EOF ? null : (SyntaxObject)value;
  }

  object ParseDatum(bool returnSyntax)
  {
    while(TokenIs(TokenType.DatumComment))
    {
      NextToken();
      ParseOne(false);
    }

    return TokenIs(TokenType.EOF) ? LispOps.EOF : ParseOne(returnSyntax);
  }

  object ParseOne(bool returnSyntax)
  {
    FilePosition start = token.Start;
    object value;

    retry:
    switch(tokenType)
    {
      case TokenType.Symbol:
      {
        string name = (string)token.Value;
        NextToken();
        value = LispSymbol.Get(name);
        break;
      }

      case TokenType.LParen: case TokenType.LBracket:
      {
        TokenType end = tokenType == TokenType.LParen ? TokenType.RParen : TokenType.RBracket;
        if(NextToken() == end)
        {
          // TODO: AddMessage(NetLispDiagnostics.IllegalEmptyList, start);
          NextToken();
          value = null;
        }
        else
        {
          List<object> items = new List<object>();
          object dottedItem = null;
          bool foundDottedItem = false;
          do
          {
            items.Add(ParseOne(returnSyntax));
            if(TryConsume(TokenType.Period))
            {
              dottedItem = ParseOne(returnSyntax);
              foundDottedItem = true;
              break;
            }
          } while(!TryConsume(end) && tokenType != TokenType.EOF);

          if(items.Count == 0 && foundDottedItem) AddMessage(NetLispDiagnostics.MalformedDottedList, start);
          value = LispOps.List(items, dottedItem);
        }
        break;
      }

      case TokenType.Literal:
        value = token.Value;
        NextToken();
        break;

      case TokenType.Quote:
        NextToken();
        value = LispOps.List(quoteSym, ParseOne(returnSyntax));
        break;

      case TokenType.Comma:
        NextToken();
        value = LispOps.List(unquoteSym, ParseOne(returnSyntax));
        break;

      case TokenType.Splice:
        NextToken();
        value = LispOps.List(spliceSym, ParseOne(returnSyntax));
        break;

      case TokenType.BackQuote:
        NextToken();
        value = LispOps.List(quasiSym, ParseOne(returnSyntax));
        break;

      case TokenType.Vector:
      {
        NextToken();
        List<object> items = new List<object>();
        while(!TryConsume(TokenType.RParen)) items.Add(ParseOne(returnSyntax));
        value = new Pair(vectorSym, LispOps.List(items));
        break;
      }

      case TokenType.DatumComment:
        NextToken();
        return ParseOne(returnSyntax);

      case TokenType.EOF:
        AddMessage(CoreDiagnostics.UnexpectedEOF);
        throw SyntaxError();

      default:
        AddMessage(CoreDiagnostics.UnexpectedToken, tokenType);
        NextToken();
        goto retry;
    }

    if(returnSyntax) value = new SyntaxObject(token.SourceName, start, lastEndPosition, value);

    return value;
  }

  void Consume(TokenType type)
  {
    Expect(type);
    NextToken();
  }

  void Expect(TokenType type)
  {
    if(!TokenIs(type))
    {
      if(TokenIs(TokenType.EOF))
      {
        Unexpected(TokenType.EOF);
      }
      else
      {
        AddExpectedMessage(type.ToString());
        throw SyntaxError();
      }
    }
  }

  TokenType NextToken()
  {
    lastEndPosition = token.End;

    if(!Scanner.NextToken(out token))
    {
      token.Type = "EOF";
      tokenType  = TokenType.EOF;
    }
    else
    {
      tokenType = tokenMap[token.Type];
    }

    return tokenType;
  }

  void AddMessage(Diagnostic diagnostic, params object[] args)
  {
    AddMessage(diagnostic, token.Start, args);
  }

  void AddMessage(Diagnostic diagnostic, FilePosition position, params object[] args)
  {
    CompilerState.Messages.Add(
      diagnostic.ToMessage(CompilerState.TreatWarningsAsErrors, token.SourceName, position, args));
  }

  SyntaxErrorException SyntaxError()
  {
    return new SyntaxErrorException(CompilerState.Messages[CompilerState.Messages.Count-1]);
  }

  bool TokenIs(TokenType type)
  {
    return tokenType == type;
  }

  bool TryConsume(TokenType type)
  {
    if(TokenIs(type))
    {
      NextToken();
      return true;
    }
    else
    {
      return false;
    }
  }

  void AddExpectedMessage(string expected)
  {
    AddMessage(CoreDiagnostics.ExpectedSyntax, expected, token.Type);
  }

  void Unexpected()
  {
    Unexpected(tokenType);
  }

  void Unexpected(TokenType type)
  {
    if(type == TokenType.EOF)
    {
      AddMessage(CoreDiagnostics.UnexpectedEOF);
    }
    else
    {
      AddMessage(CoreDiagnostics.UnexpectedToken, token.Type);
    }
    throw SyntaxError();
  }

  Token token;
  TokenType tokenType;
  FilePosition lastEndPosition;

  static readonly LispSymbol quasiSym = LispSymbol.Get("quasiquote"), quoteSym = LispSymbol.Get("quote");
  static readonly LispSymbol spliceSym = LispSymbol.Get("unquote-splicing"), unquoteSym = LispSymbol.Get("unquote");
  static readonly LispSymbol vectorSym = LispSymbol.Get("vector");
  static readonly Dictionary<string,TokenType> tokenMap;
}
#endregion

#region SyntaxObject
public sealed class SyntaxObject
{
  public SyntaxObject(string source, FilePosition start, FilePosition end, object value)
  {
    SourceFile = source;
    Start      = start;
    End        = end;
    Value      = value;
  }

  public readonly object Value;
  public readonly string SourceFile;
  public readonly FilePosition Start, End;
}
#endregion

} // namespace NetLisp.AST