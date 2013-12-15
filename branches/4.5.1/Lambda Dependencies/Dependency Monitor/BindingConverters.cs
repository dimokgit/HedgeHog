using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Hardcodet.Util.Dependencies
{
  /// <summary>
  /// Provides common converter functions that can be assigned
  /// to converters of a <see cref="LambdaBinding"/>.
  /// </summary>
  public static class BindingConverters
  {
    /// <summary>
    /// Performs simple inversion of a boolean value.
    /// </summary>
    public static Func<bool, bool> BoolInversionConverter = b => !b;

    /// <summary>
    /// Converts a nullable boolean into a regular bool, and returns
    /// <c>false</c> if the nullable does not provide a value.
    /// </summary>
    public static Func<bool?, bool> NullableToBoolConverter = b => b ?? false;
    
  }
}
