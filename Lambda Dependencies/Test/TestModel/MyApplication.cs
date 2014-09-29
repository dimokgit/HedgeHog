using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace Hardcodet.Util.Dependencies.Testing.TestModel
{
  public class MyApplication : INotifyPropertyChanged
  {
    private readonly DependencyNode<string> dependency;


    #region MyStudent

    /// <summary>
    /// The application's student.
    /// </summary>
    private Student myStudent = null;

    /// <summary>
    /// The application's student.
    /// </summary>
    public Student MyStudent
    {
      get { return myStudent; }
      set
      {
        myStudent = value;
        OnPropertyChanged("MyStudent");
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


    public MyApplication()
    {
      //create a dependency on the city of the student's school
      dependency = DependencyNode.Create(() => MyStudent.School.Address.City);
      dependency.DependencyChanged += OnStudentCityChanged;
    }


    /// <summary>
    /// Invoked whenever the dependency graph between <see cref="MyStudent"/>
    /// property and the <see cref="Address.City"/> of the student's
    /// school is being changed.
    /// </summary>
    private void OnStudentCityChanged(object sender, DependencyChangeEventArgs<string> e)
    {
      //get the changed node
      DependencyNode<string> changedNode = e.ChangedNode;

      //get student name, if we have one after all
      string studentName = myStudent == null ? "[none]" : myStudent.Name;

      //get the school name
      string school = "[none]";
      if (myStudent != null && myStudent.School != null)
      {
        school = myStudent.School.SchoolName;
      }
   
      //get the city, if the dependency graph leads to a valid address
      string city = changedNode.IsChainBroken ? "[unavailable]" : changedNode.LeafValue;

      //NOTE: You can also get the leaf value through this convenience method:
      //string city = e.TryGetLeafValue("[unavailable]");

      //write student/city to console
      string msg = "Student {0} goes now to school {1} in {2}. Change reason: {3}, Changed property: {4}";
      Console.Out.WriteLine(msg, studentName, school, city, e.Reason, e.ChangedMemberName);
    }


    public void Test()
    {
      //assign a student
      MyStudent = new Student { Name = "Lucy" };
      //set a school without an address
      MyStudent.School = new School {SchoolName = "University"};
      //assign an address
      MyStudent.School.Address = new Address {City = "Redmond"};
      //assign another address instance
      MyStudent.School.Address = new Address {City = "New York"};
      //change the City property of the address
      MyStudent.School.Address.City = "Washington";
      //cut the graph by removing the school reference
      MyStudent.School = null;
      //clear the MyStudent property completely
      MyStudent = null;
    }

  }


  
}
