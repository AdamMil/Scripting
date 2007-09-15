using System;
using System.Globalization;
using Scripting.Runtime;
using NUnit.Framework;

namespace Scripting.Tests
{

[TestFixture]
public sealed class IntegerTests
{
  static readonly Integer Billion = new Integer(1000000000), Trillion = new Integer(1000000000000);
  static readonly Integer ReallyBig = Trillion * Trillion;

  #region 01 Loading
  [Test]
  public void Test01Loading()
  {
    Integer i = new Integer();
    Assert.IsTrue(i == 0);
    
    i = new Integer((int)1000);
    Assert.IsTrue(i == 1000);

    i = new Integer((uint)1000);
    Assert.IsTrue(i == (uint)1000);

    i = new Integer((long)1000);
    Assert.IsTrue(i == (long)1000);

    i = new Integer((ulong)1000);
    Assert.IsTrue(i == (ulong)1000);

    i = new Integer((int)-1000);
    Assert.IsTrue(i == -1000);

    i = new Integer((long)-1000);
    Assert.IsTrue(i == (long)-1000);

    i = new Integer(1000.0);
    Assert.IsTrue(i == 1000);

    i = new Integer(1000.5);
    Assert.IsTrue(i == 1000);

    i = new Integer(-1000.0);
    Assert.IsTrue(i == -1000);

    i = new Integer(-1000.5);
    Assert.IsTrue(i == -1000);

    i = Integer.Parse("1000");
    Assert.IsTrue(i == 1000);

    i = Integer.Parse("-1000");
    Assert.IsTrue(i == -1000);

    Assert.IsTrue(ReallyBig == Integer.Parse("1000000000000000000000000"));
    Assert.IsTrue(-ReallyBig == Integer.Parse("-1000000000000000000000000"));
  }
  #endregion
  
  #region 02 ToString
  [Test]
  public void Test02ToString()
  {
    Integer i = new Integer(1000);
    Assert.AreEqual(i.ToString(), "1000");

    i = new Integer(-1000);
    Assert.AreEqual(i.ToString(), "-1000");

    i = new Integer(long.MaxValue);
    Assert.AreEqual(i.ToString(), long.MaxValue.ToString(CultureInfo.InvariantCulture));

    i = new Integer(long.MinValue);
    Assert.AreEqual(i.ToString(), long.MinValue.ToString(CultureInfo.InvariantCulture));

    i = new Integer(ulong.MaxValue);
    Assert.AreEqual(i.ToString(), ulong.MaxValue.ToString(CultureInfo.InvariantCulture));

    i = new Integer(ulong.MinValue);
    Assert.AreEqual(i.ToString(), ulong.MinValue.ToString(CultureInfo.InvariantCulture));

    i = new Integer(1000);
    Assert.AreEqual(i.ToString(2), "1111101000");
    Assert.AreEqual(i.ToString(3), "1101001");
    Assert.AreEqual(i.ToString(8), "1750");
    Assert.AreEqual(i.ToString(16), "3E8");

    i = new Integer(1000000000) * 4000000000;
    Assert.AreEqual(i.ToString(), "4000000000000000000");
    
    Assert.AreEqual(ReallyBig.ToString(), "1000000000000000000000000");
    Assert.AreEqual((-ReallyBig).ToString(), "-1000000000000000000000000");
  }
  #endregion

