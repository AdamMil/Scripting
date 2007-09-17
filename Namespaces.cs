namespace Scripting.AST
{

/// <summary>Maintains the view of a program's lexical scope chain at compile time.</summary>
public abstract class Namespace
{
  // look up a binding
  // create a new binding
  // delete a binding
  /* 
   * to the top-level items, the namespace should be changing at each line (eg, during the assignment of 'b', 'c' is
   * not yet defined). however, inside the lambdas, all of the top-level items are defined, so the lambda assigned to
   * 'a' should return a constant 1 (ie, the value of 'c' should be inlined). one way to go may be to process the non-
   * redefinable items first, but that may be insufficient since it also needs to work across modules...
   * 
   * (define returnsOne (lambda() b)) ; should return the constant value 1
   * (.options ((allowRedefinition #f))
   *  (define x (lambda () (set! c 4))) ; this should produce an error saying that 'c' cannot be redefined
   *  (define a (lambda () c))
   *  (define b c)
   *  (define c 1)
   *  (define d constant-value-from-another-module)) ; the constant propogation should work across modules
   * 
   * redefinable "definitions" are simply assignments to variables
   * non-redefinable definitions are actual definitions in the .NET sense
   * 
   * (define a 1) ; produces either TopLevel.Set("a", 1) or static const int a = 1; depending on whether a is
   * redefinable. of course, if it produces a static field, it will also set the top-level value to allow loosely-bound
   * lookup. (but this should be optional because our goal is to support both fully static and fully dynamic scenarios)
   * 
   * then, with this distinction, a 
   */
}

} // namespace Scripting.AST