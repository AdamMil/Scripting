using System;

namespace Scripting.Runtime
{

public static class Ops
{
  #region Numeric operations
  public static object Divide(object a, object b)
  {
    throw new NotImplementedException();
  }

  public static object UncheckedAdd(object a, object b)
  {
    throw new NotImplementedException();
  }

  public static object UncheckedMultiply(object a, object b)
  {
    throw new NotImplementedException();
  }

  public static object UncheckedSubtract(object a, object b)
  {
    throw new NotImplementedException();
  }
  #endregion
  
  #region Type conversion
  public static object ConvertTo(object obj, Type type)
  {
    return Convert.ChangeType(obj, type);
  }
  #endregion
  
  public readonly static object[] EmptyArray = new object[0];
}

} // namespace Scripting.Runtime