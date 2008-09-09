using System;

namespace Scripting.Runtime
{

#region Complex
public struct Complex
{
  public Complex(double real)
  {
    Real      = real;
    Imaginary = 0;
  }

  public Complex(double real, double imag)
  {
    Real      = real;
    Imaginary = imag;
  }

  public double Angle
  {
    get { return Math.Atan2(Imaginary, Real); }
  }

  public Complex Conjugate
  {
    get { return new Complex(Real, -Imaginary); }
  }

  public double Magnitude
  {
    get { return Math.Sqrt(Real*Real + Imaginary*Imaginary); }
  }
  
  public Complex Inverse
  {
    get { return new Complex(-Imaginary, Real); }
  }

  public override bool Equals(object obj)
  {
    if(!(obj is Complex)) return false;
    Complex other = (Complex)obj;
    return other.Real == Real && other.Imaginary == Imaginary;
  }

  public override int GetHashCode()
  {
    return Real.GetHashCode() ^ Imaginary.GetHashCode();
  }

  public override string ToString()
  {
    System.Text.StringBuilder sb = new System.Text.StringBuilder();
    sb.Append(Real);
    if(Imaginary >= 0) sb.Append('+');
    sb.Append(Imaginary);
    sb.Append('i');
    return sb.ToString();
  }

  public double Real, Imaginary;

  public static Complex Acos(Complex z)
  {
    return Math.PI/2 - Asin(z);
  }

  public static Complex Asin(Complex z)
  {
    z = Log(z.Inverse + Sqrt(1 - z*z));
    return new Complex(z.Imaginary, -z.Real);
  }

  public static Complex Atan(Complex z)
  {
    Complex iz = z.Inverse;
    z = Log(1+iz) - Log(1-iz);
    return new Complex(-z.Imaginary*0.5, -z.Real*0.5);
  }

  public static Complex Cos(Complex z) // cos(z) == (exp(i*z) + exp(-i*z)) / 2.
  {
    z = Exp(z.Inverse) + Exp(new Complex(z.Imaginary, -z.Real));
    return new Complex(z.Real*0.5, z.Imaginary*0.5);
  }

  public static Complex Cosh(Complex z) // cosh(z) == (exp(z) + exp(-z)) / 2
  {
    z = Exp(z) + Exp(-z);
    return new Complex(z.Real*0.5, z.Imaginary*0.5);
  }

  public static Complex Sin(Complex z) // sin(z) = (exp(i*z) - exp(-i*z)) / (2*i)
  {
    z = Exp(z.Inverse) - Exp(new Complex(z.Imaginary, -z.Real));
    return new Complex(z.Imaginary*0.5, -z.Real*0.5);
  }

  public static Complex Sinh(Complex z) // cosh(z) == (exp(z) - exp(-z)) / 2
  {
    z = Exp(z) - Exp(-z);
    return new Complex(z.Real*0.5, z.Imaginary*0.5);
  }

  public static Complex Tan(Complex z) // tan(z) == sin(z) / cos(z)
  {
    Complex left = Exp(z.Inverse), right = Exp(new Complex(z.Imaginary, -z.Real));
    Complex sinb = (left-right);
    return new Complex(sinb.Imaginary, -sinb.Real) / (left+right);
  }

  public static Complex Exp(Complex power) // exp(z) == pow(e, z)
  {
    if(power.Imaginary == 0)
    {
      return new Complex(Math.Exp(power.Real));
    }
    else
    {
      double length = Math.Exp(power.Real);
      return new Complex(length*Math.Cos(power.Imaginary), length*Math.Sin(power.Imaginary));
    }
  }

  public static Complex Log(Complex z)
  {
    return new Complex(Math.Log(z.Magnitude), z.Angle);
  }

  public static Complex Log10(Complex z)
  {
    return new Complex(Math.Log10(z.Magnitude), z.Angle);
  }

  public static Complex Log(Complex z, double newBase)
  {
    return new Complex(Math.Log(z.Magnitude, newBase), z.Angle);
  }

  public static Complex Pow(Complex z, Complex power)
  {
    double real, imag;

    if(power.Real == 0 && power.Imaginary == 0)
    {
      real = 1;
      imag = 0;
    }
    else if(z.Real == 0 && z.Imaginary == 0)
    {
      if(power.Real < 0) throw new ArgumentOutOfRangeException("Pow(): Power cannot be negative.");
      real = imag = 0;
    }
    else
    {
      double vabs = z.Magnitude, length = Math.Pow(vabs, power.Real), angle = z.Angle, phase = angle*power.Real;
      if(power.Imaginary != 0)
      {
        length /= Math.Exp(angle*power.Imaginary);
        phase += power.Imaginary * Math.Log(vabs);
      }
      real = length*Math.Cos(phase);
      imag = length*Math.Sin(phase);
    }

    return new Complex(real, imag);
  }

  public static Complex Pow(Complex z, double power)
  {
    return Pow(z, new Complex(power));
  }

