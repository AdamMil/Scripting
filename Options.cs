using System;
using System.Collections.Generic;

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
  
  /// <summary>Pushes a new compler state onto the stack using the language and options of the current state.</summary>
  public static CompilerState Duplicate()
  {
    return PushNew(Current.Language);
  }

  /// <summary>Pushes a new compiler state onto the stack and returns it.</summary>
  /// <returns>The new current <see cref="CompilerState"/>.</returns>
  public static CompilerState PushNew(Language language)
  {
    if(StateStack == null) StateStack = new Stack<CompilerState>();
    StateStack.Push(language.CreateCompilerState());
    return Current;
  }
  
  /// <summary>Pops the current compiler state.</summary>
  /// <exception cref="InvalidOperationException">Thrown if the state stack for this thread is empty.</exception>
  public static void Pop()
  {
    if(StateStack == null || StateStack.Count == 0) throw new InvalidOperationException();
    StateStack.Pop();
  }

  public static CompilerState Current
  {
    get { return StateStack == null || StateStack.Count == 0 ? null : StateStack.Peek(); }
  }
  
  [ThreadStatic] static Stack<CompilerState> StateStack = new Stack<CompilerState>();
}

} // namespace Scripting.AST