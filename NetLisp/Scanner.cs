using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Scripting;
using Scripting.AST;
using Scripting.Runtime;

namespace NetLisp.AST
{

static class TokenString
{
  public const string Literal="LITERAL", Symbol="SYMBOL", Vector="VECTOR", LParen="LPAREN", RParen="RPAREN",
                      LBracket="LBRACKET", RBracket="RBRACKET", LCurly="LCURLY", RCurly="RCURLY",
                      Quote="QUOTE", BackQuote="BACKQUOTE", Period="PERIOD";
}

public class Scanner : ScannerBase<NetLispCompilerState>
{
  public Scanner(params string[] sourceNames) : base(sourceNames) { }
  public Scanner(params TextReader[] sources) : base(sources) { }
  public Scanner(TextReader[] sources, string[] sourceNames) : base(sources, sourceNames) { }

  protected override bool ReadToken(out Token token)
  {
    token = new Token();
    if(!EnsureValidSource()) return false; // move to the next data source if necessary

    while(true) // while we haven't found a valid token yet
    {
      SkipWhitespace();
      token.SourceName = SourceName;
      token.Start      = Position;
      
      if(char.IsDigit(Char) || Char == '.' || Char == '-' || Char == '+')
      {
        ReadNumber(ref token);
        break;
      }
      else if(Char == '#')
      {
        switch(NextChar())
        {
          case 't': case 'f': // literal true and false
            token.Value = (Char == 't');
            token.Type  = TokenString.Literal;
            NextChar();
            break;

          case '\\': // character literal
          {
            NextChar();
            if(char.IsLetter(Char))
            {
              StringBuilder sb = new StringBuilder();
              do
              {
                sb.Append(Char);
              } while(!IsDelimiter(NextChar()));

              string name = sb.ToString();
              char literal;

              #region Character names
              if(name.Length == 1) // simple character literal (#\x == 'x')
              {
                literal = name[0];
              }
              // control codes (#\c-m == ^M)
              else if(name.StartsWith("c-", StringComparison.InvariantCultureIgnoreCase) && name.Length == 3)
              {
                int ordinal = char.ToUpper(name[2]) - 64;
                if(ordinal < 1 || ordinal > 26)
                {
                  AddMessage(NetLispDiagnostics.InvalidControlCode, token.Start, name);
                  literal = '?'; // recover by giving an arbitrary value
                }
                else
                {
                  literal = (char)ordinal;
                }
              }
              else // named characters
              {
                switch(name.ToLowerInvariant())
                {
                  case "space": literal = ' '; break;
                  case "lf": case "linefeed": case "newline": literal = '\n'; break;
                  case "cr": case "return": literal = '\r'; break;
                  case "tab": case "ht": literal = '\t'; break;
                  case "bs": case "backspace": literal = (char)8; break;
                  case "esc": case "altmode": literal = (char)27; break;
                  case "del": case "rubout": literal = (char)127; break;
                  case "nul": literal = (char)0; break;
                  case "soh": literal = (char)1; break;
                  case "stx": literal = (char)2; break;
                  case "etx": literal = (char)3; break;
                  case "eot": literal = (char)4; break;
                  case "enq": literal = (char)5; break;
                  case "ack": literal = (char)6; break;
                  case "bel": literal = (char)7; break;
                  case "vt":  literal = (char)11; break;
                  case "ff": case "page": literal = (char)12; break;
                  case "so":  literal = (char)14; break;
                  case "si":  literal = (char)15; break;
                  case "dle": literal = (char)16; break;
                  case "dc1": literal = (char)17; break;
                  case "dc2": literal = (char)18; break;
                  case "dc3": literal = (char)19; break;
                  case "dc4": literal = (char)20; break;
                  case "nak": literal = (char)21; break;
                  case "syn": literal = (char)22; break;
                  case "etb": literal = (char)23; break;
                  case "can": literal = (char)24; break;
                  case "em":  literal = (char)25; break;
                  case "sub": case "call": literal = (char)26; break;
                  case "fs":  literal = (char)28; break;
                  case "gs":  literal = (char)29; break;
                  case "rs":  literal = (char)30; break;
                  case "us": case "backnext": literal = (char)31; break;
                  default:
                    AddMessage(NetLispDiagnostics.UnknownCharacterName, token.Start, name);
                    literal = '?';
                    break;
                }
              }
              #endregion
              
              token.Type  = TokenString.Literal;
              token.Value = literal;
            }
            break;
          }

          case '%': // internal symbols
          {
            StringBuilder sb = new StringBuilder();
            sb.Append('#').Append(Char);
            while(!IsDelimiter(NextChar()))
            {
              sb.Append(Char);
            }

            token.Type  = TokenString.Symbol;
            token.Value = sb.ToString();
            break;
          }
          
          case '"': case '\'': // strings literals with few escape codes
          {
            char delim = Char;
            StringBuilder sb = new StringBuilder();
            while(true)
            {
              NextChar();
              if(Char == delim) // possibly end the string if we find the delimiter
              {
                if(NextChar() == delim) // the only exception is a double delimiter, eg: #"hello "" goodbye"
                {
                  sb.Append(delim);
                }
                else
                {
                  break;
                }
              }
              else if(Char == 0)
              {
                AddMessage(CoreDiagnostics.UnterminatedStringLiteral, token.Start);
                break;
              }
              else
              {
                sb.Append(Char);
              }
            }
            
            token.Type  = TokenString.Literal;
            token.Value = sb.ToString();
            break;
          }
          
          case '(': // start of a vector, eg #(a b c)
            token.Type = TokenString.Vector;
            NextChar();
            break;

          case '*': // start of an extended comment, eg #* this is a comment *#
            while(true)
            {
              if(NextChar() == '*' && NextChar() == '#') break;
              if(Char == 0)
              {
                AddMessage(CoreDiagnostics.UnterminatedComment, token.Start);
                break;
              }
            }
            NextChar();
            continue; // restart the token search

          case 'b': case 'o': case 'd': case 'x': case 'i': case 'e': // binary, octal, hex, exact, inexact numbers
            ReadNumber(ref token);
            break;
        
          case '<':
            AddMessage(NetLispDiagnostics.EncounteredUnreadable, token.Start);
            while(true) // recover by skipping to the next (assuming the string looks like #<...>)
            {
              NextChar();
              if(Char == '>' || Char == 0) break;
            }
            token.Type  = TokenString.Literal;
            token.Value = null;
            break;
          
          default:
            AddMessage(NetLispDiagnostics.UnknownNotation, token.Start, Char);
            while(!IsDelimiter(NextChar())) { }
            token.Type  = TokenString.Literal;
            token.Value = null;
            break;
        }

        break;
      }
      else if(Char == '"') // regular string literals
      {
        StringBuilder sb = new StringBuilder();
        while(true)
        {
          char c = NextChar();
          if(c == '"')
          {
            NextChar();
            break;
          }
          else if(c == '\\')
          {
            c = GetEscapeChar();
          }
          else if(c == 0)
          {
            AddMessage(CoreDiagnostics.UnterminatedStringLiteral, token.Start);
            break;
          }
          
          sb.Append(c);
        }
        
        token.Type  = TokenString.Literal;
        token.Value = sb.ToString();
        break;
      }
      else
      {
        switch(Char)
        {
          case '(':  token.Type = TokenString.LParen; break;
          case ')':  token.Type = TokenString.RParen; break;
          case '[':  token.Type = TokenString.LBracket; break;
          case ']':  token.Type = TokenString.RBracket; break;
          case '{':  token.Type = TokenString.LCurly; break;
          case '}':  token.Type = TokenString.RCurly; break;
          case '\'': token.Type = TokenString.Quote; break;
          case '`':  token.Type = TokenString.BackQuote; break;

          case ';': // single-line comment
            while(true)
            {
              NextChar();
              if(Char == '\n' || Char == 0) break;
            }
            continue; // restart token search

          case '\0':
            if(NextSource()) continue; // restart token search
            else return false;

          default:
          {
            StringBuilder sb = new StringBuilder();
            do
            {
              sb.Append(Char);
            } while(!IsDelimiter(NextChar()));

            string value = sb.ToString();
            if(value == "nil")
            {
              token.Type  = TokenString.Literal;
              token.Value = null;
            }
            else
            {
              token.Type  = TokenString.Symbol;
              token.Value = value;
            }
            
            goto dontSkip;
          }
        }

        NextChar(); // skip over the character we just read
        dontSkip:

        break;
      }
    }

    token.End = Position;
    return true; 
  }
  