  public static Complex Pow(double z, Complex power)
  {
    return Pow(new Complex(z), power);
  }

  public static Complex Sqrt(Complex z)
  {
    if(z.Imaginary == 0) return new Complex(Math.Sqrt(z.Real));

    double r = z.Magnitude, y = Math.Sqrt((r-z.Real)*0.5), x = z.Imaginary/(2*y);
    return x<0 ? new Complex(-x, -y) : new Complex(x, y);
  }

  public static Complex operator+(Complex a, Complex b) { return new Complex(a.Real+b.Real, a.Imaginary+b.Imaginary); }
  public static Complex operator+(Complex a, double b) { return new Complex(a.Real+b, a.Imaginary); }
  public static Complex operator+(double a, Complex b) { return new Complex(a+b.Real, b.Imaginary); }

  public static Complex operator-(Complex a, Complex b) { return new Complex(a.Real-b.Real, a.Imaginary-b.Imaginary); }
  public static Complex operator-(Complex a, double b) { return new Complex(a.Real-b, a.Imaginary); }
  public static Complex operator-(double a, Complex b) { return new Complex(a-b.Real, -b.Imaginary); }

  public static Complex operator*(Complex a, Complex b)
  {
    return new Complex(a.Real*b.Real - a.Imaginary*b.Imaginary, a.Real*b.Imaginary + a.Imaginary*b.Real);
  }
  public static Complex operator*(Complex a, double b) { return new Complex(a.Real*b, a.Imaginary*b); }
  public static Complex operator*(double a, Complex b) { return new Complex(a*b.Real, a*b.Imaginary); }

  public static Complex operator/(Complex a, Complex b)
  {
    double abs_breal = Math.Abs(b.Real), abs_bimag = Math.Abs(b.Imaginary);
    double real, imag;

    if(abs_breal >= abs_bimag)
    {
      if(abs_breal == 0.0) throw new DivideByZeroException("attempted complex division by zero");

      double ratio = b.Imaginary / b.Real;
      double denom = b.Real + b.Imaginary * ratio;
      real = (a.Real + a.Imaginary * ratio) / denom;
      imag = (a.Imaginary - a.Real * ratio) / denom;
    }
    else
    {
      double ratio = b.Real / b.Imaginary;
      double denom = b.Real * ratio + b.Imaginary;
      real = (a.Real * ratio + a.Imaginary) / denom;
      imag = (a.Imaginary * ratio - a.Real) / denom;
    }

    return new Complex(real, imag);
  }

  public static Complex operator/(Complex a, double b) { return new Complex(a.Real/b, a.Imaginary/b); }
  public static Complex operator/(double a, Complex b) { return new Complex(a) / b; }

  public static Complex operator-(Complex a) { return new Complex(-a.Real, -a.Imaginary); }

  public static bool operator==(Complex a, Complex b) { return a.Real == b.Real && a.Imaginary == b.Imaginary; }
  public static bool operator==(Complex a, double b) { return a.Real == b && a.Imaginary == 0; }
  public static bool operator==(double a, Complex b) { return a == b.Real && b.Imaginary == 0; }

  public static bool operator!=(Complex a, Complex b) { return a.Real != b.Real || a.Imaginary != b.Imaginary; }
  public static bool operator!=(Complex a, double b) { return a.Real != b || a.Imaginary != 0; }
  public static bool operator!=(double a, Complex b) { return a != b.Real || b.Imaginary != 0; }

  public static implicit operator Complex(float real) { return new Complex(real, 0); }
  public static implicit operator Complex(double real) { return new Complex(real, 0); }
  public static implicit operator Complex(int real) { return new Complex(real, 0); }
  public static implicit operator Complex(uint real) { return new Complex(real, 0); }

  public static readonly Complex Zero = new Complex(0);
  public static readonly Complex  One = new Complex(1, 0);
  public static readonly Complex    I = new Complex(0, 1);
}
#endregion

#region ComplexRational
public sealed class ComplexRational
{
  public ComplexRational(Rational real)
  {
    Real      = real;
    Imaginary = Rational.Zero;
  }

  public ComplexRational(Rational real, Rational imag)
  {
    Real      = real;
    Imaginary = imag;
  }

  public double Angle
  {
    get { return Math.Atan2(Imaginary.ToDouble(), Real.ToDouble()); }
  }

  public ComplexRational Conjugate
  {
    get { return new ComplexRational(Real, -Imaginary); }
  }

  public double Magnitude
  {
    get
    {
      double real = Real.ToDouble(), imaginary = Imaginary.ToDouble();
      return Math.Sqrt(real*real + imaginary*imaginary);
    }
  }

  public ComplexRational Inverse
  {
    get { return new ComplexRational(-Imaginary, Real); }
  }

  public override bool Equals(object obj)
  {
    if(!(obj is ComplexRational)) return false;
    ComplexRational other = (ComplexRational)obj;
    return other.Real == Real && other.Imaginary == Imaginary;
  }

