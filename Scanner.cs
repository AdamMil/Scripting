using System;
using System.Collections.Generic;
using System.IO;
using Scripting;

namespace Scripting.AST
{

#region FilePosition
/// <summary>Represents a position within a source file.</summary>
public struct FilePosition
{
  public FilePosition(int line, int column)
  {
    Line   = line;
    Column = column;
  }

  public override string ToString()
  {
    return Line.ToString() + ":" + Column.ToString();
  }

  /// <summary>The one-based line or column index.</summary>
  public int Line, Column;
}
#endregion

#region Token
/// <summary>Represents a single language token.</summary>
public struct Token
{
  /// <summary>The type of the token. This value can be arbitrary, and is only expected to be understood by the parser.
  /// </summary>
  public string Type;
  /// <summary>The name of the source file from which the token was read. This does not need to be a file on disk. It
  /// can be an in-memory file, a URL, etc. Diagnostic tools such as debuggers will attempt to read the data from this
  /// source file.
  /// </summary>
  public string SourceName;
  /// <summary>The start position of the token's span within the file.</summary>
  public FilePosition Start;
  /// <summary>The end position of the token's span within the file. The end position is exclusive, pointing to the
  /// character immediately after the token.
  /// </summary>
  public FilePosition End;
  /// <summary>An arbitrary value associated with this token. For instance, numeric tokens might pass the numeric value
  /// in this field.
  /// </summary>
  public object Value;

  public override string ToString()
  {
    return Type;
  }
}
#endregion

#region IScanner
/// <summary>An interface that represents a scanner (also called a lexer or tokenizer).</summary>
public interface IScanner
{
  /// <summary>Retrieves the next token in the stream.</summary>
  /// <returns>True if a token was retrieved and false otherwise.</returns>
  bool NextToken(out Token token);
  /// <summary>Pushes a token back onto the token stream. Scanners should support an unlimited number of pushed-back
  /// tokens.
  /// </summary>
  void PushBack(Token token);
}
#endregion

#region StandardScanner
/// <summary>Provides a helper class for implementing scanners.</summary>
/// <remarks>You are not required to use this class when you implement scanners. This class exists only to provide a
/// part of the <see cref="IScanner"/> implementation.
/// </remarks>
public abstract class ScannerBase : IScanner
{
  /// <summary>
  /// Initializes the scanner with a list of source names. The source files will be loaded based on these names.
  /// </summary>
  protected ScannerBase(CompilerState state, params string[] sourceNames)
  {
    if(sources == null) throw new ArgumentNullException();
    this.state       = state;
    this.sourceNames = sourceNames;
    ValidateSources();
  }

  /// <summary>Initializes the scanner with a list of streams. The source names of the streams will be
  /// "&lt;unknown&gt;".
  /// </summary>
  /// <param name="sources"></param>
  protected ScannerBase(CompilerState state, params TextReader[] sources)
  {
    if(sources == null) throw new ArgumentNullException();
    this.state   = state;
    this.sources = sources;
    ValidateSources();
  }

  /// <summary>Initializes the scanner with a list of streams and their names.</summary>
  protected ScannerBase(CompilerState state, TextReader[] sources, string[] sourceNames)
  {
    if(sources == null || sourceNames == null) throw new ArgumentNullException();
    if(sources.Length != sourceNames.Length)
    {
      throw new ArgumentException("Number of source names doesn't match number of sources.");
    }
    this.state       = state;
    this.sources     = sources;
    this.sourceNames = sourceNames;
    ValidateSources();
  }

  /// <summary>Retrieves the next token.</summary>
  /// <returns>True if the next token was retrieved and false otherwise.</returns>
  public bool NextToken(out Token token)
  {
    if(pushedTokens != null && pushedTokens.Count != 0)
    {
      token = pushedTokens.Dequeue();
      return true;
    }
    else
    {
      return ReadToken(out token);
    }
  }

  /// <summary>Pushes a token back onto the token stream.</summary>
  public void PushBack(Token token)
  {
    if(pushedTokens == null) pushedTokens = new Queue<Token>();
    pushedTokens.Enqueue(token);
  }

  /// <summary>Gets the current character. This is not valid until <see cref="NextSource"/> has been called at least
  /// once.
  /// </summary>
  protected char Char
  {
    get { return currentChar; }
  }

  /// <summary>Gets the pushback character. This is only valid if it has been previously set.</summary>
  protected char PushedChar
  {
    get { return pushedChar; }
    set
    {
      if(pushedChar != '\0')
      {
        throw new InvalidOperationException("A character has already been pushed back.");
      }

      // adjust the current position based on the value of the pushed character
      if(value == '\n' || value == '\r')
      {
        column = previousLineLength;
        line--;
      }
      else
      {
        column--;
      }

      pushedChar = value;
    }
  }

  /// <summary>Gets the one-based column index within the current source line.</summary>
  protected int Column
  {
    get { return column; }
  }

  /// <summary>Gets the one-based line index within the current source file.</summary>
  protected int Line
  {
    get { return line; }
  }
  
  /// <summary>Gets the current position within the source file.</summary>
  protected FilePosition Position
  {
    get { return new FilePosition(line, column); }
  }

  /// <summary>Gets the compiler state passed to the constructor.</summary>
  protected CompilerState CompilerState
  {
    get { return state; }
  }

