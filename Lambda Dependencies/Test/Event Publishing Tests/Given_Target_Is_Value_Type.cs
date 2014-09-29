using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;

namespace Hardcodet.Util.Dependencies.Testing
{
  [TestFixture]
  public class Given_Target_Is_Value_Type : INotifyPropertyChanged
  {  
    private DependencyNode<bool> dependency;
    private int eventCounter = 0;

    #region SomeBool

    /// <summary>
    /// Some boolean value.
    /// </summary>
    private bool boolFlag;


    /// <summary>
    /// Some boolean value.
    /// </summary>
    public bool SomeBool
    {
      get { return boolFlag; }
      set
      {
        boolFlag = value;
        OnPropertyChanged("SomeBool");
      }
    }

    #endregion


    [SetUp]
    public void Init()
    {
      //create a dependency that contains a unary expression (NOT)
      dependency = DependencyNode.Create(() => !SomeBool);
      dependency.DependencyChanged += delegate { eventCounter++; };
    }


    [TearDown]
    public void Cleanup()
    {
      dependency.Dispose();
    }


    [Test]
    public void Changing_Value_Type_Including_Unary_Should_Trigger_Dependency()
    {
      SomeBool = true;
      SomeBool = false;
      SomeBool = true;

      Assert.AreEqual(3, eventCounter);
    }



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
    protected void OnPropertyChanged(string propertyName)
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