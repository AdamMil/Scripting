using System;

namespace Scripting.AST
{

/// <summary>The base class of all custom compiler options.</summary>
public abstract class CompilerOption
{
  public CompilerOption(string name)
  {
    Name = name;
  }

  public readonly string Name;
}

/// <summary>A class representing the options given to the compiler and the messages returned by the compiler.</summary>
public class CompilerState
{
  public CompilerState(Language language)
  {
    if(language == null) throw new ArgumentNullException();
    Language = language;
  }

  public bool HasErrors
  {
    get { return Messages.HasErrors; }
  }

  public readonly OutputMessageCollection Messages = new OutputMessageCollection();
  public readonly Language Language;
  
  public static CompilerState Current;
}

} // namespace Scripting.AST