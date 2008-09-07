using System;
using System.Collections.Generic;
using Scripting.Emit;

namespace Scripting.AST
{

#region Symbol
public sealed class Symbol
{
  public Symbol(VariableNode variable) : this(variable, null) { }

  public Symbol(VariableNode variable, ASTNode initialValue)
  {
    this.value    = initialValue;
    this.variable = variable;
  }

  public VariableNode DeclaringVariable
  {
    get { return variable; }
  }

  public ASTNode Value
  {
    get { return value; }
  }

  VariableNode variable;
  ASTNode value;
}
#endregion

#region LexicalScope
/// <summary>Maintains the view of a program's lexical scope chain at compile time.</summary>
public class LexicalScope
{
  public LexicalScope() { }

  public LexicalScope(LexicalScope parent)
  {
    this.parent = parent;
  }

  /// <summary>Gets this scope's parent scope, or null if this is the top-level scope.</summary>
  public LexicalScope Parent
  {
    get { return parent; }
  }

  /// <summary>Adds the given symbol to this scope, with the given name.</summary>
  public void Add(string name, Symbol symbol)
  {
    if(name == null || symbol == null) throw new ArgumentNullException();
    if(symbols.ContainsKey(name)) throw new ArgumentException("A symbol named "+name+" already exists.");
    symbols[name] = symbol;
  }

  /// <summary>Retrieves the named symbol from this scope or a parent scope, or returns null if the symbol is not
  /// defined.
  /// </summary>
  public Symbol Get(string name)
  {
    if(name == null) throw new ArgumentNullException();
    Symbol symbol;
    if(!symbols.TryGetValue(name, out symbol) && parent != null) symbol = parent.Get(name);
    return symbol;
  }

  /// <summary>Removes the named symbol from this scope.</summary>
  /// <param name="name">The name of the symbol to remove.</param>
  /// <param name="allowInheritedSymbols">If true, the symbol with the given name will be inherited if it exists in the
  /// parent scope. If false, the symbol will not be visible in this scope, even if it is visible in parent scopes.
  /// </param>
  public void Remove(string name, bool allowInheritedSymbols)
  {
    if(name == null) throw new ArgumentNullException();
    if(allowInheritedSymbols) symbols.Remove(name);
    else symbols[name] = null;
  }

  Dictionary<string, Symbol> symbols = new Dictionary<string, Symbol>();
  LexicalScope parent;

  // look up a binding
  // create a new binding
  // delete a binding
  /* 
   * regarding the following example, to the top-level items, the namespace should be changing at each line (eg, during
   * the assignment of 'b', 'c' is not yet defined). however, inside the lambdas, all of the top-level items are
   * defined, so the lambda assigned to 'a' should return a constant 1 (ie, the value of 'c' should be inlined). one
   * way to go may be to process the non-redefinable items first, but that may be insufficient since it also needs to
   * work across modules...
   * 
   * (define returnsOne (lambda() b)) ; should return the constant value 1
   * (.options ((allowRedefinition #f))
   *  (define x (lambda () (set! c 4))) ; this should produce an error saying that 'c' cannot be redefined
   *  (define a (lambda () c))
   *  (define b c)
   *  (define c 1)
   *  (define d constant-value-from-another-module)) ; the constant propogation should work across modules
   * 
   * redefinable "definitions" are simply assignments to variables. non-redefinable definitions are actual definitions
   * in the .NET sense
   * 
   * (define a 1) ; produces either TopLevel.Set("a", 1) or static const int a = 1; depending on whether a is
   * redefinable. of course, if it produces a static field, it will also set the top-level value to allow loosely-bound
   * lookup. (but this should be optional because our goal is to support both fully static and fully dynamic scenarios)
   * 
   * then, with this distinction, a 
   */
}
#endregion

} // namespace Scripting.AST