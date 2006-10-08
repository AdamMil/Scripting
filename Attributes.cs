using System;
using System.Runtime.CompilerServices;

namespace Scripting.AST
{

/// <summary>The base class of all attributes that can be applied to AST nodes by the compiler.</summary>
public abstract class ASTAttribute : Attribute
{
}

/// <summary>Applied to function and class declarations, implies that the object should be given the specified
/// .NET name. If the name is a dotted name (A.B.C), the object will be created in the given namespace. Nested objects
/// (ie, class methods and nested classes) cannot have a dotted name.
/// </summary>
public class DotNetNameAttribute : ASTAttribute
{
  public DotNetNameAttribute(string name)
  {
    if(string.IsNullOrEmpty(name)) throw new ArgumentException("Name must not be empty.");
    Name = name;
  }
  
  public readonly string Name;
}

/// <summary>Applied to functions, implies that the function should be marked as the assembly entry point. The
/// function have a valid entry point signature.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class EntryPointAttribute : ASTAttribute
{
}

} // namespace Scripting.AST