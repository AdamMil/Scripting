using System;
using Scripting.Runtime;
using NUnit.Framework;

namespace Scripting.Tests
{

[TestFixture]
public sealed class ComplexTests
{
  #region 01 Basics
  [Test]
  public void Test01Basics()
  {
    Complex c = new Complex(5, 1), nc = c.Conjugate;
    Assert.AreEqual(new Complex(5).ToString(), "5+0i");
    Assert.AreEqual(new Complex(-5).ToString(), "-5+0i");
    Assert.AreEqual(new Complex(5, 1).ToString(), "5+1i");
    Assert.AreEqual(new Complex(5, -1).ToString(), "5-1i");
#pragma warning disable 1718
    Assert.IsTrue(c == c);
#pragma warning restore 1718
    Assert.IsTrue(c != nc);
    Assert.IsTrue(c.Equals(c));
    Assert.IsFalse(c.Equals(nc));
    Assert.IsFalse(c.Equals(5));
  }
  #endregion
  
  #region 02 Properties
  [Test]
  public void Test02Properties()
  {
    Complex c = new Complex(5, 3);
    Assert.AreEqual(c.Angle, 0.54041950027058416);
    Assert.AreEqual(c.Magnitude, 5.8309518948453007);
    Assert.AreEqual(c.Magnitude, c.Conjugate.Magnitude);
    Assert.AreEqual(c.Conjugate.Angle, -0.54041950027058416);
    Assert.IsTrue(c.Inverse == c*Complex.I);
  }
  #endregion
  
  #region 03 Operations
  [Test]
  public void Test03Operations()
  {
    Complex c = new Complex(5, 3);
    Assert.IsTrue(Complex.Asin(c) == new Complex(1.0238217465117834, 2.4529137425028074));
    Assert.IsTrue(Complex.Acos(c) == new Complex(0.54697458028311319, -2.4529137425028074));
    Assert.IsTrue(Complex.Atan(c) == new Complex(-1.4236790442393028, 0.086569059179458563));
    Assert.IsTrue(Complex.Log(c) == new Complex(1.7631802623080808, 0.54041950027058416));
    Assert.IsTrue(Complex.Log10(c) == new Complex(0.7657394585211276, 0.54041950027058416));
    Assert.IsTrue(Complex.Log(c, 5) == new Complex(1.0955254928979907, 0.54041950027058416));
    Assert.IsTrue(Complex.Sqrt(c) == new Complex(2.3271175190399491, 0.644574237324647));
    Assert.IsTrue(Complex.Sqrt(new Complex(4)) == 2);
    Assert.IsTrue(Complex.Pow(c, 2) == new Complex(16, 30));
    Assert.IsTrue(Complex.Pow(c, new Complex(-3, -2)) == new Complex(0.0062676425978413705, 0.013479775343161568));
    Assert.IsTrue(Complex.Pow(2, new Complex(2, 2)) == new Complex(0.73382789897320688, 3.932110961644975));
    Assert.IsTrue(Complex.Pow(c, new Complex()) == 1);
    Assert.IsTrue(Complex.Pow(Complex.Zero, c) == Complex.Zero);

    Assert.IsTrue(Complex.Exp(new Complex(0, 0)) == 1);
    Assert.IsTrue(Complex.Exp(new Complex(1, 0)) == Math.E);
    Assert.IsTrue(Complex.Exp(new Complex(2, 0)) == Math.Exp(2));
    Assert.IsTrue(Complex.Exp(new Complex(-2, 3)) == new Complex(-0.13398091492954262, 0.019098516261135196));
    Assert.IsTrue(Complex.Sin(c) == new Complex(-9.65412547685484, 2.841692295606352));
    Assert.IsTrue(Complex.Cos(c) == new Complex(2.855815004227387, 9.606383448432581));
    Assert.IsTrue(Complex.Tan(c) == new Complex(-0.0027082358362240898, 1.0041647106948153));
    Assert.IsTrue(Complex.Sinh(c) == new Complex(-73.46062169567368, 10.472508533940392));
    Assert.IsTrue(Complex.Cosh(c) == new Complex(-73.467292212645262, 10.471557674805574));

    Assert.IsTrue(c+5 == new Complex(c.Real+5, c.Imaginary));
    Assert.IsTrue(c-3 == new Complex(c.Real-3, c.Imaginary));
    Assert.IsTrue(c*5 == new Complex(25, 15));
    Assert.IsTrue(c*5 == 5*c);
    Assert.IsTrue(c/new Complex(2, 0) == new Complex(2.5, 1.5));
    Assert.IsTrue(c/2 == c/new Complex(2, 0));
    Assert.IsTrue(2/c == new Complex(5/17.0, -3/17.0));
    Assert.IsTrue(2 == new Complex(2, 0));
    Assert.IsTrue(2 != new Complex(2, 1));
    Assert.IsTrue(c != 2);
  }
  #endregion

  #region 04 Exceptions
  [Test]
  public void Test04Exceptions()
  {
    TestException<DivideByZeroException>(delegate() { Complex c = new Complex(2) / new Complex(0, 0); });
    TestException<ArgumentOutOfRangeException>(delegate() { Complex.Pow(0, new Complex(-1, 0)); });
  }
  #endregion

  delegate void CodeBlock();
  static void TestException<T>(CodeBlock block) where T : Exception
  {
    bool threw = false;
    try { block(); }
    catch(Exception ex)
    {
      if(!typeof(T).IsAssignableFrom(ex.GetType())) throw;
      threw = true;
    }
    Assert.IsTrue(threw);
  }
}

} // namespace Scripting.Tests