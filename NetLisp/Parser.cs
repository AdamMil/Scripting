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
  Literal, Symbol, Vector, LParen, RParen, LBracket, RBracket, LCurly, RCurly, Quote, BackQuote, Period, EOF
}

public class Parser : ParserBase<NetLispCompilerState>
{
  public Parser(IScanner scanner) : base(scanner)
  {
    NextToken(); // load the first token
  }
  
  static Parser()
  {
    string[] names = Enum.GetNames(typeof(TokenType));
    TokenType[] types = (TokenType[])Enum.GetValues(typeof(TokenType));

    tokenMap = new System.Collections.Hashtable(names.Length);
    for(int i=0; i<names.Length; i++)
    {
      tokenMap[names[i].ToUpperInvariant()] = types[i];
    }
  }

  public override ASTNode ParseExpression()
  {
    return ParseOne();
  }

  public override ASTNode ParseOne()
  {
    FilePosition start = token.Start;
    ASTNode ret = null;

    switch(tokenType)
    {
      #region LParen
      case TokenType.LParen: // a list of items (eg, (a b c))
      {
        NextToken();
        if(tokenType == TokenType.RParen) // empty list (eg, ()) --> nil
        {
          ret = new LiteralNode(null);
        }
        else if(quoted) // a quoted list, to be taken literally
        {
          ret = new ListNode(ParseNodeList());
        }
        else
        {
          ASTNode function = null;

          if(TokenIs(TokenType.Symbol))
          {
            switch((string)token.Value) // see if it's a built-in
            {
              case "quote": // (quote a) --> a
                ret = ParseNextQuoted();
                break;

              case "if": // (if condition ifTrue [ifFalse])
                NextToken();
                ret = new IfNode(ParseOne(), ParseOne(), TokenIs(TokenType.RParen) ? null : ParseOne());
                break;

              case "let": // (let (binding ...) body) where binding is identifier or (identifier init)
                ret = ParseLet();
                break;
            
              case "begin": // (begin form ...)
                NextToken();
                ret = ParseBody();
                break;
              
              case "lambda": // (lambda (.returns type)? params form ...) where params is a symbol or a possibly-dotted list of symbols
              {
                NextToken();
                ITypeInfo returnType;
                ParameterNode[] parameters = ParseLambdaList(out returnType);
                ret = new ScriptFunctionNode(null, returnType, parameters, ParseBody());
                break;
              }

              case "set!": // (set! symbol form [symbol form ...])
                ret = ParseSet();
                break;

              case "define": // (define symbol value)
              {
                NextToken();
                ret = new DefineNode(ParseSymbolName(), ParseOne());
                break;
              }

              case "vector":
                NextToken();
                ret = new VectorNode(ParseNodeList());
                break;
              
              case ".cast":
                NextToken();
                ret = new UnsafeCastNode(ParseType(), ParseExpression());
                break;

              case ".options": // (.options ((option value) ...) form ...)
                ret = ParseOptions();
                break;

              default: // it's a function call with a symbol referencing the function
                function = new VariableNode(ParseSymbolName());
                break;
            }
          }

          if(ret == null) // if ret is still null by this point, it means we have a function call
          {
            if(function == null) // if function is null, it's not a symbol, so we'll parse it.
            {
              function = ParseOne();
            }

            ret = new CallNode(function, ParseNodeList());
          }
        }

        Consume(TokenType.RParen);
        break;
      }
      #endregion
      
      case TokenType.Symbol:
        if(quoted)
        {
          ret = new SymbolNode(ParseSymbolName());
        }
        else
        {
          ret = new VariableNode(ParseSymbolName());
        }
        break;

      case TokenType.Literal:
        ret = new LiteralNode(token.Value);
        NextToken();
        break;

      case TokenType.Quote: // a quote, eg 'a
        if(quoted) // transform (quote 'a) into (quote (quote a))
        {
          ret = new ListNode(new SymbolNode("quote"), ParseOne());
        }
        else
        {
          ret = ParseNextQuoted();
        }
        break;
      
      case TokenType.BackQuote:
        throw new NotImplementedException();
      
      case TokenType.Vector:
        NextToken();
        if(quoted)
        {
          ListNode list = new ListNode();
          list.ListItems.Add(new SymbolNode("vector"));
          list.ListItems.AddRange(ParseNodeList());
          ret = list;
        }
        else
        {
          ret = new VectorNode(ParseNodeList());
        }
        Consume(TokenType.RParen);
        break;

      case TokenType.EOF:
        Unexpected(tokenType);
        break;

      default:
        AddMessage(CoreDiagnostics.UnexpectedToken, token.Type);
        NextToken();
        ret = new LiteralNode(null);
        break;
    }

    return ret;
  }

