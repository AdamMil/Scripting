using System;
using Scripting.Runtime;
using NUnit.Framework;
using AdamMil.Tests;

namespace Scripting.Tests
{

[TestFixture]
public sealed class ComplexRationalTests
{
  #region 01 Basics
  [Test]
  public void Test01Basics()
  {
    ComplexRational c = new ComplexRational(5, 1), nc = c.Conjugate;
    Assert.AreEqual(new ComplexRational(5).ToString(), "5+0i");
    Assert.AreEqual(new ComplexRational(-5).ToString(), "-5+0i");
    Assert.AreEqual(new ComplexRational(new Rational(5, 2), 1).ToString(), "5/2+1i");
    Assert.AreEqual(new ComplexRational(5, -1).ToString(), "5-1i");
    Assert.AreEqual(new ComplexRational(1, new Rational(-5, 2)).ToString(), "1-5/2i");
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
    ComplexRational c = new ComplexRational(5, 3);
    Assert.AreEqual(c.Angle, 0.54041950027058416);
    Assert.AreEqual(c.Magnitude, 5.8309518948453007);
    Assert.AreEqual(c.Magnitude, c.Conjugate.Magnitude);
    Assert.AreEqual(c.Conjugate.Angle, -0.54041950027058416);
    Assert.AreEqual(c.Inverse, c*ComplexRational.I);
  }
  #endregion
  
  #region 03 Operations
  [Test]
  public void Test03Operations()
  {
    ComplexRational c = new ComplexRational(5, 3);

    Assert.AreEqual(c+5, new ComplexRational(c.Real+5, c.Imaginary));
    Assert.AreEqual(c-3, new ComplexRational(c.Real-3, c.Imaginary));
    Assert.AreEqual(c*5, new ComplexRational(25, 15));
    Assert.AreEqual(c*5, 5*c);
    Assert.AreEqual(c/new ComplexRational(2, 0), new ComplexRational(2.5, 1.5));
    Assert.AreEqual(c/2, c/new ComplexRational(2, 0));
    Assert.AreEqual(2/c, new ComplexRational(new Rational(5, 17), new Rational(-3, 17)));
    Assert.AreEqual(new ComplexRational(2), new ComplexRational(2, 0));
    Assert.AreNotEqual(new ComplexRational(2), new ComplexRational(2, 1));
    Assert.AreNotEqual(new ComplexRational(2), c);
  }
  #endregion

  #region 04 Exceptions
  [Test]
  public void Test04Exceptions()
  {
    TestHelpers.TestException<DivideByZeroException>(delegate() { ComplexRational c = new ComplexRational(2) / new ComplexRational(0, 0); });
  }
  #endregion
}

} // namespace Scripting.Tests