  #region 03 Comparison
  [Test]
  public void Test03Comparison()
  {
    Integer i = new Integer(1000), i999 = new Integer(999), i1000 = new Integer(1000), i1001 = new Integer(1001);
    Assert.IsTrue(i >  999);
    Assert.IsTrue(i == 1000);
    Assert.IsTrue(i <  1001);
    Assert.IsTrue(i != 500);
    Assert.IsTrue(i >= 999);
    Assert.IsTrue(i >= 1000);
    Assert.IsTrue(i <= 1001);
    Assert.IsTrue(i <= 1000);

    Assert.IsTrue(i >  (uint)999);
    Assert.IsTrue(i == (uint)1000);
    Assert.IsTrue(i <  (uint)1001);
    Assert.IsTrue(i != (uint)500);
    Assert.IsTrue(i >= (uint)999);
    Assert.IsTrue(i >= (uint)1000);
    Assert.IsTrue(i <= (uint)1001);
    Assert.IsTrue(i <= (uint)1000);

    Assert.IsTrue(i >  (long)999);
    Assert.IsTrue(i == (long)1000);
    Assert.IsTrue(i <  (long)1001);
    Assert.IsTrue(i != (long)500);
    Assert.IsTrue(i >= (long)999);
    Assert.IsTrue(i >= (long)1000);
    Assert.IsTrue(i <= (long)1001);
    Assert.IsTrue(i <= (long)1000);

    Assert.IsTrue(i >  (ulong)999);
    Assert.IsTrue(i == (ulong)1000);
    Assert.IsTrue(i <  (ulong)1001);
    Assert.IsTrue(i != (ulong)500);
    Assert.IsTrue(i >= (ulong)999);
    Assert.IsTrue(i >= (ulong)1000);
    Assert.IsTrue(i <= (ulong)1001);
    Assert.IsTrue(i <= (ulong)1000);

    Assert.IsTrue(i >  i999);
    Assert.IsTrue(i == i1000);
    Assert.IsTrue(i <  i1001);
    Assert.IsTrue(i != i999);
    Assert.IsTrue(i >= i999);
    Assert.IsTrue(i >= i1000);
    Assert.IsTrue(i <= i1001);
    Assert.IsTrue(i <= i1000);
    
    i = -i;
    Assert.IsTrue(i <  -999);
    Assert.IsTrue(i == -1000);
    Assert.IsTrue(i >  -1001);
    Assert.IsTrue(i != -500);
    Assert.IsTrue(i <= -999);
    Assert.IsTrue(i <= -1000);
    Assert.IsTrue(i >= -1001);
    Assert.IsTrue(i >= -1000);

    Assert.IsTrue(i <  (long)-999);
    Assert.IsTrue(i == (long)-1000);
    Assert.IsTrue(i >  (long)-1001);
    Assert.IsTrue(i != (long)-500);
    Assert.IsTrue(i <= (long)-999);
    Assert.IsTrue(i <= (long)-1000);
    Assert.IsTrue(i >= (long)-1001);
    Assert.IsTrue(i >= (long)-1000);

    Assert.IsTrue(i <  new Integer(-999));
    Assert.IsTrue(i == new Integer(-1000));
    Assert.IsTrue(i >  new Integer(-1001));
    Assert.IsTrue(i != new Integer(-500));
    Assert.IsTrue(i <= new Integer(-999));
    Assert.IsTrue(i <= new Integer(-1000));
    Assert.IsTrue(i >= new Integer(-1001));
    Assert.IsTrue(i >= new Integer(-1000));
    
    Assert.IsTrue(i < (uint)0);
    Assert.IsTrue(i < (ulong)0);
    
    Assert.IsTrue(i < ReallyBig);
    Assert.IsTrue(ReallyBig > i);
#pragma warning disable 1718 // disable "comparison made to same variable" warning
    Assert.IsTrue(ReallyBig == ReallyBig);
#pragma warning restore 1718
    Assert.IsTrue(ReallyBig != Trillion);
    Assert.IsTrue(ReallyBig > Trillion);
    Assert.IsTrue(Trillion < ReallyBig);
  }
  #endregion
  
  #region 04 Addition & Subtraction
  [Test]
  public void Test04AdditionSubtraction()
  {
    Integer i = new Integer(uint.MaxValue);

    i += 1;
    Assert.IsTrue(i == (long)uint.MaxValue+1);
    i -= 1;
    Assert.IsTrue(i == uint.MaxValue);

    Assert.IsTrue(5+i == i+5);
    Assert.IsTrue(-5+i == i-5);
    Assert.IsTrue(5-i == 5-(long)uint.MaxValue);
    
    Assert.IsTrue(0-i == -i);
    Assert.IsTrue(i-0 == i);

    Assert.IsTrue((Billion+Billion+Billion+Billion+Billion).ToString() == "5000000000");
  }
  #endregion
  
  #region 05 Multiplication
  [Test]
  public void Test05Multiplication()
  {
    Integer i = Billion;
    Assert.IsTrue(i*1000 == Trillion);
    Assert.AreEqual((i*i*i*i*i).ToString(), "1000000000000000000000000000000000000000000000");
    Assert.AreEqual((i*i*i*i*-i).ToString(), "-1000000000000000000000000000000000000000000000");
    Assert.AreEqual((i*-i*i*-i*i).ToString(), "1000000000000000000000000000000000000000000000");
  }
  #endregion
  
  #region 06 Division
  [Test]
  public void Test06Division()
  {
    Assert.IsTrue(Billion*Billion/Billion == Billion);
    Assert.IsTrue(Trillion*Trillion/Trillion == Trillion);
    Assert.IsTrue(ReallyBig/Trillion == Trillion);
    Assert.IsTrue(1/ReallyBig == Integer.Zero);
    Assert.IsTrue(Billion/Billion == Integer.One);
    Assert.IsTrue(Billion/-Billion == Integer.MinusOne);
    Assert.IsTrue(ReallyBig/5 == Integer.Parse("200000000000000000000000"));
    Assert.IsTrue(ReallyBig/-5 == Integer.Parse("-200000000000000000000000"));
  }
  #endregion
  
