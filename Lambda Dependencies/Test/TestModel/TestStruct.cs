using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Hardcodet.Util.Dependencies.Testing.TestModel
{
  /// <summary>
  /// A value type rather than a class.
  /// </summary>
  public struct TestStruct : INotifyPropertyChanged
  {
    #region Name

    /// <summary>
    /// The name of the item.
    /// </summary>
    private string name;


    /// <summary>
    /// The name of the item.
    /// </summary>
    public string Name
    {
      get { return name; }
      set
      {
        //ignore if values are equal
        if (value == name) return;

        name = value;
        OnPropertyChanged("Name");
      }
    }

    #endregion


    #region INotifyPropertyChanged event

    ///<summary>
    ///Occurs when a property value changes.
    ///</summary>
    public event PropertyChangedEventHandler PropertyChanged;


    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event for
    /// a given property.
    /// </summary>
    /// <param name="propertyName">The name of the changed property.</param>
    private void OnPropertyChanged(string propertyName)
    {
      //validate the property name in debug builds
      VerifyProperty(propertyName);

      if (PropertyChanged != null)
      {
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
      }
    }


    /// <summary>
    /// Verifies whether the current class provides a property with a given
    /// name. This method is only invoked in debug builds, and results in
    /// a runtime exception if the <see cref="OnPropertyChanged"/> method
    /// is being invoked with an invalid property name. This may happen if
    /// a property's name was changed but not the parameter of the property's
    /// invocation of <see cref="OnPropertyChanged"/>.
    /// </summary>
    /// <param name="propertyName">The name of the changed property.</param>
    [Conditional("DEBUG")]
    private void VerifyProperty(string propertyName)
    {
      Type type = this.GetType();

      //look for a *public* property with the specified name
      PropertyInfo pi = type.GetProperty(propertyName);
      if (pi == null)
      {
        //there is no matching property - notify the developer
        string msg = "OnPropertyChanged was invoked with invalid property name {0}: ";
        msg += "{0} is not a public property of {1}.";
        msg = String.Format(msg, propertyName, type.FullName);
        Debug.Fail(msg);
      }
    }

    #endregion
  }
}
