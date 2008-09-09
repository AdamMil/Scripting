using System;
using Scripting.AST;
using Scripting.Emit;
using Scripting.Runtime;
using NUnit.Framework;
using AdamMil.Tests;

namespace Scripting.Tests
{

[TestFixture]
public sealed class NetLispTests
{
  public NetLispTests()
  {
    ResetState();
  }

  #region 01 Scanner
  [Test]
  public void Test01Scanner()
  {
    // test nil
    Assert.AreEqual(null, RunCoreCode("nil"));
    Assert.AreEqual(null, RunCoreCode("'()"));

    // test booleans
    Assert.AreEqual(true, RunCoreCode(@"#t"));
    Assert.AreEqual(true, RunCoreCode(@"#T"));
    Assert.AreEqual(false, RunCoreCode(@"#f"));
    Assert.AreEqual(false, RunCoreCode(@"#F"));

    // test characters
    Assert.AreEqual('a', RunCoreCode(@"#\a"));
    Assert.AreEqual('A', RunCoreCode(@"#\A"));
    Assert.AreEqual('(', RunCoreCode(@"#\("));
    Assert.AreEqual(' ', RunCoreCode(@"#\   "));
    Assert.AreEqual(' ', RunCoreCode(@"#\space"));
    Assert.AreEqual((char)0, RunCoreCode(@"#\nul"));
    Assert.AreEqual((char)7, RunCoreCode(@"#\alarm"));
    Assert.AreEqual('\b', RunCoreCode(@"#\backspace"));
    Assert.AreEqual('\t', RunCoreCode(@"#\tab"));
    Assert.AreEqual('\n', RunCoreCode(@"#\linefeed"));
    Assert.AreEqual('\n', RunCoreCode(@"#\newline"));
    Assert.AreEqual('\v', RunCoreCode(@"#\vtab"));
    Assert.AreEqual('\f', RunCoreCode(@"#\page"));
    Assert.AreEqual('\r', RunCoreCode(@"#\return"));
    Assert.AreEqual((char)27, RunCoreCode(@"#\esc"));
    Assert.AreEqual('\n', RunCoreCode(@"#\xA"));
    Assert.AreEqual((char)127, RunCoreCode(@"#\delete"));
    Assert.AreEqual((char)0xFF, RunCoreCode(@"#\xFF"));
    Assert.AreEqual((char)0x3BB, RunCoreCode(@"#\x03bb"));
    Assert.AreEqual((char)0x3BB, RunCoreCode(@"#\λ"));
    Assert.AreEqual((char)0x6587, RunCoreCode(@"#\x00006587"));
    Assert.AreEqual((char)1, RunCoreCode(@"#\x000000001"));

    // test character errors
    TestError(@"#\x0001z", 505);
    TestError(@"#\λx", 502);
    TestError(@"#\alarmx", 502);
    TestError(@"#\Alarm", 502);
    TestError(@"#\(x)", 502);
    TestError(@"#\x00110000", 505);
    TestError(@"#\xD800", 505);
    TestHelpers.TestException<UndefinedVariableException>(delegate { RunCoreCode(@"#\((x)"); });

    // test strings
    Assert.AreEqual("abc", RunCoreCode(@"""abc"""));
    Assert.AreEqual("\x41bc", RunCoreCode(@"""\x41bc;"""));
    Assert.AreEqual("A\nbc", RunCoreCode("\"A\r\nbc\""));
    Assert.AreEqual("Abc", RunCoreCode("\"A\\\r\nbc\""));
    Assert.AreEqual("Abc", RunCoreCode("\"A\\   \r\nbc\""));
    Assert.AreEqual("\a\b\t\n\v\f\r\"\\", RunCoreCode(@"""\a\b\t\n\v\f\r\""\\"""));
    Assert.AreEqual(@"hello\nth""ere", RunCoreCode(@"#""hello\nth""""ere"""));
    Assert.AreEqual(@"hello\nth""ere", RunCoreCode(@"#'hello\nth""ere'"));

    // test string errors
    TestError(@"""\x41""", 506);
    TestError(@"""\x;""", 506);
    TestError(@"""\x41x;""", 506);
    TestError(@"""\x110000;""", 506);
    TestError(@"""\xD800;""", 506);
    TestError(@"""Abc", 55);
    TestError(@"""\q""", 53);
    TestError(@"#""\q", 55);

    // test numbers
    TestInt("-1", -1);
    TestInt("#e2e5", 200000);
    TestInt("#x2e5", 741);
    TestInt("#b101", 5);
    TestInt("#o52", 42);
    TestLong("10000000000", 10000000000L);
    TestLong("#e-2e10", -20000000000L);
    TestDouble("1.0", 1.0);
    TestDouble("1.", 1.0);
    TestDouble(".5", 0.5);
    TestDouble("#i5", 5.0);
    TestComplex("#i1+2i", 1, 2);
    TestComplex("#i-2i", 0, -2);
    TestComplex("-1.0+3.0e7i", -1, 30000000);
    TestRational("1/2", 1, 2);
    TestRational("#e1.753", 1753, 1000);
    TestRational("#e1753.1e-1", 17531, 100);
    TestRational("#e1.01234567890123456789", Integer.Parse("101234567890123456789"), Integer.Parse("100000000000000000000"));
    TestDouble("+nan.0", double.NaN);
    TestDouble("-nan.0", double.NaN);
    TestDouble("+inf.0", double.PositiveInfinity);
    TestDouble("-inf.0", double.NegativeInfinity);

    TestError("1/0", 501);

    // test comments
    Assert.AreEqual(42, RunCoreCode("; goodbye, world\n42"));
    Assert.AreEqual(42, RunCoreCode("#||# 42"));
    Assert.AreEqual(42, RunCoreCode("#| goodbye, world |# 42"));
    Assert.AreEqual(42, RunCoreCode("#|# goodbye, world ||# 42"));
    Assert.AreEqual(42, RunCoreCode("#| ## || goodbye, world |# 42"));
    Assert.AreEqual(42, RunCoreCode("#| #|# goodbye, world |#| |# 42"));
    Assert.AreEqual(42, RunCoreCode("42 #;50"));
    TestError("#| #| |# 42", 54);

    // test miscellaneous stuff
    TestError("#<hello>", 503);
    TestError("#q", 504);
  }
  #endregion

  #region 02 Core Language
  [Test]
  public void Test02CoreLanguage()
  {
    // test basic optimized code generation
    Assert.AreEqual(6, RunCoreCode(
      "(.options ((checked #f) (allowRedefinition #f)) (define x 5) (define foo (lambda () (+ x 1))) (foo))", true));

    // test basic read-only closure functionality, with optimized code
    Assert.AreEqual(7, RunCoreCode(@"
      (.options ((checked #f) (allowRedefinition #f))
        (define foo
          (lambda (.returns function) ((int x))
            (lambda (.returns int) ((int n))
              (+ x n))))
        (define x (foo 5))
        (x 2))", true));
    
    // test a deeply-nested read-only closures
    Assert.AreEqual(23, RunCoreCode(@"
      (.options ((checked #f) (allowRedefinition #f))
        (define foo
          (lambda ((int x))
            (lambda ((int y))
              (lambda ((int z))
                (lambda ((int n)) (+ x y z n))))))
        (define a (foo 5))
        (define b (a 6))
        (define c (b 2))
        (c 10))", true));
  }
  #endregion
  
  #region 03 Exceptions
  [Test]
  public void Test03Exceptions()
  {
    TestError("(.options ((allowRedefinition #f)) (define x 5) (set! x 6))", 306, true);

    TestHelpers.TestException<ReadOnlyVariableException>(delegate()
    {
      RunCoreCode("(.options ((allowRedefinition #f)) (define x 5))", true);
      RunCoreCode("(define x 6)", false);
    });
  }
  #endregion

  void ResetState()
  {
    CompilerState.Clear();
    CompilerState.PushNew(NetLisp.NetLispLanguage.Instance);
    TopLevel.Current = new TopLevel();
  }

  object RunCoreCode(string code)
  {
    return RunCoreCode(code, false);
  }

  object RunCoreCode(string code, bool resetFirst)
  {
    if(resetFirst) ResetState();
    else CompilerState.Current.Messages.Clear();

    ASTNode node = CompilerState.Current.Language.Parse(new System.IO.StringReader(code), "<interactive>");

    if(!CompilerState.Current.HasErrors)
    {
      CompilerState.Current.Language.Decorate(ref node, DecoratorType.Compiled);
    }

    if(CompilerState.Current.HasErrors)
    {
      foreach(OutputMessage message in CompilerState.Current.Messages)
      {
        if(message.Type == OutputMessageType.Error)
        {
          throw message.Exception == null ? new SyntaxErrorException(message) : message.Exception;
        }
      }
    }

    return assembly.GenerateSnippet(node).Run();
  }

  void TestError(string sourceCode, int errorCode)
  {
    TestError(sourceCode, errorCode, false);
  }

  void TestError(string sourceCode, int errorCode, bool resetFirst)
  {
    try
    {
      RunCoreCode(sourceCode, resetFirst);
      throw new Exception("An error was expected, but it did not occur.");
    }
    catch(SyntaxErrorException ex)
    {
      Assert.AreEqual(errorCode, ex.ErrorCode);
    }
  }

  void TestComplex(string sourceCode, double real, double imaginary)
  {
    object o = RunCoreCode(sourceCode);
    Assert.IsInstanceOfType(typeof(Complex), o);
    Assert.AreEqual((Complex)o, new Complex(real, imaginary));
  }

  void TestDouble(string sourceCode, double value)
  {
    object o = RunCoreCode(sourceCode);
    Assert.IsInstanceOfType(typeof(double), o);
    Assert.AreEqual((double)o, value);
  }

  void TestInt(string sourceCode, int value)
  {
    object o = RunCoreCode(sourceCode);
    Assert.IsInstanceOfType(typeof(int), o);
    Assert.AreEqual((int)o, value);
  }

  void TestLong(string sourceCode, long value)
  {
    object o = RunCoreCode(sourceCode);
    Assert.IsInstanceOfType(typeof(long), o);
    Assert.AreEqual((long)o, value);
  }

  void TestRational(string sourceCode, Integer numerator, Integer denominator)
  {
    object o = RunCoreCode(sourceCode);
    Assert.IsInstanceOfType(typeof(Rational), o);
    Assert.AreEqual((Rational)o, new Rational(numerator, denominator));
  }

  AssemblyGenerator assembly = new AssemblyGenerator("snippets", "snippets.dll", true);
}

} // namespace Scripting.Tests