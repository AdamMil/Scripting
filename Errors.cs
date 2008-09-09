using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using Scripting.AST;
using Scripting.Emit;

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
    ErrorCode  = message.Code;
  }

  public readonly FilePosition Position;
  public readonly string SourceName;
  public readonly int ErrorCode;
}
#endregion

#region AmbiguousCallException
/// <summary>Thrown when a method call is ambiguous between a multiple overrides.</summary>
public class AmbiguousCallException : CompileTimeException
{
  public AmbiguousCallException(params IMethodInfo[] methods) : base(ConstructMessage(methods)) { }
  
  static string ConstructMessage(IMethodInfo[] methods)
  {
    StringBuilder sb = new StringBuilder("Call is ambiguous between the following methods:\n");
    foreach(IMethodInfo mi in methods)
    {
      sb.Append("  ").Append(mi.DeclaringType.FullName).Append('.').Append(mi.Name).Append('(');
      bool firstParam = true;
      foreach(IParameterInfo pi in mi.GetParameters())
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

#region ReadOnlyVariableException
/// <summary>Thrown when an attempt is made to change a read-only variable.</summary>
public class ReadOnlyVariableException : RuntimeScriptException
{
  public ReadOnlyVariableException() : base("An attempt was made to change a read-only variable.") { }
  public ReadOnlyVariableException(string variableName) : base("Variable '" + variableName + "' is read-only.") { }
}
#endregion

#region UndefinedVariableException
/// <summary>Thrown when an attempt is made to access an undefined variable.</summary>
public class UndefinedVariableException : RuntimeScriptException
{
  public UndefinedVariableException() : base("An attempt was made to access an undefined variable.") { }
  public UndefinedVariableException(string variableName) : base("Use of unbound variable '" + variableName + "'") { }
}
#endregion
#endregion

#region OutputMessage, OutputMessageType, and OutputMessageCollection
/// <summary>The type of an output message from the compiler.</summary>
public enum OutputMessageType
{
  Information, Warning, Error
}

/// <summary>An output message from the compiler.</summary>
public class OutputMessage
{
  public OutputMessage(OutputMessageType type, string message, int code)
  {
    this.Message = message;
    this.Type    = type;
    this.Code    = code;
  }

  public OutputMessage(OutputMessageType type, string message, int code, string sourceName, FilePosition position)
    : this(type, message, code)
  {
    this.SourceName = sourceName;
    this.Position   = position;
  }

  public OutputMessage(OutputMessageType type, string message, int code, string sourceName, FilePosition position,
                       Exception exception)
    : this(type, message, code, sourceName, position)
  {
    this.Exception = exception;
  }

  public override string ToString()
  {
    return string.Format("{0}({1},{2}): {3}", SourceName, Position.Line, Position.Column, Message);
  }

  /// <summary>The source file name related to the error, if available.</summary>
  public string SourceName;
  /// <summary>The position within the source file related to the error, if available.</summary>
  public FilePosition Position;

  /// <summary>The message to display to the user.</summary>
  public string Message;
  /// <summary>The exception that caused this error, if any.</summary>
  public Exception Exception;

  /// <summary>A numerical code describing the output message.</summary>
  public int Code;

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

  public void Add(Diagnostic diagnostic, params object[] args)
  {
    Add(diagnostic.ToMessage(CompilerState.Current.TreatWarningsAsErrors, args));
  }

  protected override void InsertItem(int index, OutputMessage item)
  {
    if(item == null) throw new ArgumentNullException(); // disallow null messages
    base.InsertItem(index, item);
  }

  protected override void SetItem(int index, OutputMessage item)
  {
    if(item == null) throw new ArgumentNullException(); // disallow null messages
    base.SetItem(index, item);
  }
}
#endregion

#region Diagnostic
/// <summary>This class represents a single diagnostic message, and contains static members for all valid messages.</summary>
public sealed class Diagnostic
{
  public Diagnostic(OutputMessageType type, string prefix, int code, int level, string format)
  {
    if(code < 0 || code > 9999) throw new ArgumentOutOfRangeException(); // should be a 4-digit code
    if(string.IsNullOrEmpty(format)) throw new ArgumentException();

    this.type   = type;
    this.prefix = prefix;
    this.code   = code;
    this.level  = level;
    this.format = format;
  }

  /// <summary>Gets the string prefix placed before diagnostic code, to identify the system that output it.</summary>
  public string Prefix
  {
    get { return prefix; }
  }

  /// <summary>The numeric code of this diagnostic message.</summary>
  public int Code
  {
    get { return code; }
  }

  /// <summary>The level of the diagonostic, with higher levels representing less severe issues.</summary>
  public int Level
  {
    get { return level; }
  }

  /// <summary>The format string for the diagnostic's message.</summary>
  public string Format
  {
    get { return format; }
  }

  /// <summary>The type of diagnostic message.</summary>
  public OutputMessageType Type
  {
    get { return type; }
  }

  /// <summary>Converts this diagnostic to an <see cref="OutputMessage"/>.</summary>
  /// <param name="treatWarningAsError">Whether a warning should be treated as an error.</param>
  /// <param name="args">Arguments to use when formatting the diagnostic's message.</param>
  public OutputMessage ToMessage(bool treatWarningAsError, params object[] args)
  {
    return ToMessage(treatWarningAsError, "<unknown>", new FilePosition(), args);
  }

  /// <summary>Converts this diagnostic to an <see cref="OutputMessage"/>.</summary>
  /// <param name="treatWarningAsError">Whether a warning should be treated as an error.</param>
  /// <param name="sourceName">The name of the source file to which the diagnostic applies.</param>
  /// <param name="position">The position within the source file of the construct that caused the diagnostic.</param>
  /// <param name="args">Arguments to use when formatting the diagnostic's message.</param>
  public OutputMessage ToMessage(bool treatWarningAsError,
                                 string sourceName, FilePosition position, params object[] args)
  {
    OutputMessageType type = this.type == OutputMessageType.Warning && treatWarningAsError
      ? OutputMessageType.Error : this.type;
    return new OutputMessage(type, ToString(treatWarningAsError, args), Code, sourceName, position);
  }

  /// <summary>Converts this diagnostic to a message suitable for use in an <see cref="OutputMessage"/>.</summary>
  /// <param name="treatWarningAsError">Whether a warning should be treated as an error.</param>
  /// <param name="args">Arguments to use when formatting the diagnostic's message.</param>
  public string ToString(bool treatWarningAsError, params object[] args)
  {
    string typeString;
    if(type == OutputMessageType.Error || treatWarningAsError && type == OutputMessageType.Warning)
    {
      typeString = "error";
    }
    else if(type == OutputMessageType.Warning)
    {
      typeString = "warning";
    }
    else
    {
      typeString = "information";
    }

    return string.Format(CultureInfo.InvariantCulture, "{0} {1}{2:D4}: {3}", typeString, prefix, code,
                         string.Format(format, args));
  }

  readonly string prefix, format;
  readonly OutputMessageType type;
  readonly int code, level;

  /// <summary>Returns a name for the given character suitable for insertion between single quotes.</summary>
  public static string CharLiteral(char c)
  {
    switch(c)
    {
      case '\'': return @"\'";
      case '\0': return @"\0";
      case '\a': return @"\a";
      case '\b': return @"\b";
      case '\f': return @"\f";
      case '\n': return @"\n";
      case '\r': return @"\r";
      case '\t': return @"\t";
      case '\v': return @"\v";
      default:
        return c < 32 || c > 126 ? "0x"+((int)c).ToString("X") : new string(c, 1);
    }
  }

  public static Diagnostic MakeError(string prefix, int code, string format)
  {
    return new Diagnostic(OutputMessageType.Error, prefix, code, 0, format);
  }

  public static Diagnostic MakeWarning(string prefix, int code, int level, string format)
  {
    return new Diagnostic(OutputMessageType.Warning, prefix, code, 1, format);
  }
}
#endregion

#region CoreDiagnostics
public static class CoreDiagnostics
{
  public static readonly Diagnostic InternalCompilerError     = Error(1, "Internal compiler error: {0}");
  // scanner
  public static readonly Diagnostic ExpectedHexDigit          = Error(51, "Expected a hex digit, but found '{0}'");
  public static readonly Diagnostic ExpectedLetter            = Error(52, "Expected a letter, but found '{0}'");
  public static readonly Diagnostic UnknownEscapeCharacter    = Error(53, "Unknown escape character '{0}'");
  public static readonly Diagnostic UnterminatedComment       = Error(54, "Unterminated multiline comment");
  public static readonly Diagnostic UnterminatedStringLiteral = Error(55, "Unterminated string literal");
  public static readonly Diagnostic UnexpectedCharacter       = Error(56, "Unexpected character '{0}'");
  public static readonly Diagnostic UnexpectedEOF             = Error(57, "Unexpected end of file");
  public static readonly Diagnostic ExpectedNumber            = Error(58, "Expected a number, but found '{0}'");
  // parser
  public static readonly Diagnostic UnexpectedToken           = Error(101, "Syntax error, unexpected token '{0}'");
  public static readonly Diagnostic ExpectedSyntax            = Error(102, "Syntax error, expected '{0}' but found '{1}'");
  // syntax checking
  public static readonly Diagnostic VariableRedefined         = Error(201, "A variable named '{0}' is already defined in this scope");
  public static readonly Diagnostic UnassignedVariableUsed    = Error(202, "Use of unassigned variable '{0}'");
  // semantics
  public static readonly Diagnostic MissingName               = Error(301, "The name '{0}' does not exist in the current context");
  public static readonly Diagnostic CannotConvertType         = Error(302, "Cannot convert type '{0}' to '{1}'");
  public static readonly Diagnostic WrongOperatorArity        = Error(303, "Operator '{0}' expects {1} arguments but was given {2}");
  public static readonly Diagnostic ExpectedValue             = Error(304, "A value was expected");
  public static readonly Diagnostic CannotApplyOperator       = Error(305, "Operator '{0}' cannot be applied to values of type '{1}'");
  public static readonly Diagnostic CannotApplyOperator2      = Error(305, "Operator '{0}' cannot be applied to values of type '{1}' and '{2}'");
  public static readonly Diagnostic ReadOnlyVariableAssigned  = Error(306, "Read-only variable '{0}' cannot be assigned");

  public static readonly Diagnostic VariableAssignedToSelf    = Warning(1001, "Variable assigned to itself; did you mean to assign something else?");

  static Diagnostic Error(int code, string format)
  {
    return Diagnostic.MakeError("CORE", code, format);
  }

  static Diagnostic Warning(int code, string format)
  {
    return Diagnostic.MakeWarning("CORE", code, code/1000, format); // the warning level is implied by the first digit
  }                                                                 // of the 4-digit code
}
#endregion
} // namespace Scripting