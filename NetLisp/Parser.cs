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

public class Parser : ParserBase
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
                ret = ParseBody();
                break;
              
              case "lambda": // (lambda params form ...) where params is a symbol or a possibly-dotted list of symbols
              { NextToken();
                bool hasList;
                ParameterNode[] parameters = ParameterNode.GetParameters(ParseLambdaList(out hasList));
                if(hasList)
                {
                  ParameterNode last = parameters[parameters.Length-1];
                  parameters[parameters.Length-1] = new ParameterNode(last.Name, typeof(Pair), ParameterType.List);
                }
                return new ScriptFunctionNode(null, parameters, ParseBody());
              }

              case "set!": // (set! symbol form [symbol form ...])
                ret = ParseSet();
                break;
              
              case "define": // (define symbol value)
              {
                NextToken();
                string name = ParseSymbolName();
                ret = new AssignNode(new VariableNode(name, new TopLevelSlot(name)), ParseOne());
                break;
              }

              case "vector":
                NextToken();
                ret = new VectorNode(ParseNodeList());
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
          ret = new LiteralNode(Symbol.Get(ParseSymbolName()));
        }
        else
        {
          ret = new VariableNode(ParseSymbolName());
        }
        NextToken();
        break;

      case TokenType.Literal:
        ret = new LiteralNode(token.Value);
        NextToken();
        break;

      case TokenType.Quote: // a quote, eg 'a
        if(quoted) // transform (quote 'a) into (quote (quote a))
        {
          ret = new ListNode(new LiteralNode(Symbol.Get("quote")), ParseOne());
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
          list.ListItems.Add(new LiteralNode(Symbol.Get("vector")));
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
        AddErrorMessage(string.Format("Unexpected token '{0}'", token.Type));
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

  /// <summary>Adds a new error message using the current source name and position.</summary>
  void AddErrorMessage(string message)
  {
    AddErrorMessage(token.SourceName, token.Start, message);
  }

  /// <summary>Adds a new error message using the current source name and the given position.</summary>
  void AddErrorMessage(FilePosition position, string message)
  {
    AddErrorMessage(token.SourceName, position, message);
  }

  /// <summary>Adds a new error message using the current source name and the given position.</summary>
  void AddErrorMessage(FilePosition position, string format, params object[] args)
  {
    AddErrorMessage(position, string.Format(format, args));
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
        throw SyntaxError("expected '{0}' but found '{1}'", type, token.Type);
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
      else if(TryConsume(TokenType.LParen)) // it's a binding/value pair
      {
        names.Add(ParseSymbolName());
        values.Add(ParseOne());
        Consume(TokenType.RParen);
      }
      else
      {
        AddErrorMessage(string.Format("expected 'let' binding, but received '{0}'", token.Type));
        if(!TokenIs(TokenType.RParen)) NextToken(); // attempt a lame recovery
      }
    } while(!TryConsume(TokenType.RParen));

    ASTNode ret;
    if(names.Count == 0)
    {
      AddErrorMessage(start, "'let' has no bindings");
      ret = ParseOne();
    }
    else
    {
      ret = new LocalBindingNode(names.ToArray(), values.ToArray(), ParseOne());
    }
    
    return ret;
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

  string[] ParseLambdaList(out bool hasList)
  {
    hasList = false;
    
    if(TokenIs(TokenType.Symbol)) // a single symbol that receives a list of arguments
    {
      hasList = true;
      return new string[] { ParseSymbolName() };
    }
    else if(TryConsume(TokenType.LParen)) // a list of symbols
    {
      List<string> names = new List<string>();
      while(!TokenIs(TokenType.RParen))
      {
        names.Add(ParseSymbolName());
        if(TryConsume(TokenType.Period)) // a dotted list
        {
          if(hasList) Unexpected(TokenType.Period);
          hasList = true;
          names.Add(ParseSymbolName());
        }
      }
      return names.ToArray();
    }
    else
    {
      throw SyntaxError(token.Start, "expected lambda parameter list");
    }
  }

  string ParseSymbolName()
  {
    Expect(TokenType.Symbol);
    string name = (string)token.Value;
    NextToken();
    return name;
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
        AddErrorMessage("expected symbol name in set!");
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

  SyntaxErrorException SyntaxError(string format, params object[] args)
  {
    return SyntaxError(token.Start, format, args);
  }

  SyntaxErrorException SyntaxError(FilePosition position, string format, params object[] args)
  {
    OutputMessage message = new OutputMessage(OutputMessageType.Error, string.Format(format, args),
                                              token.SourceName, position);
    AddMessage(message);
    return new SyntaxErrorException(message);
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
      throw SyntaxError("unexpected end of file");
    }
    else
    {
      throw SyntaxError("unexpected token '{0}'", token.Type);
    }
  }

  FilePosition lastEndPosition;
  Token token;
  TokenType tokenType;
  bool quoted;
  
  static readonly System.Collections.Hashtable tokenMap;
}

} // namespace NetLisp.AST