  public override int GetHashCode()
  {
    return Real.GetHashCode() ^ Imaginary.GetHashCode();
  }

  public override string ToString()
  {
    System.Text.StringBuilder sb = new System.Text.StringBuilder();
    sb.Append(Real);
    if(Imaginary >= 0) sb.Append('+');
    sb.Append(Imaginary);
    sb.Append('i');
    return sb.ToString();
  }

  public Rational Real, Imaginary;

  public static ComplexRational operator+(ComplexRational a, ComplexRational b) 
  {
    return new ComplexRational(a.Real+b.Real, a.Imaginary+b.Imaginary); 
  }
  public static ComplexRational operator+(ComplexRational a, double b) 
  {
    return new ComplexRational(a.Real+b, a.Imaginary); 
  }
  public static ComplexRational operator+(double a, ComplexRational b) 
  {
    return new ComplexRational(a+b.Real, b.Imaginary); 
  }

  public static ComplexRational operator-(ComplexRational a, ComplexRational b)
  {
    return new ComplexRational(a.Real-b.Real, a.Imaginary-b.Imaginary); 
  }
  public static ComplexRational operator-(ComplexRational a, double b) 
  {
    return new ComplexRational(a.Real-b, a.Imaginary); 
  }
  public static ComplexRational operator-(double a, ComplexRational b) 
  {
    return new ComplexRational(a-b.Real, -b.Imaginary); 
  }

  public static ComplexRational operator*(ComplexRational a, ComplexRational b)
  {
    return new ComplexRational(a.Real*b.Real - a.Imaginary*b.Imaginary, a.Real*b.Imaginary + a.Imaginary*b.Real);
  }
  public static ComplexRational operator*(ComplexRational a, double b)
  { 
    return new ComplexRational(a.Real*b, a.Imaginary*b); 
  }
  public static ComplexRational operator*(double a, ComplexRational b) 
  { 
    return new ComplexRational(a*b.Real, a*b.Imaginary); 
  }

  public static ComplexRational operator/(ComplexRational a, ComplexRational b)
  {
    Rational abs_breal = Rational.Abs(b.Real), abs_bimag = Rational.Abs(b.Imaginary);
    Rational real, imag;

    if(abs_breal >= abs_bimag)
    {
      if(abs_breal == Rational.Zero) throw new DivideByZeroException("attempted ComplexRational division by zero");

      Rational ratio = b.Imaginary / b.Real;
      Rational denom = b.Real + b.Imaginary * ratio;
      real = (a.Real + a.Imaginary * ratio) / denom;
      imag = (a.Imaginary - a.Real * ratio) / denom;
    }
    else
    {
      Rational ratio = b.Real / b.Imaginary;
      Rational denom = b.Real * ratio + b.Imaginary;
      real = (a.Real * ratio + a.Imaginary) / denom;
      imag = (a.Imaginary * ratio - a.Real) / denom;
    }

    return new ComplexRational(real, imag);
  }

  public static ComplexRational operator/(ComplexRational a, Rational b) { return new ComplexRational(a.Real/b, a.Imaginary/b); }
  public static ComplexRational operator/(Rational a, ComplexRational b) { return new ComplexRational(a) / b; }

  public static ComplexRational operator-(ComplexRational a) { return new ComplexRational(-a.Real, -a.Imaginary); }

  public static bool operator==(ComplexRational a, ComplexRational b) { return a.Real == b.Real && a.Imaginary == b.Imaginary; }
  public static bool operator==(ComplexRational a, Rational b) { return a.Real == b && a.Imaginary == 0; }
  public static bool operator==(Rational a, ComplexRational b) { return a == b.Real && b.Imaginary == 0; }

  public static bool operator!=(ComplexRational a, ComplexRational b) { return a.Real != b.Real || a.Imaginary != b.Imaginary; }
  public static bool operator!=(ComplexRational a, Rational b) { return a.Real != b || a.Imaginary != 0; }
  public static bool operator!=(Rational a, ComplexRational b) { return a != b.Real || b.Imaginary != 0; }

  public static implicit operator ComplexRational(float real) { return new ComplexRational(real, 0); }
  public static implicit operator ComplexRational(double real) { return new ComplexRational(real, 0); }
  public static implicit operator ComplexRational(Rational real) { return new ComplexRational(real, 0); }
  public static implicit operator ComplexRational(int real) { return new ComplexRational(real, 0); }
  public static implicit operator ComplexRational(uint real) { return new ComplexRational(real, 0); }
  public static implicit operator ComplexRational(long real) { return new ComplexRational(real, 0); }
  public static implicit operator ComplexRational(ulong real) { return new ComplexRational(real, 0); }
  public static implicit operator ComplexRational(Integer real) { return new ComplexRational(real, 0); }

  public static readonly ComplexRational Zero = new ComplexRational(0);
  public static readonly ComplexRational One  = new ComplexRational(1, 0);
  public static readonly ComplexRational I    = new ComplexRational(0, 1);
}
#endregion

} // namespace Scripting.Runtime