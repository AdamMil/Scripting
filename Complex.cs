using System;

namespace Scripting.Runtime
{

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

  public Complex Pow(Complex power)
  {
    double real, imag;

    if(power.Real == 0 && power.Imaginary == 0)
    {
      real = 1;
      imag = 0;
    }
    else if(Real == 0 && Imaginary == 0)
    {
      if(power.Imaginary != 0 || power.Real < 0) throw new DivideByZeroException("Complex Pow(): division by zero");
      real = imag = 0;
    }
    else
    {
      double vabs = Magnitude, length = Math.Pow(vabs, power.Real), angle = Angle, phase = angle*power.Real;
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

  public Complex Pow(double power)
  {
    return Pow(new Complex(power));
  }

  public double Real, Imaginary;

  public static Complex Acos(Complex z)
  {
    return Math.PI/2 - Asin(z);
  }

  // TODO: i suspect that these naive implementations have problems with certain edge cases
  public static Complex Asin(Complex z)
  {
    Complex iz = new Complex(-z.Imaginary, z.Real);
    z = Log(iz + Sqrt(1 - z*z));
    return new Complex(z.Imaginary, -z.Real);
  }

  public static Complex Atan(Complex z)
  {
    Complex iz = new Complex(-z.Imaginary, z.Real);
    z = Log(1+iz) - Log(1-iz);
    return new Complex(z.Imaginary/2, -z.Real/2);
  }

  public static Complex Log(Complex c)
  {
    return new Complex(Math.Log(c.Magnitude), c.Angle);
  }

  public static Complex Log10(Complex c)
  {
    return new Complex(Math.Log10(c.Magnitude), c.Angle);
  }

  public static Complex Pow(double a, Complex b)
  {
    return new Complex(a).Pow(b);
  }

  public static Complex Sqrt(Complex c)
  {
    if(c.Imaginary == 0) return new Complex(Math.Sqrt(c.Real));

    double r = c.Magnitude, y = Math.Sqrt((r-c.Real)/2), x = c.Imaginary/(2*y);
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

  public static readonly Complex Zero = new Complex(0);
  public static readonly Complex  One = new Complex(1, 0);
  public static readonly Complex    I = new Complex(0, 1);
}

} // namespace Scripting.Runtime