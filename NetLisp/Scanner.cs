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
using System.Globalization;
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
                      Quote="QUOTE", BackQuote="BACKQUOTE", Period="PERIOD", Comma="COMMA", Splice="SPLICE",
                      DatumComment="DATUMCOMMENT";
}

public class Scanner : ScannerBase<CompilerState>
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
        NextChar();

        char lower = char.ToLowerInvariant(Char);
        switch(lower)
        {
          case 't': case 'f': // literal true and false
            token.Value = (lower == 't');
            token.Type  = TokenString.Literal;
            NextChar();
            break;

          case '\\': // character literal
          {
            NextChar();

            StringBuilder sb = new StringBuilder();
            do
            {
              sb.Append(Char);
            } while(!IsDelimiter(NextChar()));

            char literal;

            #region Character names
            if(sb.Length == 1) // simple character literal (#\x == 'x')
            {
              literal = sb[0];
            }
            // hex form (#\x0041 == 'A') 
            else if(sb.Length > 1 && sb[0] == 'x' && IsHexDigit(sb[1]))
            {
              int value = 0;
              bool error = false;

              for(int i=1; i<sb.Length; i++)
              {
                if(!IsHexDigit(sb[i]))
                {
                  error = true;
                  break;
                }

                value = (value << 4) + GetHexValue(sb[i]);
                
                if(value > 0x10FFFF)
                {
                  error = true;
                  break;
                }
              }

              if(!error && value >= 0xD800 && value <= 0xDFFF) error = true;

              if(error)
              {
                AddMessage(NetLispDiagnostics.InvalidHexCharacter, sb);
                literal = '?';
              }
              else
              {
                string u16str = char.ConvertFromUtf32(value);
                if(u16str.Length != 1) throw new NotImplementedException("Multi-char character literal");
                literal = u16str[0];
              }
            }
            else // named characters
            {
              string name = sb.ToString();
              switch(name)
              {
                case "space": literal = ' '; break;
                case "lf": case "linefeed": case "newline": literal = '\n'; break;
                case "cr": case "return": literal = '\r'; break;
                case "tab": literal = '\t'; break;
                case "bs": case "backspace": literal = (char)8; break;
                case "esc": literal = (char)27; break;
                case "del": case "delete": literal = (char)127; break;
                case "nul": literal = (char)0; break;
                case "alarm": literal = (char)7; break;
                case "vtab": literal = (char)11; break;
                case "ff": case "page": literal = (char)12; break;
                default:
                  AddMessage(NetLispDiagnostics.UnknownCharacterName, token.Start, name);
                  literal = '?';
                  break;
              }
            }
            #endregion
            
            token.Type  = TokenString.Literal;
            token.Value = literal;
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

          case '|': // start of an nestable block comment, eg #| this is a comment |#
          {
            int depth = 1;
            NextChar();

            while(true)
            {
              if(Char == '#')
              {
                if(NextChar() == '|') depth++;
                else continue;
              }
              else if(Char == '|')
              {
                if(NextChar() == '#')
                {
                  if(--depth == 0) break;
                }
                else continue;
              }
              else if(Char == 0)
              {
                AddMessage(CoreDiagnostics.UnterminatedComment, token.Start);
                break;
              }

              NextChar();
            }

            NextChar();
            continue; // restart the token search
          }

          case 'b': case 'o': case 'd': case 'x': case 'i': case 'e': // binary, octal, hex, exact, inexact numbers
            ReadNumber(ref token);
            break;
        
          case ';': // beginning of a datum comment
            NextChar();
            token.Type = TokenString.DatumComment;
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
            char? ec = GetEscapeChar();
            if(!ec.HasValue) continue;
            c = ec.Value;
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

          case ',': // unquote or unquote-splicing
            if(NextChar() == '@')
            {
              token.Type = TokenString.Splice;
              break;
            }
            else
            {
              token.Type = TokenString.Comma;
              goto dontSkip;
            }

          case ';': // single-line comment runs until EOL, 
            while(true)
            {
              NextChar();
              if(IsEOL(Char) || Char == 0) break;
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
    \<lf>     Ignored
    \<ws><lf> Ignored
    \\        Backslash
    \"        Double quotation mark
    \n        Newline
    \t        Tab
    \r        Carriage return
    \b        Backspace
    \e        Escape
    \a        Bell
    \f        Form feed
    \v        Vertical tab
    \xHH;     Semicolon-terminated hex digits -> character value
  */
  char? GetEscapeChar()
  {
    char c = NextChar();
    switch(c)
    {
      case '\"': return '\"';
      case 'n':  return '\n';
      case 't':  return '\t';
      case 'r':  return '\r';
      case 'b':  return '\b';
      case 'e':  return (char)27;
      case 'a':  return '\a';
      case 'f':  return '\f';
      case 'v':  return '\v';
      case '\\': return '\\';

      case 'x':
      {
        StringBuilder sb = new StringBuilder();
        int value = 0;
        bool gotDigit = false, error = false;

        while(true)
        {
          c = NextChar();
          sb.Append(c);

          if(IsHexDigit(c))
          {
            value = (value << 4) + GetHexValue(c);
            if(value > 0x10FFFF) error = true;
            gotDigit = true;
          }
          else if(c == ';') break;
          else
          {
            error = true;
            break;
          }
        }

        if(!gotDigit || value >= 0xD800 && value <= 0xDFFF) error = true;

        if(error)
        {
          AddMessage(NetLispDiagnostics.InvalidHexEscape, sb.ToString());
          return '?';
        }
        else
        {
          string u16str = char.ConvertFromUtf32(value);
          if(u16str.Length != 1) throw new NotImplementedException("Multi-char character literal");
          return u16str[0];
        }
      }

      default:
        if(IsEOL(c)) return null; // if it's an EOL character, that's a line continuation

        if(char.IsWhiteSpace(c)) // if it's whitespace followed by an EOL character, that's also a line continuation
        {
          SaveState();
          char tc;
          do
          {
            tc = NextChar();
            if(IsEOL(tc)) return null;
          } while(char.IsWhiteSpace(tc));
          RestoreState();
        }

        AddMessage(CoreDiagnostics.UnknownEscapeCharacter, c);
        return '?';
    }
  }

  void ReadNumber(ref Token token)
  {
    // at this point, the current character is a digit or one of the following characters: .+-bodxieBODXIE
    // if it's a letter, then it followed a # mark and the result must be a valid number (or an error will occur).
    // otherwise, if it's not a valid number, then it's an identifier

    // read the entire thing into a stringbuilder
    StringBuilder sb = new StringBuilder();
    do sb.Append(Char); while(!IsDelimiter(NextChar()));

    // we can quickly determine if it's the period token, or the symbols - or +, so do that
    if(sb.Length == 1 && (sb[0] == '.' || sb[0] == '-' || sb[0] == '+'))
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

    string numString;
    int radix = 0;
    char exact = '?';
    bool required;

    token.Type  = TokenString.Literal;

    if(!char.IsLetter(sb[0])) // if it doesn't begin with a letter, then it's either a number or a symbol like 1+
    {
      numString = sb.ToString();
      required  = false;
    }
    else // otherwise, it begins with numeric flags, so it must be a number.
    {
      token.Value = 0;
      required = true;

      int i;
      for(i=0; i<sb.Length; i++) // read the flags
      {
        switch(sb[i])
        {
          case 'b': case 'd': case 'o': case 'x':
            if(radix != 0)
            {
              AddMessage(NetLispDiagnostics.MultipleRadixFlags, "#"+sb.ToString());
              return;
            }
            radix = sb[i] == 'x' ? 16 : sb[i] == 'o' ? 8 : sb[i] == 'b' ? 2 : 10;
            break;

          case 'e': case 'i':
            if(exact != '?')
            {
              AddMessage(NetLispDiagnostics.MultipleExactnessFlags, "#"+sb.ToString());
              return;
            }
            exact = sb[i];
            break;

          default: goto doneWithFlags;
        }
      }

      doneWithFlags:
      numString = sb.ToString(i, sb.Length-i);
    }

    if(radix == 0) radix = 10;

    // numString has been set to what should be a number
    Match m = (radix == 10 ? decNumRe : radix == 16 ? hexNumRe : radix == 8 ? octNumRe : binNumRe).Match(numString);
    if(!m.Success) // if it not a valid number, then assume it's a symbol
    {
      if(required) // but if a number was required in this position, report an error
      {
        AddMessage(CoreDiagnostics.ExpectedNumber, "#" + sb.ToString());
        return;
      }

      token.Type  = TokenString.Symbol;
      token.Value = numString;
      return;
    }

    if(m.Groups["special"].Success)
    {
      string special = m.Groups["special"].Value;
      token.Value = special[1] == 'n' ? double.NaN :
                                      special[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
    }
    else if(ParseNumber(m.Groups["num"].Value, m.Groups["exp"].Value, m.Groups["den"].Value, radix, exact,
                        out token.Value))
    {
      if(m.Groups["imag"].Success) // if it has an imaginary part (meaning that it's a complex number)
      {
        object real = token.Value, imaginary;
        if(!ParseNumber(m.Groups["imag"].Value, m.Groups["imagexp"].Value, m.Groups["imagden"].Value, radix, exact,
                        out imaginary))
        {
          token.Value = imaginary;
          return;
        }

        bool valueIsExact = !(real is double) && !(imaginary is double);
        if(exact == 'e' || valueIsExact)
        {
          token.Value = new ComplexRational(MakeRational(real), MakeRational(imaginary));
        }
        else
        {
          token.Value = new Complex(real is double ? (double)real : Convert.ToDouble(real),
                                    imaginary is double ? (double)imaginary : Convert.ToDouble(imaginary));
        }
      }
      else if(m.Groups["angle"].Success) // if it has an angular part (meaning that it's a polar number)
      {
        throw new NotImplementedException("Polar numbers");
      }
    }
  }

  bool ParseNumber(string str, string exp, string den, int radix, char exact, out object value)
  {
    if(!string.IsNullOrEmpty(den)) // if the number is a rational
    {
      Integer numerator = Integer.Parse(str, radix), denominator = Integer.Parse(den, radix);

      if(denominator == Integer.Zero)
      {
        AddMessage(NetLispDiagnostics.DivisionByZero, str+"/"+den);
        value = double.NaN;
        return false;
      }

      value = exact == 'i' ? (object)(Integer.ToDouble(numerator) / Integer.ToDouble(denominator))
                        : new Rational(numerator, denominator);
    }
    else if(!string.IsNullOrEmpty(str)) // the number is an explicit real
    {
      value = ParseReal(str, exp, radix, exact);
    }
    else // the number is not specified
    {
      value = 0;
    }

    return true;
  }

  static bool IsDelimiter(char c)
  {
    if(char.IsWhiteSpace(c)) return true;

    switch(c)
    {
      case '(': case ')': case '[': case ']': case '{': case '}': case '"': case '`': case '\'': case ',': case '\0':
        return true;
      default:
        return false;
    }
  }

  static bool IsEOL(char c)
  {
    return c == '\n' || c == '\x85' || c == '\x2028';
  }

  static bool IsHexDigit(char c)
  {
    if(c >= '0' && c <= '9') return true;
    c = char.ToLowerInvariant(c);
    return c >= 'a' && c <= 'f';
  }

  static int GetHexValue(char c)
  {
    return c >= '0' && c <= '9' ? c-'0' : char.ToLowerInvariant(c)-'a'+10;
  }

  static Rational MakeRational(object o)
  {
    if(o is int) return new Rational((int)o);
    else if(o is long) return new Rational((long)o);
    else return (Rational)o;
  }

  static double ParseDouble(string str, int radix)
  {
    return radix == 10 ? double.Parse(str, CultureInfo.InvariantCulture) : ParseNumberAsRational(str, radix).ToDouble();
  }

  static object ParseReal(string str, string exp, int radix, char exact)
  {
    if(str.IndexOf('.') == -1 && string.IsNullOrEmpty(exp) && exact != 'i') // if the number is an exact integer
    {
      return ShrinkInteger(Integer.Parse(str, radix));
    }
    else if(exact == 'e') // otherwise, the number is exact, probably a rational
    {
      Rational number = ParseNumberAsRational(str, radix);
      if(!string.IsNullOrEmpty(exp))
      {
        Integer factor = Integer.Pow(10, Integer.Abs(Integer.Parse(exp, radix)));
        if(exp[0] == '-') number /= factor;
        else number *= factor;

        // if after the exponentation, the number became an integer, return the integer
        if(number.Denominator == Integer.One) return ShrinkInteger(number.Numerator);
      }
      return number;
    }
    else // the number is inexact (a double)
    {
      double number = ParseDouble(str, radix);
      if(!string.IsNullOrEmpty(exp)) number *= Math.Pow(10, ParseDouble(exp, radix));
      return number;
    }
  }

  static Rational ParseNumberAsRational(string str, int radix)
  {
    int period = str.IndexOf('.');
    if(period == -1) return new Rational(Integer.Parse(str, radix));
    string whole = str.Substring(0, period), frac = str.Substring(period+1);
    return new Rational(Integer.Parse(whole+frac, radix), Integer.Pow(10, new Integer(frac.Length)));
  }

  static object ShrinkInteger(Integer i)
  {
    if(i >= int.MinValue && i <= int.MaxValue) return Integer.ToInt32(i);
    else if(i >= long.MinValue && i <= long.MaxValue) return Integer.ToInt64(i);
    else return i;
  }

  static Regex MakeNumberRegex(string digitType)
  {
    return MakeNumberRegex(digitType, "ef");
  }

  static Regex MakeNumberRegex(string digitType, string extraExpChars)
  {
    string unsignedInt  = digitType + "+", signedInt = "[+-]?" + unsignedInt;
    string unsignedReal = "(" + unsignedInt + @"(\." + digitType + @"*)?|\." + unsignedInt + ")";
    string signedReal   = "[+-]?" + unsignedReal;
    string e = "[dls" + extraExpChars + "]";

    return
      new Regex(@"^(((?<num>" + signedReal + ") ("+e+"(?<exp>" + signedInt + @"))? |
                     (?<num>" + signedInt + ") / (?<den>" + unsignedInt + @"))
                    ((@ ((?<angle>" + signedReal + ") ("+e+"(?<angleexp>" + signedInt + @"))? |
                         (?<angle>" + signedInt + ") / (?<angleden>" + unsignedInt + @"))) |
                     (((?<imag>[+-]" + unsignedReal + ") ("+e+"(?<imagexp>" + signedInt + @"))? |
                       (?<imag>[+-]" + unsignedInt + ") / (?<imagden>" + unsignedInt + @")) i)
                    )? |
                    ((?<imag>[+-]" + unsignedReal + ") ("+e+"(?<imagexp>" + signedInt + @"))? |
                     (?<imag>[+-]" + unsignedInt + ") / (?<imagden>" + unsignedInt + @")) i |
                    (?<special>[+-](inf|nan))\.0
                  )$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace |
                       RegexOptions.Singleline | RegexOptions.ExplicitCapture);
  }

  static readonly Regex binNumRe = MakeNumberRegex("[01]");
  static readonly Regex octNumRe = MakeNumberRegex("[0-7]");
  static readonly Regex decNumRe = MakeNumberRegex("[0-9]");
  static readonly Regex hexNumRe = MakeNumberRegex("[0-9a-f]", null);
}

} // namespace NetLisp.Backend