  #region 07 Modulus
  [Test]
  public void Test07Modulus()
  {
    Assert.IsTrue(Billion%7 == 6);
    Assert.IsTrue(-Billion%7 == -6);
    Assert.IsTrue(Billion%-7 == 6);
    Assert.IsTrue(-Billion%-7 == -6);

    Assert.IsTrue(ReallyBig%7 == 1);
    Assert.IsTrue(ReallyBig%(Trillion-7) == 49);
  }
  #endregion
  
  #region 08 Bitwise
  [Test]
  public void Test08Bitwise()
  {
    Integer i = new Integer(0xFF);
    Assert.IsTrue((i&0xF0) == 0xF0);
    Assert.IsTrue((i|0xF00) == 0xFFF);
    Assert.IsTrue((i^0x80) == 0x7F);
    Assert.IsTrue(~i == 0xFFFFFF00);
    Assert.IsTrue(~~i == 0xFF);
    
    Assert.IsTrue((i<<32) == 0xFF00000000);
    Assert.IsTrue((i<<32>>32) == 0xFF);
    Assert.IsTrue((i<<2) == 0x3FC);
    Assert.IsTrue((i<<34) == 0x3FC00000000);
    Assert.IsTrue((i>>2) == 63);
    Assert.IsTrue((i>>7) == 1);
    Assert.IsTrue((i>>8) == 0);

    Integer j = new Integer(1, new uint[] { 0, 0xF0 });
    Assert.IsTrue((i&j) == 0);
    i <<= 32;
    Assert.IsTrue((i&j) == 0xF000000000);
    Assert.IsTrue((i|(j<<4)) == 0xFFF00000000);
    Assert.IsTrue((i^j) == 0xF00000000);
    Assert.IsTrue(~i == 0xFFFFFF00FFFFFFFF);
    Assert.IsTrue(~j == 0xFFFFFF0FFFFFFFFF);
    
    i = new Integer(-1);
    Assert.IsTrue((i|1) == -1);
    Assert.IsTrue((i&1) == 1);
    Assert.IsTrue((i^1) == -2);
    Assert.IsTrue((i^0x80000000) == 0x80000001);
  }
  #endregion
  
  #region 09 IConvertible
  [Test]
  public void Test09IConvertible()
  {
    IConvertible i = Billion;
    Assert.IsTrue(i.ToBoolean(null));
    Assert.IsFalse(((IConvertible)(Integer.One+Integer.MinusOne)).ToBoolean(null));
    Assert.IsTrue(i.ToDecimal(null) == 1000000000);
    Assert.IsTrue(i.ToDouble(null) == 1000000000);
    Assert.IsTrue(i.ToInt32(null) == 1000000000);
    Assert.IsTrue(i.ToInt64(null) == 1000000000);
    Assert.IsTrue(i.ToSingle(null) == 1000000000);
    Assert.IsTrue(i.ToUInt32(null) == 1000000000);
    Assert.IsTrue(i.ToUInt64(null) == 1000000000);
    
    Assert.IsTrue(Integer.ToDecimal(Trillion) == 1000000000000);
    Assert.IsTrue(Integer.ToDouble(Trillion) == 1000000000000);
    Assert.IsTrue(Integer.ToInt64(Trillion) == 1000000000000);
    Assert.IsTrue(Integer.ToSingle(Trillion) == 1000000000000);
    Assert.IsTrue(Integer.ToUInt64(Trillion) == 1000000000000);
    
    Assert.IsTrue(Integer.ToDouble(new Integer(double.MaxValue)) == Math.Floor(double.MaxValue));
    Assert.IsTrue(Integer.ToDouble(new Integer(double.MinValue)) == Math.Ceiling(double.MinValue));
  }
  #endregion
  
  #region 99 Miscellaneous
  [Test]
  public void Test99Miscellaneous()
  {
    Assert.IsTrue(Integer.Abs(Billion) == Billion);
    Assert.IsTrue(Integer.Abs(new Integer(-50)) == 50);

    Assert.IsTrue(Integer.Pow(Trillion, 2) == ReallyBig);

    Assert.IsTrue(Integer.GreatestCommonFactor(Billion, Trillion) == Billion);
    Assert.IsTrue(Integer.GreatestCommonFactor(new Integer(42), new Integer(12)) == 6);

    Assert.IsTrue(Integer.LeastCommonMultiple(new Integer(3), new Integer(4)) == 12);
  }
  #endregion
}

} // namespace Scripting.Tests