  /*
    \newline  Ignored
    \\        Backslash
    \"        Double quotation mark
    \'        Single quotation mark
    \n        Newline
    \t        Tab
    \r        Carriage return
    \b        Backspace
    \e        Escape
    \a        Bell
    \f        Form feed
    \v        Vertical tab
    \xHH      Up to 2 hex digits -> byte value
    \uHHHH    Up to 4 hex digits -> 16-bit unicode value
    \cC       Control code (eg, \cC is ctrl-c)
  */
  char GetEscapeChar()
  {
    char c = NextChar();
    switch(c)
    {
      case '\"': return '\"';
      case '\'': return '\'';
      case 'n':  return '\n';
      case 't':  return '\t';
      case 'r':  return '\r';
      case 'b':  return '\b';
      case 'e':  return (char)27;
      case 'a':  return '\a';
      case 'f':  return '\f';
      case 'v':  return '\v';
      case '\\': return '\\';

      case 'x': case 'u':
      {
        int num = 0;
        for(int i=0,limit=(c=='x' ? 2 : 4); i<limit; i++)
        {
          SaveState();
          c = NextChar();
          if(char.IsDigit(c))
          {
            num = (num<<4) | (c-'0');
          }
          else if((c<'A' || c>'F') && (c<'a' || c>'f'))
          {
            if(i == 0) AddMessage(CoreDiagnostics.ExpectedHexDigit, Diagnostic.CharLiteral(c));
            RestoreState();
            break;
          }
          else
          {
            num = (num<<4) | (char.ToUpperInvariant(c)-'A'+10);
          }
        }
        return (char)num;
      }

      case 'c':
        c = char.ToUpperInvariant(NextChar());
        if(c<'A' || c>'Z')
        {
          AddMessage(CoreDiagnostics.ExpectedLetter, Diagnostic.CharLiteral(c));
          return '?';
        }
        else
        {
          return (char)(c-64);
        }

      default:
        AddMessage(CoreDiagnostics.UnknownEscapeCharacter, c);
        return '?';
    }
  }

