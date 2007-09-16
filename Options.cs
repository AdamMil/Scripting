using System;
using System.Collections.Generic;

namespace Scripting
{

/// <summary>A class representing the options given to the compiler and the messages returned by the compiler.</summary>
public class CompilerState
{
  public CompilerState(Language language)
  {
    if(language == null) throw new ArgumentNullException();
    Language = language;
  }

  /// <summary>Initializes this compiler state, copying options from <paramref name="template"/>, and using the same
  /// message collection, so messages added to one state will be added to both.
  /// </summary>
  public CompilerState(CompilerState template) : this(template.Language)
  {
    Messages          = template.Messages;
    Checked           = template.Checked;
    PromoteOnOverflow = template.PromoteOnOverflow;
  }

  /// <summary>Gets whether any error messages have been added to this compiler state.</summary>
  public bool HasErrors
  {
    get { return Messages.HasErrors; }
  }

  public readonly OutputMessageCollection Messages = new OutputMessageCollection();
  public readonly Language Language;
  
  /// <summary>Whether arithmetic operations will be checked within the current region.</summary>
  public bool Checked;
  /// <summary>Whether to emit debug code or not.</summary>
  public bool Debug = true;
  /// <summary>Whether to optimize the generated code or not.</summary>
  public bool Optimize = true;
  /// <summary>Whether to promote values to a larger integer type if an overflow occurs in a checked region.
  /// For instance, int+int may be converted to long, and long+long may be converted to <see cref="Integer"/>.
  /// Enabling this option will result in less-efficient generated code, because the type of generated 
  /// </summary>
  public bool PromoteOnOverflow;
  /// <summary>Whether warnings will be treated as errors.</summary>
  public bool TreatWarningsAsErrors;

  /// <summary>Pushes a new compler state onto the stack using the language and options of the current state. The
  /// message collection will be reused for the new state, allowing messages added to either state to show up in both.
  /// </summary>
  public static CompilerState Duplicate()
  {
    return Push(Current.Language.CreateCompilerState(Current));
  }

  /// <summary>Pushes a new compiler state onto the stack and returns it.</summary>
  /// <returns>The new current <see cref="CompilerState"/>.</returns>
  public static CompilerState PushNew(Language language)
  {
    if(language == null) throw new ArgumentNullException();
    return Push(language.CreateCompilerState());
  }

  /// <summary>Pushes the given compiler state onto the stack and returns it.</summary>
  public static CompilerState Push(CompilerState state)
  {
    if(state == null) throw new ArgumentNullException();
    if(StateStack == null) StateStack = new Stack<CompilerState>();
    StateStack.Push(state);
    return state;
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

} // namespace Scripting