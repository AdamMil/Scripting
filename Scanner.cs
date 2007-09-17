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

#region ScannerBase
/// <summary>Provides a helper class for implementing scanners.</summary>
/// <remarks>You are not required to use this class when you implement scanners. This class exists only to provide a
/// part of the <see cref="IScanner"/> implementation.
/// </remarks>
public abstract class ScannerBase<CompilerStateType> : IScanner where CompilerStateType : CompilerState
{
  /// <summary>
  /// Initializes the scanner with a list of source names. The source files will be loaded based on these names.
  /// </summary>
  protected ScannerBase(params string[] sourceNames)
  {
    if(sources == null) throw new ArgumentNullException();
    this.sourceNames = sourceNames;
    ValidateSources();
  }

  /// <summary>Initializes the scanner with a list of streams. The source names of the streams will be
  /// "&lt;unknown&gt;".
  /// </summary>
  /// <param name="sources"></param>
  protected ScannerBase(params TextReader[] sources)
  {
    if(sources == null) throw new ArgumentNullException();
    this.sources = sources;
    ValidateSources();
  }

  /// <summary>Initializes the scanner with a list of streams and their names.</summary>
  protected ScannerBase(TextReader[] sources, string[] sourceNames)
  {
    if(sources == null || sourceNames == null) throw new ArgumentNullException();
    if(sources.Length != sourceNames.Length)
    {
      throw new ArgumentException("Number of source names doesn't match number of sources.");
    }
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
    get { return sourceState.Char; }
  }

  /// <summary>Gets the one-based column index within the current source line.</summary>
  protected int Column
  {
    get { return sourceState.Position.Column; }
  }

  /// <summary>Gets the one-based line index within the current source file.</summary>
  protected int Line
  {
    get { return sourceState.Position.Line; }
  }
  
  /// <summary>Gets the current position within the source file.</summary>
  protected FilePosition Position
  {
    get { return sourceState.Position; }
  }

  /// <summary>Gets the compiler state passed to the constructor.</summary>
  protected CompilerStateType CompilerState
  {
    get { return (CompilerStateType)Scripting.CompilerState.Current; }
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

  /// <summary>Adds a new message using the current source name and position.</summary>
  protected void AddMessage(Diagnostic diagnostic, params object[] args)
  {
    AddMessage(diagnostic, SourceName, Position, args);
  }

  /// <summary>Adds a new message using the current source name and the given position.</summary>
  protected void AddMessage(Diagnostic diagnostic, FilePosition position, params object[] args)
  {
    AddMessage(diagnostic, SourceName, position, args);
  }

  /// <summary>Adds a new error message using the given source name and position.</summary>
  protected void AddMessage(Diagnostic diagnostic, string sourceName, FilePosition position, params object[] args)
  {
    CompilerState.Messages.Add(
      diagnostic.ToMessage(CompilerState.TreatWarningsAsErrors, sourceName, position, args));
  }

  /// <summary>Loads the next source data source if there currently is none.</summary>
  /// <returns>Returns true if a valid source is loaded, and false if no source could be loaded.</returns>
  protected bool EnsureValidSource()
  {
    return HasValidSource ? true : NextSource();
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
    
    if(sourceState.DataIndex >= textData.Length) // or, if we've reached the end of input, return the nul character
    {
      sourceState.Char = '\0';
      return sourceState.Char;
    }
    else // otherwise, read the next input character
    {
      sourceState.Char = textData[sourceState.DataIndex++];
      sourceState.Position.Column++;
    }

    if(sourceState.Char == '\n') // if it's a newline, move the pointer to the next line
    {
      sourceState.Position.Line++;
      sourceState.Position.Column = 0;
    }
    else if(sourceState.Char == '\r')
    {
      // if it's a carriage return from a CRLF pair, skip over the carriage return.
      if(sourceState.DataIndex < textData.Length && textData[sourceState.DataIndex] == '\n')
      {
        sourceState.DataIndex++;
      }
      // in any case, treat the carriage return like a newline
      sourceState.Char = '\n';
      sourceState.Position.Line++;
      sourceState.Position.Column = 0;
    }
    // if it's an embedded nul character, convert it to a space (we're using nul characters to signal EOF)
    else if(sourceState.Char == '\0')
    {
      sourceState.Char = ' ';
    }

    return sourceState.Char;
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
      
      sourceState.DataIndex = 0;
      sourceState.Position  = new FilePosition(1, 0); // the NextChar() will advance to the first column
      savedState = sourceState;
      NextChar();
      return true;
    }
  }

  /// <summary>Reads the next token from the input.</summary>
  /// <returns>Returns true if the next token was read and false if there are no more tokens in any input stream.</returns>
  protected abstract bool ReadToken(out Token token);

  /// <summary>Saves the state of the current source. This allows lookahead.</summary>
  /// <remarks>Characters can be read with <see cref="NextChar"/> and then <see cref="RestoreState"/> can be called to
  /// restore the position within the source to the point where this method was called. There is no stack of sources,
  /// so it's not required to call <see cref="RestoreState"/>, but you cannot push multiple states either.
  /// Note that the state cannot be saved and restored across different data sources.
  /// </remarks>
  protected void SaveState()
  {
    savedState = sourceState;
  }

  /// <summary>Restores the state of the current source to the way it was when <see cref="SaveState"/> was last called.</summary>
  /// <remarks>There is no stack of sources so it's not required to call <see cref="RestoreState"/>.</remarks>
  protected void RestoreState()
  {
    sourceState = savedState;
  }

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
    while((skipNewLines || Char != '\n') && char.IsWhiteSpace(Char))
    {
      NextChar();
    }
    return Char;
  }

  struct State
  {
    public FilePosition Position;
    public int DataIndex;
    public char Char;
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
  string[] sourceNames;
  TextReader[] sources;
  State sourceState, savedState;
  string textData;
  int sourceIndex = -1;
}
#endregion

} // namespace Scripting.AST