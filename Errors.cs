using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using Scripting.AST;

namespace Scripting
{

#region Compile-time errors
#region CompileTimeException
/// <summary>The base class of compile-time script exceptions.</summary>
public class CompileTimeException : ApplicationException
{
  public CompileTimeException(string message) : base(message) { }
  public CompileTimeException(string message, Exception innerException) : base(message, innerException) { }

  public CompileTimeException(string message, string sourceName, FilePosition position)
    : base(message)
  {
    SourceName = sourceName;
    Position   = position;
  }

  public CompileTimeException(OutputMessage message) : base(message.Message, message.Exception)
  {
    SourceName = message.SourceName;
    Position   = message.Position;
  }

  public readonly FilePosition Position;
  public readonly string SourceName;
}
#endregion

#region AmbiguousCallException
/// <summary>Thrown when a method call is ambiguous between a multiple overrides.</summary>
public class AmbiguousCallException : CompileTimeException
{
  public AmbiguousCallException(params MethodInfo[] methods) : base(ConstructMessage(methods)) { }
  
  static string ConstructMessage(MethodInfo[] methods)
  {
    StringBuilder sb = new StringBuilder("Call is ambiguous between the following methods:\n");
    foreach(MethodInfo mi in methods)
    {
      sb.Append("  ").Append(mi.DeclaringType.FullName).Append('.').Append(mi.Name).Append('(');
      bool firstParam = true;
      foreach(ParameterInfo pi in mi.GetParameters())
      {
        if(firstParam) firstParam = false;
        else sb.Append(", ");
        sb.Append(pi.ParameterType.FullName).Append(' ').Append(pi.Name);
      }
      sb.Append(")\n");
    }
    return sb.ToString();
  }
}
#endregion

#region CantApplyOperatorException
/// <summary>Thrown when an operator cannot be applied to the given operands.</summary>
public class CantApplyOperatorException : CompileTimeException
{
  public CantApplyOperatorException(string opName, Type type)
    : base("Operator '"+opName+"' cannot be applied to operand of type "+type.FullName) { }
  
  public CantApplyOperatorException(string opName, Type lhs, Type rhs)
    : base("Operator '"+opName+"' cannot be applied to operands of type '"+lhs.FullName+"' and '"+rhs.FullName+"'") { }
}
#endregion

#region SyntaxErrorException
/// <summary>The base class of syntax error exceptions.</summary>
public class SyntaxErrorException : CompileTimeException
{
  public SyntaxErrorException(string message) : base(message) { }
  public SyntaxErrorException(string message, Exception innerException) : base(message, innerException) { }
  public SyntaxErrorException(OutputMessage message) : base(message) { }
}
#endregion

#region TooManyErrorsException
/// <summary>Thrown to abort the compilation process when too many errors have occurred.</summary>
public class TooManyErrorsException : CompileTimeException
{
  public TooManyErrorsException() : base("Too many errors have occurred.") { }
}
#endregion
#endregion

#region Runtime errors
#region RuntimeScriptException
/// <summary>The base class of runtime script exceptions.</summary>
public abstract class RuntimeScriptException : ApplicationException
{
  public RuntimeScriptException() { }
  public RuntimeScriptException(string message) : base(message) { }
}
#endregion

#region UndefinedVariableException
/// <summary>Thrown when an attempt is made to access an undefined variable.</summary>
public class UndefinedVariableException : RuntimeScriptException
{
  public UndefinedVariableException() : base("An attempt was made to access an undefined variable.") { }
  public UndefinedVariableException(string variableName) : base("Use of unbound variable: " + variableName) { }
}
#endregion
#endregion

#region OutputMessage, OutputMessageType, and OutputMessageCollection
/// <summary>The type of an output message from the compiler.</summary>
public enum OutputMessageType
{
  Warning, Error
}

/// <summary>An output message from the compiler.</summary>
public class OutputMessage
{
  public OutputMessage(OutputMessageType type, string message)
  {
    this.Message = message;
    this.Type    = type;
  }
  
  public OutputMessage(OutputMessageType type, string message, string sourceName, FilePosition position)
    : this(type, message)
  {
    this.SourceName = sourceName;
    this.Position   = position;
  }

  public OutputMessage(OutputMessageType type, string message, string sourceName, FilePosition position,
                       Exception exception)
    : this(type, message, sourceName, position)
  {
    this.Exception = exception;
  }

  /// <summary>The source file name related to the error, if available.</summary>
  public string SourceName;
  /// <summary>The position within the source file related to the error, if available.</summary>
  public FilePosition Position;

  /// <summary>The message to display to the user.</summary>
  public string Message;
  /// <summary>The exception that caused this error, if any.</summary>
  public Exception Exception;
  
  /// <summary>The type of output message.</summary>
  public OutputMessageType Type;
}

/// <summary>A collection of compiler output messages.</summary>
public class OutputMessageCollection : Collection<OutputMessage>
{
  /// <summary>Gets whether or not any error messages have been added to the message collection.</summary>
  public bool HasErrors
  {
    get
    {
      foreach(OutputMessage message in this)
      {
        if(message.Type == OutputMessageType.Error)
        {
          return true;
        }
      }

      return false;
    }
  }
}
#endregion

} // namespace Scripting