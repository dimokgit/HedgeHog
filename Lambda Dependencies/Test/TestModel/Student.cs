using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace Hardcodet.Util.Dependencies.Testing.TestModel
{
  public class Student : INotifyPropertyChanged
  {
    #region Age

    /// <summary>
    /// The person's age.
    /// </summary>
    internal int age = 20;


    /// <summary>
    /// The person's age.
    /// </summary>
    public int Age
    {
      get { return age; }
      set
      {
        age = value;
        OnPropertyChanged("Age");
      }
    }

    #endregion

    #region Name

    /// <summary>
    /// Person name.
    /// </summary>
    internal string name = string.Empty;


    /// <summary>
    /// Person name.
    /// </summary>
    public string Name
    {
      get { return name; }
      set
      {
        name = value;
        OnPropertyChanged("Name");
      }
    }

    #endregion

    #region School

    /// <summary>
    /// The student's school.
    /// </summary>
    internal School school = null;


    /// <summary>
    /// The student's school.
    /// </summary>
    public School School
    {
      get { return school; }
      set
      {
        school = value;
        OnPropertyChanged("School");
      }
    }

    #endregion

    #region SchoolCity

    /// <summary>
    /// The city of the student's school. This is a redundant property
    /// that is used to validate synchronization using <see cref="LambdaBinding"/>
    /// instances.
    /// </summary>
    private string schoolCity;


    /// <summary>
    /// The city of the student's school. This is a redundant property
    /// that is used to validate synchronization using <see cref="LambdaBinding"/>
    /// instances.
    /// </summary>
    public string SchoolCity
    {
      get { return schoolCity; }
      set
      {
        //ignore if values are equal
        if (value == schoolCity) return;

        schoolCity = value;
        OnPropertyChanged("SchoolCity");
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
    protected void OnPropertyChanged(string propertyName)
    {
      if (PropertyChanged != null)
      {
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
      }
    }

    #endregion

    ~Student()
    {
      FinalizeCounter++;
    }

    public static int FinalizeCounter = 0;
  }
}