  void ReadNumber(ref Token token)
  {
    int  radix = 10;
    char exact = '?';

    StringBuilder sb = new StringBuilder();
    while(!IsDelimiter(Char))
    {
      sb.Append(Char);
      NextChar();
    }

    if(sb[0] == '.' || sb[0] == '-' || sb[0] == '+')
    {
      if(sb.Length == 1)
      {
        if(sb[0] == '.')
        {
          token.Type = TokenString.Period;
          return;
        }
        else
        {
          token.Value = sb.ToString();
          token.Type  = TokenString.Symbol;
          return;
        }
      }
    }

    string numString;

    if(!char.IsLetter(sb[0]))
    {
      numString = sb.ToString();
    }
    else
    {
      int i;
      for(i=0; i<sb.Length; i++)
      {
        switch(sb[i])
        {
          case 'b': radix = 2; break;
          case 'o': radix = 8; break;
          case 'd': radix = 10; break;
          case 'x': radix = 16; break;
          case 'e': case 'i': exact = sb[i]; break;
          default: goto doneWithFlags;
        }
      }

      doneWithFlags:
      numString = sb.ToString(i, sb.Length-i);
    }

    token.Type = TokenString.Literal;

    Match m = (radix == 10 ? decNum : radix == 16 ? hexNum : radix == 8 ? octNum : binNum).Match(numString);
    if(!m.Success) // if it not a valid number, then assume it's a symbol
    {
      token.Value = numString;
      token.Type  = TokenString.Symbol;
      return;
    }

    if(m.Groups["den"].Success) // if the number has a denominator (meaning that it's a fraction)
    {
      if(exact == 'i')
      {
        token.Value = Convert.ToDouble(ParseInteger(m.Groups["num"].Value, radix)) /
                      Convert.ToDouble(ParseInteger(m.Groups["den"].Value, radix));
      }
      else
      {
        token.Value = new Rational(Integer.Parse(m.Groups["num"].Value, radix),
                                   Integer.Parse(m.Groups["den"].Value, radix));
      }
      return;
    }
    
    object numerator = ParseNumber(m.Groups["num"].Value, m.Groups["exp"].Value, radix, exact);
    
    if(m.Groups["imag"].Success) // if it has an imaginary part (meaning that it's a complex number)
    {
      if(exact == 'e')
      {
        throw new NotImplementedException("exact complexes");
      }
      else
      {
        token.Value = new Complex(Convert.ToDouble(numerator),
                                  Convert.ToDouble(ParseNumber(m.Groups["imag"].Value, m.Groups["imagexp"].Value,
                                                               radix, exact)));
      }
    }
    else if(char.ToLowerInvariant(numString[numString.Length-1]) == 'i') // the regex doesn't think it has an imaginary
    {                                                                    // part, but it actually does...
      token.Value = new Complex(0, Convert.ToDouble(numerator));
    }
    else
    {
      token.Value = numerator;
    }
  }

  static bool IsDelimiter(char c)
  {
    if(char.IsWhiteSpace(c)) return true;

    switch(c)
    {
      case '(': case ')': case '[': case ']': case '{': case '}': case '#': case '`': case ',': case '\'': case'\0':
        return true;
      default:
        return false;
    }
  }
  
  static object ParseInteger(string str, int radix)
  {
    if(string.IsNullOrEmpty(str)) return 0;

    bool negative = str[0] == '-';
    if(negative) str = str.Substring(1); // the .NET number parser doesn't accept negative numbers with non-10 bases

    try
    {
      int i = Convert.ToInt32(str, radix);
      return negative ? -i : i;
    }
    catch(OverflowException)
    {
      try
      {
        long L = Convert.ToInt64(str, radix);
        return negative ? -L : L;
      }
      catch(OverflowException)
      {
        Integer I = Integer.Parse(str, radix);
        return negative ? -I : I;
      }
    }
  }
  
