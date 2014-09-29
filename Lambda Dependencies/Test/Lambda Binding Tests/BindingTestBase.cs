using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using Hardcodet.Util.Dependencies.Testing.TestModel;
using NUnit.Framework;

namespace Hardcodet.Util.Dependencies.Testing.Lambda_Binding_Tests
{
  [TestFixture]
  public abstract class BindingTestBase : INotifyPropertyChanged
  {
    #region Student

    /// <summary>
    /// The test subject.
    /// </summary>
    private Student student ;


    /// <summary>
    /// The test subject.
    /// </summary>
    public Student Student
    {
      get { return student; }
      set
      {
        student = value;
        OnPropertyChanged("Student");
      }
    }

    #endregion

    protected LambdaBinding Binding { get; set; }



    [SetUp]
    public void BaseInit()
    {
      Student = TestUtil.CreateTestStudent();

      //include the address field rather than the property!
      CreateBinding();
      Init();

      //reset finalization counters
      TestUtil.ResetFinalizationCounters();
    }


    /// <summary>
    /// Inits the <see cref="Binding"/> property. The default implementation
    /// registers a binding that updates the student's <see cref="Student.SchoolCity"/>
    /// with the <see cref="Address.City"/> of the student's school's address.
    /// </summary>
    protected virtual void CreateBinding()
    {
      Binding = LambdaBinding.BindOneWay(() => Student.School.Address.City, () => Student.SchoolCity);
    }


    [TearDown]
    public void BaseCleanup()
    {
      Student = null;
      ResetBinding();

      CleanUp();

      //force garbage collection
      GC.Collect();
      GC.WaitForPendingFinalizers();
    }


    /// <summary>
    /// Clears the <see cref="Binding"/> property.
    /// </summary>
    protected void ResetBinding()
    {
      if (Binding != null)
      {
        Binding.Dispose();
      }
      Binding = null;
    }


    /// <summary>
    /// Init method which can be overridden to
    /// provide custom initialization logic.
    /// </summary>
    protected virtual void Init()
    {

    }

    /// <summary>
    /// Default cleanup method - can be overridden to provide
    /// custom cleanup logic.
    /// </summary>
    protected virtual void CleanUp()
    {

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
