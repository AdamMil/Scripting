using System;
using System.Globalization;
using Scripting.Runtime;
using NUnit.Framework;

namespace Scripting.Tests
{

[TestFixture]
public sealed class RationalTests
{
  #region 01 Loading
  [Test]
  public void Test01Loading()
  {
    Rational r;

    r = new Rational((int)1000);
    Assert.IsTrue(r == 1000);

    r = new Rational((long)1000);
    Assert.IsTrue(r == (long)1000);

    r = new Rational((int)-1000);
    Assert.IsTrue(r == -1000);

    r = new Rational((long)-1000);
    Assert.IsTrue(r == (long)-1000);

    r = new Rational(1000.0);
    Assert.IsTrue(r == 1000);

    r = new Rational(1000.5);
    Assert.IsTrue(r == 1000.5);

    r = new Rational(-1000.0);
    Assert.IsTrue(r == -1000);

    r = new Rational(-1000.5);
    Assert.IsTrue(r == -1000.5);

    r = Rational.Parse("1000");
    Assert.IsTrue(r == 1000);

    r = Rational.Parse("-1000");
    Assert.IsTrue(r == -1000);

    r = new Rational(42, 12);
    Assert.IsTrue(r.Numerator == 7 && r.Denominator == 2);
    Assert.IsTrue(r == Rational.Parse(r.ToString()));
    Assert.IsTrue(r == Rational.Parse("42/12"));

    Assert.IsTrue(new Rational(0.5) == new Rational(1, 2));
  }
  #endregion

  #region 02 Comparison
  [Test]
  public void Test02Comparison()
  {
    Rational r = new Rational(1000.5), r999 = new Rational(999.5), r1000 = new Rational(1000.5), r1001 = new Rational(1001.5);
    Assert.IsTrue(r >  999);
    Assert.IsTrue(r == 1000.5);
    Assert.IsTrue(r <  1001);
    Assert.IsTrue(r != 500);
    Assert.IsTrue(r >= 999);
    Assert.IsTrue(r >= 1000.5);
    Assert.IsTrue(r <= 1001);
    Assert.IsTrue(r <= 1000.5);

    Assert.IsTrue(r >  (uint)999);
    Assert.IsTrue(r <  (uint)1001);
    Assert.IsTrue(r != (uint)500);
    Assert.IsTrue(r >= (uint)999);
    Assert.IsTrue(r <= (uint)1001);

    Assert.IsTrue(r >  (long)999);
    Assert.IsTrue(r <  (long)1001);
    Assert.IsTrue(r != (long)500);
    Assert.IsTrue(r >= (long)999);
    Assert.IsTrue(r <= (long)1001);

    Assert.IsTrue(r >  (ulong)999);
    Assert.IsTrue(r <  (ulong)1001);
    Assert.IsTrue(r != (ulong)500);
    Assert.IsTrue(r >= (ulong)999);
    Assert.IsTrue(r <= (ulong)1001);

    Assert.IsTrue(r >  r999);
    Assert.IsTrue(r == r1000);
    Assert.IsTrue(r <  r1001);
    Assert.IsTrue(r != r999);
    Assert.IsTrue(r >= r999);
    Assert.IsTrue(r >= r1000);
    Assert.IsTrue(r <= r1001);
    Assert.IsTrue(r <= r1000);
    
    r = -r;
    Assert.IsTrue(r <  -999);
    Assert.IsTrue(r == -1000.5);
    Assert.IsTrue(r >  -1001);
    Assert.IsTrue(r != -500);
    Assert.IsTrue(r <= -999);
    Assert.IsTrue(r <= -1000.5);
    Assert.IsTrue(r >= -1001);
    Assert.IsTrue(r >= -1000.5);

    Assert.IsTrue(r <  (long)-999);
    Assert.IsTrue(r >  (long)-1001);
    Assert.IsTrue(r != (long)-500);
    Assert.IsTrue(r <= (long)-999);
    Assert.IsTrue(r >= (long)-1001);

    Assert.IsTrue(r <  new Integer(-999));
    Assert.IsTrue(r >  new Integer(-1001));
    Assert.IsTrue(r != new Integer(-500));
    Assert.IsTrue(r <= new Integer(-999));
    Assert.IsTrue(r >= new Integer(-1001));

    Assert.IsTrue(r <  -r999);
    Assert.IsTrue(r == -r1000);
    Assert.IsTrue(r >  -r1001);
    Assert.IsTrue(r <= -r999);
    Assert.IsTrue(r <= -r1000);
    Assert.IsTrue(r >= -r1001);
    Assert.IsTrue(r >= -r1000);

    Assert.IsTrue(r < (uint)0);
    Assert.IsTrue(r < (ulong)0);
  }
  #endregion

  #region 03 Addition & Subtraction
  [Test]
  public void Test03AdditionSubtraction()
  {
    Rational a = new Rational(5, 2);

    a++;
    Assert.IsTrue(a == 3.5);

    a--;
    a += 1;
    Assert.IsTrue(a == 3.5);

    a -= 1;
    Assert.IsTrue(a == 2.5);
    Assert.IsTrue(a+a == 5);
    Assert.IsTrue(a+2.5 == 5);
    Assert.IsTrue(5-a == 2.5);
    Assert.IsTrue(5+a == 7.5);
    Assert.IsTrue(a-5 == -2.5);
    Assert.IsTrue(a+5 == 7.5);

    Assert.IsTrue(5+a == a+5);
    Assert.IsTrue(-5+a == a-5);
    
    Assert.IsTrue(0-a == -a);
    Assert.IsTrue(a-0 == a);

    Rational b = new Rational(3, 7);
    Assert.IsTrue(a+b == new Rational(41, 14));
    Assert.IsTrue(a-b == new Rational(29, 14));
  }
  #endregion

  #region 04 Multiplication
  [Test]
  public void Test04Multiplication()
  {
    Rational r = new Rational(3, 7);
    Assert.IsTrue(r*2 == new Rational(6, 7));
    Assert.IsTrue(r*new Rational(2, 3) == new Rational(2, 7));
    Assert.IsTrue(r*-1 == -r);
    Assert.IsTrue(r*0.5 == new Rational(3, 14));
  }
  #endregion

  #region 05 Division
  [Test]
  public void Test05Division()
  {
    Rational r = new Rational(15, 16);
    Assert.IsTrue(r/2 == new Rational(15, 32));
    Assert.IsTrue(2/r == new Rational(32, 15));
    Assert.IsTrue(r/-2 == new Rational(-15, 32));
    Assert.IsTrue(-2/r == new Rational(-32, 15));
    Assert.IsTrue(r/r == 1);
    Assert.IsTrue(1/r == new Rational(16, 15));
    Assert.IsTrue(new Rational(1) / r == 1/r);
    Assert.IsTrue(r / new Rational(4, 5) == new Rational(75, 64));
  }
  #endregion

  #region 99 Miscellaneous
  [Test]
  public void Test99Miscellaneous()
  {
    Rational r = new Rational(-4, 5);
    Assert.IsTrue(Rational.Abs(r) == new Rational(4, 5));
  }
  #endregion
}

} // namespace Scripting.Tests