  /// <summary>Gets whether a source is loaded and whether <see cref="Source"/>, <see cref="SourceName"/>, etc are
  /// valid.
  /// </summary>
  protected bool HasValidSource
  {
    get { return textData != null; }
  }

  /// <summary>Gets the text of the current source stream. You must call <see cref="NextSource"/> at least once before
  /// this will be valid.
  /// </summary>
  protected string Source
  {
    get
    {
      AssertValidSource();
      return textData;
    }
  }

  /// <summary>Gets the name of the current source. You must call <see cref="NextSource"/> at least once before this
  /// will be valid.
  /// </summary>
  protected string SourceName
  {
    get
    {
      AssertValidSource();
      return sourceNames == null ? "<unknown>" : sourceNames[sourceIndex];
    }
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

  /// <summary>Loads a data stream, given its source name.</summary>
  protected virtual TextReader LoadSource(string name)
  {
    return new StreamReader(name);
  }

  /// <summary>Reads the next character from the input stream.</summary>
  /// <returns>Returns the next character, or the nul (0) character if there is no more input in the current source.</returns>
  protected char NextChar()
  {
    AssertValidSource();
    
    bool wasPushback = false; // true if the character comes from the pushback character

    if(pushedChar != '\0') // if we have a pushback character, use it
    {
      currentChar = pushedChar;
      pushedChar  = '\0';
      wasPushback = true;
    }
    else if(dataIndex >= textData.Length) // or, if we've reached the end of input, return the nul character
    {
      currentChar = '\0';
      return currentChar;
    }
    else // otherwise, read the next input character
    {
      currentChar = textData[dataIndex++];
      column++;
    }

    if(currentChar == '\n') // if it's a newline, move the pointer to the next line
    {
      previousLineLength = column;
      line++;
      column = 0;
    }
    else if(currentChar == '\r')
    {
      // if it's a carriage return from a CRLF pair, skip over the carriage return. don't look at the array if it's
      // a pushback character because the data won't necessarily match
      if(!wasPushback && dataIndex < textData.Length && textData[dataIndex] == '\n')
      {
        dataIndex++;
      }
      // in any case, treat the carriage return like a newline
      currentChar = '\n';
      previousLineLength = column;
      line++;
      column = 0;
    }
    // if it's an embedded nul character, convert it to a space (we're using nul characters to signal EOF)
    else if(currentChar == '\0')
    {
      currentChar = ' ';
    }

    return currentChar;
  }

  /// <summary>Advances to the next input stream and calls <see cref="NextChar"/> on it.</summary>
  /// <returns>Returns true if <see cref="CurrentStream"/> has been set to the next input source and false if all
  /// input sources have been consumed.
  /// </returns>
  protected bool NextSource()
  {
    int maxSources = sources == null ? sourceNames.Length : sources.Length;
    if(sourceIndex == maxSources) return false; // if we've already consumed all the sources, return false

    sourceIndex++;
    if(sourceIndex >= maxSources) // if there no more sources, return false
    {
      textData = null;
      return false;
    }
    else // otherwise, there are still sources...
    {
      if(sources == null) // if they weren't provided in the constructor, load the next source by name
      {
        using(TextReader reader = LoadSource(sourceNames[sourceIndex]))
        {
          textData = reader.ReadToEnd();
        }
      }
      else // otherwise use what the user provided
      {
        textData = sources[sourceIndex].ReadToEnd();
      }
      line = 1;
      column = previousLineLength = 0;
      pushedChar = '\0';
      NextChar();
      return true;
    }
  }

  /// <summary>Reads the next token from the input.</summary>
  /// <returns>Returns true if the next token was read and false if there are no more tokens in any input stream.</returns>
  protected abstract bool ReadToken(out Token token);

  /// <summary>Skips over whitespace.</summary>
  /// <returns>Returns the next non-whitespace character.</returns>
  protected char SkipWhitespace()
  {
    return SkipWhitespace(true);
  }

  /// <summary>Skips over whitespace.</summary>
  /// <param name="skipNewLines">If true, newline characters will be skipped over.</param>
  /// <returns>Returns the next non-whitespace character.</returns>
  protected char SkipWhitespace(bool skipNewLines)
  {
    while((skipNewLines || currentChar != '\n') && char.IsWhiteSpace(currentChar))
    {
      NextChar();
    }
    return currentChar;
  }

  /// <summary>Asserts that <see cref="NextSource"/> has been called and has moved to a valid source.</summary>
  void AssertValidSource()
  {
    if(textData == null)
    {
      throw new InvalidOperationException("The scanner is not positioned at a valid source.");
    }
  }
  
  /// <summary>Validates that none of the array items passed to the constructor are null.</summary>
  void ValidateSources()
  {
    if(sources != null)
    {
      foreach(TextReader reader in sources)
      {
        if(reader == null) throw new ArgumentException("A text reader was null.");
      }
    }

    if(sourceNames != null)
    {
      foreach(string name in sourceNames)
      {
        if(name == null) throw new ArgumentException("A source name was null.");
      }
    }
  }

  Queue<Token> pushedTokens;
  CompilerState state;
  string[] sourceNames;
  TextReader[] sources;
  string textData;
  int dataIndex, sourceIndex = -1, line, column, previousLineLength;
  char currentChar, pushedChar;
}
#endregion

} // namespace Scripting.AST