  static Rational ParseNumber(string str, int radix)
  {
    int period = str.IndexOf('.');
    if(period == -1) return new Rational(Integer.Parse(str, radix));
    string whole = str.Substring(0, period), frac = str.Substring(period+1);
    return new Rational(Integer.Parse(whole+frac, radix), Integer.Pow(new Integer(10), new Integer(frac.Length)));
  }
  
  static object ParseNumber(string str, string exp, int radix, char exact)
  {
    if(str.IndexOf('.') == -1 && exp.IndexOf('.') == -1 && exact != 'i') // if the number has no fractional part and
    {                                                                    // this isn't an inexact number, read it as an
      Integer i = Integer.Parse(str, radix);                             // integer
      if(!string.IsNullOrEmpty(exp)) i *= Integer.Pow(new Integer(10), Integer.Parse(exp, radix));
      return ShrinkInteger(i);
    }
    else // otherwise, read it as a floating point
    {
      Rational number = ParseNumber(str, radix); // TODO: use rational instead of double for the highest accuracy
      if(!string.IsNullOrEmpty(exp))
      {
        Rational exponent = ParseNumber(exp, radix);
        if(exponent.Denominator != 1) throw new NotImplementedException("rational powers");
        number *= Integer.Pow(new Integer(10), exponent.Numerator);
      }

      if(exact != 'e') // if they want an inexact number, convert it to floating point
      {
        return number.ToDouble();
      }
      else if(number.Numerator == 1) // otherwise, if the number seems to be an integer, convert it to one
      {
        return ShrinkInteger(number.Numerator);
      }
      else // otherwise, just return the rational
      {
        return number;
      }
    }
  }

  static object ShrinkInteger(Integer i)
  {
    if(i >= int.MinValue && i <= int.MaxValue) return Integer.ToInt32(i);
    else if(i >= long.MinValue && i <= long.MaxValue) return Integer.ToInt64(i);
    else return i;
  }

  static readonly Regex binNum =
    new Regex(@"^(
                   (?<num>[+-]?([01]+(\.[01]*)?|\.[01]+))
                   (e(?<exp>[+-]?([01]+(\.[01]*)?|\.[01]+)))?
                   (((?<imag>[+-]([01]+(\.[01]*)?|\.[01]+))
                     (e(?<imagexp>[+-]?([01]+(\.[01]*)?|\.[01]+)))?
                   )?i)?
                 |
                 (?<num>[+-]?[01]+)/(?<den>[+-]?[01]+)
                )$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace |
                     RegexOptions.Singleline | RegexOptions.ExplicitCapture);

  static readonly Regex octNum =
    new Regex(@"^(
                   (?<num>[+-]?([0-7]+(\.[0-7]*)?|\.[0-7]+))
                   (e(?<exp>[+-]?([0-7]+(\.[0-7]*)?|\.[0-7]+)))?
                   (((?<imag>[+-]([0-7]+(\.[0-7]*)?|\.[0-7]+))
                     (e(?<imagexp>[+-]?([0-7]+(\.[0-7]*)?|\.[0-7]+)))?
                   )?i)?
                 |
                 (?<num>[+-]?[0-7]+)/(?<den>[+-]?[0-7]+)
                )$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace |
                     RegexOptions.Singleline | RegexOptions.ExplicitCapture);

  static readonly Regex decNum =
    new Regex(@"^(
                   (?<num>[+-]?(\d+(\.\d*)?|\.\d+))
                   (e(?<exp>[+-]?(\d+(\.\d*)?|\.\d+)))?
                   (((?<imag>[+-](\d+(\.\d*)?|\.\d+))
                     (e(?<imagexp>[+-]?(\d+(\.\d*)?|\.\d+)))?
                   )?i)?
                 |
                 (?<num>[+-]?\d+)/(?<den>[+-]?\d+)
                )$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace |
                     RegexOptions.Singleline | RegexOptions.ExplicitCapture);

  static readonly Regex hexNum =
    new Regex(@"^(
                   (?<num>[+-]?([\da-f]+(\.[\da-f]*)?|\.[\da-f]+))
                   (e(?<exp>[+-]?([\da-f]+(\.[\da-f]*)?|\.[\da-f]+)))?
                   (((?<imag>[+-]([\da-f]+(\.[\da-f]*)?|\.[\da-f]+))
                     (e(?<imagexp>[+-]?([\da-f]+(\.[\da-f]*)?|\.[\da-f]+)))?
                   )?i)?
                 |
                 (?<num>[+-]?[\da-f]+)/(?<den>[+-]?[\da-f]+)
                )$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace |
                     RegexOptions.Singleline | RegexOptions.ExplicitCapture);
}

} // namespace NetLisp.Backend