  public override ASTNode ParseProgram()
  {
    BlockNode body = new BlockNode();
    while(!TokenIs(TokenType.EOF))
    {
      body.Children.Add(ParseOne());
    }
    return body;
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
      tokenType = (TokenType)tokenMap[token.Type];
    }

    return tokenType;
  }

  ASTNode ParseBody()
  {
    ASTNode body = new BlockNode();
    do
    {
      body.Children.Add(ParseOne());
    } while(!TokenIs(TokenType.RParen));
    return body;
  }

  ASTNode ParseLet()
  {
    Debug.Assert(TokenIs(TokenType.Symbol) && (string)token.Value == "let"); // we're positioned at the 'let' symbol

    FilePosition start = token.Start;

    NextToken();
    Consume(TokenType.LParen); // start of the bindings
    List<string> names = new List<string>();
    List<ASTNode> values = new List<ASTNode>();

    do // for each binding
    {
      if(TokenIs(TokenType.Symbol)) // it's just an identifier
      {
        names.Add(ParseSymbolName());
        values.Add(null);
        NextToken();
      }
      else if(TryConsume(TokenType.LParen)) // it's a list containing type/name, name/value, or type/name/value
      {
        names.Add(ParseSymbolName());
        values.Add(ParseOne());
        Consume(TokenType.RParen);
      }
      else
      {
        AddExpectedMessage("let binding");
        if(!TokenIs(TokenType.RParen)) NextToken(); // attempt a lame recovery
      }
    } while(!TryConsume(TokenType.RParen));

    return names.Count == 0 ? ParseBody() : new LocalBindingNode(names.ToArray(), values.ToArray(), ParseBody());
  }

  ASTNode ParseOptions()
  {
    FilePosition start = token.Start;

    NextToken();
    Consume(TokenType.LParen); // start of the options
    NetLispCompilerState state = (NetLispCompilerState)CompilerState.Language.CreateCompilerState(CompilerState);

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

  ASTNode ParseNextQuoted()
  {
    if(quoted) throw new InvalidOperationException("Quoted structures cannot be nested!");
    NextToken();
    quoted = true;
    try { return ParseOne(); }
    finally { quoted = false; }
  }

  ASTNode[] ParseNodeList()
  {
    List<ASTNode> nodes = new List<ASTNode>();
    while(!TokenIs(TokenType.RParen))
    {
      nodes.Add(ParseOne());
    }
    return nodes.ToArray();
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
  }

  ITypeInfo ParseType()
  {
    string typeName = ParseSymbolName();
    switch(typeName)
    {
      case "bool": return TypeWrapper.Bool;
      case "byte": return TypeWrapper.Byte;
      case "char": return TypeWrapper.Char;
      case "complex": return TypeWrapper.Complex;
      case "double": return TypeWrapper.Double;
      case "function": return TypeWrapper.ICallable;
      case "int": return TypeWrapper.Int;
      case "integer": return TypeWrapper.Integer;
      case "list": return LispTypes.Pair;
      case "long": return TypeWrapper.Long;
      case "object": return TypeWrapper.Object;
      case "sbyte": return TypeWrapper.SByte;
      case "short": return TypeWrapper.Short;
      case "string": return TypeWrapper.String;
      case "float": return TypeWrapper.Single;
      case "uint": return TypeWrapper.UInt;
      case "ulong": return TypeWrapper.ULong;
      case "ushort": return TypeWrapper.UShort;
      default:
        Type type = Type.GetType(typeName);
        if(type == null)
        {
          AddMessage(CoreDiagnostics.MissingName, typeName);
          return TypeWrapper.Object;
        }
        return TypeWrapper.Get(type);
    }
  }

  ASTNode ParseSet()
  {
    Debug.Assert(TokenIs(TokenType.Symbol) && (string)token.Value == "set!"); // we're positioned at the 'set!' symbol
    NextToken();

    List<string>   names = new List<string>();
    List<ASTNode> values = new List<ASTNode>();
    
    do
    {
      if(!TokenIs(TokenType.Symbol))
      {
        AddExpectedMessage("symbol");
        NextToken();
      }
      else
      {
        names.Add(ParseSymbolName());
        values.Add(ParseOne());
      }
    } while(!TokenIs(TokenType.RParen));
    
    ASTNode ret;
    if(names.Count == 1)
    {
      ret = new AssignNode(new VariableNode(names[0]), values[0]);
    }
    else
    {
      ret = new BlockNode();
      for(int i=0; i<names.Count; i++)
      {
        ret.Children.Add(new AssignNode(new VariableNode(names[i]), values[i]));
      }
    }
    return ret;
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

  void AddExpectedMessage(string expected)
  {
    AddMessage(CoreDiagnostics.ExpectedSyntax, expected, token.Type);
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

  FilePosition lastEndPosition;
  Token token;
  TokenType tokenType;
  bool quoted;
  
  static readonly System.Collections.Hashtable tokenMap;
}

} // namespace NetLisp.AST