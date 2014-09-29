using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Hardcodet.Util.Dependencies.Testing.TestModel
{
  public class Course : INotifyPropertyChanged
  {
    private ObservableCollection<Student> students;

    /// <summary>
    /// The students that visit the course.
    /// </summary>
    public ObservableCollection<Student> Students
    {
      get { return students; }
      set
      {
        students = value;
        OnPropertyChanged("Students");
      }
    }


    #region IsFull

    /// <summary>
    /// A nullable boolean.
    /// </summary>
    private bool? isFull = null;


    /// <summary>
    /// A nullable boolean.
    /// </summary>
    public bool? IsFull
    {
      get { return isFull; }
      set
      {
        //ignore if values are equal
        if (value == isFull) return;

        isFull = value;
        OnPropertyChanged("IsFull");
      }
    }

    #endregion


    public Course()
    {
      Students = new ObservableCollection<Student>();
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
      if (PropertyChanged != null)
      {
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
      }
    }

    #endregion
  }
}
